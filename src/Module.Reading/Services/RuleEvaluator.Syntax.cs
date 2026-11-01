using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NewToys.Reading.Services;

/// <summary>
/// 规则文本的**切分**（不碰文档）：<c>{{}}</c> 插值、<c>||</c> 并列、<c>&&</c> 节点链、
/// <c>##</c> 正则替换、<c>@</c> 步骤、选择器归一化。
/// <para>
/// 🔴 切分顺序 = <c>{{}}</c> → <c>||</c> → <c>&&</c> → <c>##</c>。其中 <c>##</c> 后两段是
/// <b>正则 / 替换串</b>，⛔ 不参与规则切分（写成先剥 <c>##</c> 再拆 <c>&amp;&amp;</c> 结果等价，
/// 且能避免把替换串里的 <c>&amp;&amp;</c> 当链）。
/// </para>
/// <para>🔴 <c>@js:</c> / <c>&lt;js&gt;</c> 片段**不参与任何切分** —— 那里的 <c>||</c> 是 JS 逻辑或。</para>
/// </summary>
public static class RuleSyntax
{
    /// <summary>规则里是否含 JS 片段（本批次一律标记「需 JS」并返回空值）。</summary>
    public static bool ContainsJs(string? rule)
    {
        if (string.IsNullOrEmpty(rule)) return false;
        return rule.Contains("@js:", StringComparison.OrdinalIgnoreCase)
            || rule.Contains("<js>", StringComparison.OrdinalIgnoreCase)
            || rule.Contains("</js>", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>是否为未知前缀（本批次不支持、只废该字段）。</summary>
    public static bool IsUnsupportedPrefix(string step, out string prefix)
    {
        prefix = "";
        if (step.StartsWith("@xpath:", StringComparison.OrdinalIgnoreCase)) { prefix = "@XPath:"; return true; }
        if (step.StartsWith("@regex:", StringComparison.OrdinalIgnoreCase)) { prefix = "@regex:"; return true; }
        return false;
    }

    /// <summary>
    /// <c>{{key}}</c> 插值。
    /// <para>🔴 含函数调用（<c>{{java.xxx(...)}}</c>、<c>{{(a+b)}}</c>）按 JS 处理：
    /// <paramref name="needsJs"/> 置真，调用方应当整条规则放弃。</para>
    /// </summary>
    public static string Interpolate(string rule, RuleContext ctx, out bool needsJs)
    {
        needsJs = false;
        if (string.IsNullOrEmpty(rule) || !rule.Contains("{{", StringComparison.Ordinal)) return rule;

        var sb = new StringBuilder(rule.Length);
        var i = 0;
        while (i < rule.Length)
        {
            var open = rule.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0) { sb.Append(rule, i, rule.Length - i); break; }
            var close = rule.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) { sb.Append(rule, i, rule.Length - i); break; }

            sb.Append(rule, i, open - i);
            var expr = rule.Substring(open + 2, close - open - 2).Trim();

            if (expr.IndexOfAny(new[] { '(', ')', '+', '*', '/', '\'', '"' }) >= 0)
            {
                // 函数调用 / 表达式 ⇒ 不是变量替换
                needsJs = true;
                sb.Append(rule, open, close + 2 - open);
            }
            else if (ctx.Vars.TryGetValue(expr, out var value))
            {
                sb.Append(value);
            }
            else
            {
                // 未定义变量：保留原文，交给下游按「规则不完整」报（⛔ 不静默变空串）
                sb.Append(rule, open, close + 2 - open);
            }

            i = close + 2;
        }

        return sb.ToString();
    }

    /// <summary>按 <c>||</c> 拆并列分支（顺序保留）。</summary>
    public static IReadOnlyList<string> SplitOr(string rule)
        => Split(rule, "||");

    /// <summary>按 <c>&&</c> 拆节点链（前段节点集是后段的求值根）。</summary>
    public static IReadOnlyList<string> SplitAnd(string rule)
        => Split(rule, "&&");

