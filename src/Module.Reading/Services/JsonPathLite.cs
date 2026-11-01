using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace NewToys.Reading.Services;

/// <summary>
/// 极简 JSONPath 子集（<c>@json:</c> 规则的求值器）。
/// <para>
/// 支持：<c>$.a.b</c>、<c>$.a[0]</c>、<c>$.a[*].b</c>、<c>$..b</c>（递归下降）、<c>['a']</c>。
/// 过滤表达式（<c>[?(@.x)]</c>）不支持 —— 遇到即返回空，调用方按「选择器未命中」报。
/// </para>
/// <para>属性取值**自动展开数组**（<c>$.data.list.name</c> 在 list 为数组时逐元素取 name），
/// 这是书源里最常见的省略写法。</para>
/// </summary>
public static class JsonPathLite
{
    private enum TokenKind { Property, Index, Wildcard, Recursive }

    private readonly record struct Token(TokenKind Kind, string Name, int Index);

    /// <summary>按路径求值；结果里的数组元素会被展开成多项。</summary>
    public static IReadOnlyList<JsonElement> Select(JsonElement root, string path)
    {
        var tokens = Tokenize(path);
        if (tokens.Count == 0) return new[] { root };

        var current = new List<JsonElement> { root };
        foreach (var token in tokens)
        {
            var next = new List<JsonElement>();
            foreach (var element in current) Apply(element, token, next);
            if (next.Count == 0) return Array.Empty<JsonElement>();
            current = next;
        }

        // 顶层数组展开（书单 / 目录这类「根就是一个数组」的写法）
        if (current.Count == 1 && current[0].ValueKind == JsonValueKind.Array)
            return current[0].EnumerateArray().ToList();

        return current;
    }

    private static void Apply(JsonElement element, Token token, List<JsonElement> sink)
    {
        switch (token.Kind)
        {
            case TokenKind.Wildcard:
                if (element.ValueKind == JsonValueKind.Array)
                    sink.AddRange(element.EnumerateArray());
                else if (element.ValueKind == JsonValueKind.Object)
                    sink.AddRange(element.EnumerateObject().Select(p => p.Value));
                return;

            case TokenKind.Index:
                if (element.ValueKind != JsonValueKind.Array) return;
                var index = token.Index < 0 ? element.GetArrayLength() + token.Index : token.Index;
                if (index < 0 || index >= element.GetArrayLength()) return;
                sink.Add(element[index]);
                return;

            case TokenKind.Recursive:
                CollectRecursive(element, token.Name, sink);
                return;

            case TokenKind.Property:
                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in element.EnumerateArray()) Apply(child, token, sink);
                    return;
                }
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(token.Name, out var value))
                    sink.Add(value);
                return;
        }
    }

    private static void CollectRecursive(JsonElement element, string name, List<JsonElement> sink)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name)) sink.Add(property.Value);
                CollectRecursive(property.Value, name, sink);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) CollectRecursive(child, name, sink);
        }
    }

    private static List<Token> Tokenize(string path)
    {
        var tokens = new List<Token>();
        var text = (path ?? "").Trim();
        if (text.StartsWith("$", StringComparison.Ordinal)) text = text.Substring(1);

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '.')
            {
                var recursive = i + 1 < text.Length && text[i + 1] == '.';
                i += recursive ? 2 : 1;
                var name = ReadName(text, ref i);
                if (name.Length == 0) break;
                tokens.Add(recursive ? new Token(TokenKind.Recursive, name, 0) : new Token(TokenKind.Property, name, 0));
                continue;
            }

            if (c == '[')
            {
                var close = text.IndexOf(']', i);
                if (close < 0) break;
                var inner = text.Substring(i + 1, close - i - 1).Trim();
                i = close + 1;

                if (inner == "*") { tokens.Add(new Token(TokenKind.Wildcard, "", 0)); continue; }
                if (inner.StartsWith("?", StringComparison.Ordinal) || inner.StartsWith("(", StringComparison.Ordinal))
                    return new List<Token>();   // 过滤表达式不支持 ⇒ 空结果

                if (inner.Length >= 2 && (inner[0] == '\'' || inner[0] == '"'))
                {
                    tokens.Add(new Token(TokenKind.Property, inner.Trim('\'', '"', ' '), 0));
                    continue;
                }

                if (int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                {
                    tokens.Add(new Token(TokenKind.Index, "", index));
                    continue;
                }

                tokens.Add(new Token(TokenKind.Property, inner, 0));
                continue;
            }

            var bare = ReadName(text, ref i);
            if (bare.Length == 0) break;
            tokens.Add(new Token(TokenKind.Property, bare, 0));
        }

        return tokens;
    }

    private static string ReadName(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && text[i] != '.' && text[i] != '[') i++;
        return text.Substring(start, i - start).Trim();
    }
}