    private static IReadOnlyList<string> Split(string text, string separator)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        var parts = new List<string>();
        var start = 0;
        while (true)
        {
            var idx = text.IndexOf(separator, start, StringComparison.Ordinal);
            if (idx < 0) break;
            parts.Add(text.Substring(start, idx - start));
            start = idx + separator.Length;
        }
        parts.Add(text.Substring(start));
        return parts;
    }

    /// <summary>
    /// 拆 <c>##</c> 正则替换：<c>规则##正则##替换串</c>（只有一段 <c>##</c> ⇒ 替换为空 = 删除）。
    /// </summary>
    public static (string Body, string? Pattern, string? Replacement) SplitReplace(string rule)
    {
        if (string.IsNullOrEmpty(rule)) return (rule, null, null);
        var first = rule.IndexOf("##", StringComparison.Ordinal);
        if (first < 0) return (rule, null, null);

        var body = rule.Substring(0, first);
        var rest = rule.Substring(first + 2);
        var second = rest.IndexOf("##", StringComparison.Ordinal);
        return second < 0
            ? (body, rest, "")
            : (body, rest.Substring(0, second), rest.Substring(second + 2));
    }

    private static readonly string[] StepPrefixes =
    {
        "@css:", "@json:", "@put:", "@get:", "@js:", "@xpath:", "@regex:", "@var:", "@virtual:",
    };

    /// <summary>
    /// 按 <c>@</c> 拆步骤，但**保留前缀**（<c>@css:</c> 这类是段首标记，不是分隔符）。
    /// </summary>
    public static IReadOnlyList<string> SplitSteps(string part)
    {
        if (string.IsNullOrEmpty(part)) return Array.Empty<string>();

        var steps = new List<string>();
        var sb = new StringBuilder();

        for (var i = 0; i < part.Length; i++)
        {
            if (part[i] != '@') { sb.Append(part[i]); continue; }

            string? matched = null;
            foreach (var prefix in StepPrefixes)
            {
                if (string.Compare(part, i, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    matched = prefix;
                    break;
                }
            }

            if (matched is null)
            {
                // 普通分隔符：`class.x@tag.a@text`
                if (sb.Length > 0) { steps.Add(sb.ToString()); sb.Clear(); }
                continue;
            }

            if (sb.Length > 0) { steps.Add(sb.ToString()); sb.Clear(); }

            // `@put:{k:rule}` / `@get:{k}` / `@var:{…}`：花括号里可能还有 `@`，必须整段收下
            if (matched is "@put:" or "@get:" or "@var:")
            {
                var open = part.IndexOf('{', i);
                var end = open < 0 ? -1 : FindClosingBrace(part, open);
                if (end > 0)
                {
                    sb.Append(part, i, end + 1 - i);
                    i = end;
                    continue;
                }
            }

            sb.Append(part[i]);
        }

        if (sb.Length > 0) steps.Add(sb.ToString());
        return steps;
    }

    /// <summary>从 <paramref name="open"/> 处的 <c>{</c> 起找配对的 <c>}</c>（找不到 ⇒ -1）。</summary>
    private static int FindClosingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static readonly HashSet<string> AttributeSteps = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "owntext", "html", "innerhtml", "href", "src", "content", "textnodes", "all",
    };

    /// <summary>该步骤是否是属性抽取（<c>text</c> / <c>href</c> / <c>content</c> …）。</summary>
    public static bool IsAttributeStep(string step, out string attribute)
    {
        attribute = step.Trim();
        return AttributeSteps.Contains(attribute);
    }

    /// <summary>选择器步骤的解析结果：<paramref name="Selector"/> + 取哪个（<paramref name="Index"/>）+ 排除哪些。</summary>
    public readonly record struct SelectorStep(string Selector, int? Index, IReadOnlyList<int> Exclude);

    private static readonly Regex IndexMark = new(
        @"^(?<sel>.+?)(?<mark>[!:])(?<list>-?\d+(?::-?\d+)*)$", RegexOptions.Compiled);

    /// <summary>
    /// 解析选择器步骤的索引尾：
    /// <list type="bullet">
    ///   <item><c>class.x.0</c> ⇒ 取第 1 个；<c>class.x.-1</c> ⇒ 取最后一个；</item>
    ///   <item><c>class.x!0</c> ⇒ **排除**第 1 个；<c>class.x!0:1:2</c> ⇒ 排除前 3 个（跳过表头的常见写法）；</item>
    ///   <item><c>class.x:1:2</c> ⇒ 只取第 2、3 个。</item>
    /// </list>
    /// 🔴 <c>!</c> 是「排除」不是「倒数」—— 倒数用负数（<c>!-1</c> 排除最后一个）。
    /// </summary>
    public static SelectorStep ParseSelector(string step)
    {
        var text = step.Trim();
        if (text.Length == 0) return new SelectorStep(text, null, Array.Empty<int>());

        var marked = IndexMark.Match(text);
        if (marked.Success)
        {
            var selector = marked.Groups["sel"].Value;
            var numbers = marked.Groups["list"].Value
                .Split(':')
                .Select(part => int.TryParse(part, out var value) ? value : int.MinValue)
                .Where(value => value != int.MinValue)
                .ToList();

            if (numbers.Count == 0) return new SelectorStep(selector, null, Array.Empty<int>());

            return marked.Groups["mark"].Value == "!"
                ? new SelectorStep(selector, null, numbers)
                : new SelectorStep(selector, numbers[0], Array.Empty<int>());
        }

        // 尾部 `.N` = 正序索引（先剥索引，再判 class 语法）
        var dot = text.LastIndexOf('.');
        if (dot > 0 && int.TryParse(text.AsSpan(dot + 1), out var forward))
            return new SelectorStep(text.Substring(0, dot), forward, Array.Empty<int>());

        return new SelectorStep(text, null, Array.Empty<int>());
    }

    /// <summary>
    /// JSON 源的**单花括号插值**：<c>http://x.com/b/{$.wapBookId}.html</c> ⇒ 把字段值填进去。
    /// <para>🔴 只在上下文有 JSON 根时生效；<c>{{…}}</c> 已由 <see cref="Interpolate"/> 处理，这里跳过双括号。</para>
    /// </summary>
    public static string InterpolateJsonFields(string rule, RuleContext ctx)
    {
        if (ctx.JsonRoot is not { } root || !rule.Contains('{', StringComparison.Ordinal)) return rule;

        var sb = new StringBuilder(rule.Length);
        var i = 0;
        while (i < rule.Length)
        {
            var open = rule.IndexOf('{', i);
            if (open < 0) { sb.Append(rule, i, rule.Length - i); break; }

            if (open + 1 < rule.Length && rule[open + 1] == '{')
            {
                sb.Append(rule, i, open - i + 2);
                i = open + 2;
                continue;
            }

            var close = rule.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(rule, i, rule.Length - i); break; }

            sb.Append(rule, i, open - i);
            var path = rule.Substring(open + 1, close - open - 1).Trim();
            var items = JsonPathLite.Select(root, path);
            if (items.Count > 0)
            {
                var item = items[0];
                sb.Append(item.ValueKind == System.Text.Json.JsonValueKind.String ? item.GetString() : item.GetRawText());
            }
            i = close + 1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// legado 选择器写法转 CSS：<c>class.x</c> ⇒ <c>.x</c>，<c>tag.x</c> ⇒ <c>x</c>，<c>id.x</c> ⇒ <c>#x</c>，
    /// 其余（已带 <c>.</c> / <c>#</c> / 裸标签 / 复合选择器）原样交给 AngleSharp。
    /// </summary>
    public static string ToCss(string selector)
    {
        var text = selector.Trim();
        if (text.Length == 0) return text;

        if (text.StartsWith("class.", StringComparison.OrdinalIgnoreCase))
            return "." + text.Substring(6).Trim();
        if (text.StartsWith("tag.", StringComparison.OrdinalIgnoreCase))
            return text.Substring(4).Trim();
        if (text.StartsWith("id.", StringComparison.OrdinalIgnoreCase))
            return "#" + text.Substring(3).Trim();

        return text;
    }
}
