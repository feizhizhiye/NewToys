using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace NewToys.Reading.Services;

/// <summary>
/// 书源规则求值器：把一个字段规则（字符串）在给定上下文里算出**值**。
/// <para>
/// 支持的语法元件（本批次）：<c>class.x</c> / <c>tag.y</c> / <c>id.z</c> / <c>.x</c> / 裸标签、
/// 属性 <c>text</c> / <c>ownText</c> / <c>html</c> / <c>href</c> / <c>src</c> / <c>content</c>、
/// 索引 <c>.N</c> / <c>!N</c>、<c>@css:</c> / <c>@json:</c> 前缀、<c>||</c> / <c>&amp;&amp;</c> / <c>##</c>、
/// <c>{{}}</c> 插值、<c>@put:</c> / <c>@get:</c>。
/// </para>
/// <para>
/// 🔴 未知前缀（<c>@XPath:</c> 等）与 JS 片段只**废掉当前字段并记错误**，⛔ 不抛异常、⛔ 不牵连同一条源的其它字段。
/// </para>
/// </summary>
public sealed class RuleEvaluator
{
    /// <summary>求值。</summary>
    public RuleValue Evaluate(string? rule, RuleContext ctx)
    {
        if (string.IsNullOrWhiteSpace(rule)) return RuleValue.Empty;

        if (RuleSyntax.ContainsJs(rule))
        {
            Report(ctx, "规则含 JS 片段，本版本不执行", rule!);
            return RuleValue.Empty;
        }

        var text = RuleSyntax.Interpolate(rule!, ctx, out var needsJs);
        if (needsJs)
        {
            Report(ctx, "规则含函数调用（按 JS 处理）", rule!);
            return RuleValue.Empty;
        }

        text = RuleSyntax.InterpolateJsonFields(text, ctx);

        // 插值后已经是一个完整地址（JSON 源的详情 / 封面地址常这么写）⇒ 它就是值，⛔ 不再当选择器
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("//", StringComparison.Ordinal))
            return RuleValue.FromText(text);

        foreach (var branch in RuleSyntax.SplitOr(text))
        {
            var value = EvaluateChain(branch, ctx);
            if (value.Kind != RuleValueKind.Empty) return value;
        }

        return RuleValue.Empty;
    }

    /// <summary>求值并取首个非空文本（标量字段的便捷入口）。</summary>
    public string EvaluateText(string? rule, RuleContext ctx)
    {
        var value = Evaluate(rule, ctx);
        return value.Kind switch
        {
            RuleValueKind.Json => JsonText(value),
            RuleValueKind.Nodes => value.Nodes.Count > 0 ? Clean(value.Nodes[0].TextContent) : "",
            _ => value.FirstText,
        };
    }

    /// <summary>
    /// 正文求值：命中节点集时**保留段落换行**取文本（<c>ruleContent.content</c> 常只写选择器、不带 <c>@text</c>）。
    /// </summary>
    public RuleValue EvaluateContent(string? rule, RuleContext ctx)
    {
        var value = Evaluate(rule, ctx);
        return value.Kind != RuleValueKind.Nodes
            ? value
            : RuleValue.FromTexts(value.Nodes.Select(ContentText));
    }

    private RuleValue EvaluateJson(string path, RuleContext ctx)
    {
        var anchors = new List<JsonElement>();
        if (ctx.JsonRoot is { } item) anchors.Add(item);
        else if (TryParseJson(ctx.RawText, out var document)) anchors.Add(document);

        if (anchors.Count == 0) return RuleValue.Empty;

        var items2 = anchors.SelectMany(anchor => JsonPathLite.Select(anchor, path)).ToList();
        if (items2.Count == 0) return RuleValue.Empty;

        return items2.All(IsScalar)
            ? RuleValue.FromTexts(items2.Select(ScalarText))
            : RuleValue.FromJson(items2);
    }

    // ==================== 链 / 段 / 步骤 ====================

    private RuleValue EvaluateChain(string branch, RuleContext ctx)
    {
        var (body, pattern, replacement) = RuleSyntax.SplitReplace(branch);

        RuleValue current = RuleValue.Empty;
        IReadOnlyList<IElement>? roots = null;

        foreach (var segment in RuleSyntax.SplitAnd(body))
        {
            if (string.IsNullOrWhiteSpace(segment)) continue;

            current = EvaluateSegment(segment, ctx, roots);
            if (current.Kind == RuleValueKind.Empty) return RuleValue.Empty;
            roots = current.Kind == RuleValueKind.Nodes ? current.Nodes : null;
        }

        if (pattern is not null) current = ApplyReplace(current, pattern, replacement ?? "", ctx);
        return current;
    }

    private RuleValue EvaluateSegment(string segment, RuleContext ctx, IReadOnlyList<IElement>? roots)
    {
        var steps = RuleSyntax.SplitSteps(segment);
        if (steps.Count == 0) return RuleValue.Empty;

        var nodes = roots is { Count: > 0 }
            ? new List<IElement>(roots)
            : ctx.Root is { } root ? new List<IElement> { root } : new List<IElement>();

        var jsonNodes = new List<JsonElement>();
        if (ctx.JsonRoot is { } jsonRoot) jsonNodes.Add(jsonRoot);

        var texts = new List<string>();

        foreach (var raw in steps)
        {
            var step = raw.Trim();
            if (step.Length == 0) continue;

            if (step.StartsWith("@put:", StringComparison.OrdinalIgnoreCase))
            {
                ApplyPut(step.Substring(5), ctx, nodes, jsonNodes);
                continue;
            }

            if (step.StartsWith("@get:", StringComparison.OrdinalIgnoreCase))
            {
                var key = step.Substring(5).Trim().Trim('{', '}', ' ');
                if (ctx.Store.TryGetValue(key, out var stored))
                {
                    texts.Add(stored);
                    nodes.Clear();
                    jsonNodes.Clear();
                }
                else Report(ctx, "字典里没有这个键：" + key, step);
                continue;
            }

            if (RuleSyntax.ContainsJs(step))
            {
                Report(ctx, "步骤含 JS 片段，本版本不执行", step);
                return RuleValue.Empty;
            }

            if (RuleSyntax.IsUnsupportedPrefix(step, out var prefix))
            {
                Report(ctx, "不支持的前缀 " + prefix + "，该字段作废", step);
                return RuleValue.Empty;
            }

            if (step.StartsWith("@css:", StringComparison.OrdinalIgnoreCase))
            {
                nodes = QueryAll(nodes, RuleSyntax.ToCss(step.Substring(5).Trim()));
                jsonNodes.Clear();
                continue;
            }

            // JSON 源：以 `$` 开头就是 JSON 规则（legado 不要求写 @json:）
            if (step.StartsWith("$", StringComparison.Ordinal))
            {
                var jsonValue = EvaluateJson(step, ctx);
                if (jsonValue.Kind == RuleValueKind.Json)
                {
                    jsonNodes = jsonValue.JsonItems.ToList();
                    nodes.Clear();
                }
                else if (jsonValue.Kind == RuleValueKind.Texts)
                {
                    texts.AddRange(jsonValue.Texts);
                    nodes.Clear();
                    jsonNodes.Clear();
                }
                continue;
            }

            if (step.StartsWith("@json:", StringComparison.OrdinalIgnoreCase))
            {
                if (!EvalJsonPath(step.Substring(6).Trim(), ctx, jsonNodes, texts, out var nextJson))
                    return RuleValue.Empty;
                if (nextJson is not null) jsonNodes = nextJson;
                if (texts.Count > 0) nodes.Clear();
                continue;
            }

            if (RuleSyntax.IsAttributeStep(step, out var attribute))
            {
                foreach (var node in nodes)
                {
                    var value = Extract(node, attribute, ctx);
                    if (!string.IsNullOrWhiteSpace(value)) texts.Add(value);
                }
                nodes.Clear();
                continue;
            }

            var selectorStep = RuleSyntax.ParseSelector(step);
            var css = RuleSyntax.ToCss(selectorStep.Selector);
            if (css.Length == 0) continue;

            nodes = QueryAll(nodes, css);
            if (selectorStep.Exclude.Count > 0) nodes = Drop(nodes, selectorStep.Exclude);
            if (selectorStep.Index is { } at) nodes = Pick(nodes, at);
            jsonNodes.Clear();
        }

        if (texts.Count > 0) return RuleValue.FromTexts(texts);
        if (jsonNodes.Count > 0) return RuleValue.FromJson(jsonNodes);
        return nodes.Count > 0 ? RuleValue.FromNodes(nodes) : RuleValue.Empty;
    }

    // ==================== 选择器 / JSON ====================

    private static List<IElement> QueryAll(IReadOnlyList<IElement> roots, string css)
    {
        if (css.Length == 0) return new List<IElement>();
        var result = new List<IElement>();
        foreach (var root in roots)
        {
            try { result.AddRange(root.QuerySelectorAll(css)); }
            catch { /* 非法选择器：当作没命中，⛔ 不抛 */ }
        }
        return result;
    }

    private static List<IElement> Pick(IReadOnlyList<IElement> nodes, int index)
    {
        var at = index < 0 ? nodes.Count + index : index;
        return at >= 0 && at < nodes.Count ? new List<IElement> { nodes[at] } : new List<IElement>();
    }

    /// <summary>按索引排除若干节点（<c>!0:1:2</c> 跳过表头那一行的常见写法）。</summary>
    private static List<IElement> Drop(IReadOnlyList<IElement> nodes, IReadOnlyList<int> indexes)
    {
        var drop = new HashSet<int>();
        foreach (var index in indexes) drop.Add(index < 0 ? nodes.Count + index : index);
        return nodes.Where((_, i) => !drop.Contains(i)).ToList();
    }

    private static bool EvalJsonPath(string path, RuleContext ctx, List<JsonElement> anchors, List<string> texts, out List<JsonElement>? next)
    {
        next = null;

        if (anchors.Count == 0)
        {
            if (!TryParseJson(ctx.RawText, out var document))
            {
                Report(ctx, "响应不是合法 JSON", "@json:" + path);
                return false;
            }
            anchors = new List<JsonElement> { document };
        }

        var results = new List<JsonElement>();
        foreach (var anchor in anchors) results.AddRange(JsonPathLite.Select(anchor, path));

        if (results.Count == 0) return true;

        if (results.All(IsScalar))
        {
            foreach (var item in results)
            {
                var text = ScalarText(item);
                if (!string.IsNullOrWhiteSpace(text)) texts.Add(text);
            }
            return true;
        }

        next = results;
        return true;
    }

    private static bool IsScalar(JsonElement element)
        => element.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static string ScalarText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "",
    };

    private static bool TryParseJson(string text, out JsonElement document)
    {
        document = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(text);
            document = parsed.RootElement.Clone();
            return true;
        }
        catch { return false; }
    }

    private static string JsonText(RuleValue value)
        => value.JsonItems.Count > 0 ? ScalarText(value.JsonItems[0]) : "";

    // ==================== @put ====================

    private void ApplyPut(string spec, RuleContext ctx, List<IElement> nodes, List<JsonElement> jsonNodes)
    {
        var body = spec.Trim().Trim('{', '}').Trim();
        if (body.Length == 0) return;

        RuleContext sub = nodes.Count > 0 ? ctx.ForElement(nodes[0])
            : jsonNodes.Count > 0 ? ctx.ForJson(jsonNodes[0])
            : ctx;

        foreach (var pair in body.Split(','))
        {
            var colon = pair.IndexOf(':');
            if (colon <= 0) continue;
            var key = pair.Substring(0, colon).Trim();
            var rule = pair.Substring(colon + 1).Trim();
            if (key.Length == 0 || rule.Length == 0) continue;
            ctx.Store[key] = Evaluate(rule, sub).FirstText;
        }
    }

    // ==================== ## 正则替换 ====================

    private RuleValue ApplyReplace(RuleValue value, string pattern, string replacement, RuleContext ctx)
    {
        if (string.IsNullOrEmpty(pattern)) return value;

        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
            var texts = value.Kind == RuleValueKind.Nodes
                ? value.Nodes.Select(n => Clean(n.TextContent)).ToList()
                : value.Texts.ToList();

            if (texts.Count == 0 && value.Kind == RuleValueKind.Json)
                texts = value.JsonItems.Select(ScalarText).ToList();

            return RuleValue.FromTexts(texts.Select(t => regex.Replace(t, replacement)));
        }
        catch (Exception ex)
        {
            Report(ctx, "正则替换失败：" + ex.Message, pattern);
            return value;
        }
    }

    // ==================== 属性抽取 ====================

    private static string Extract(IElement element, string attribute, RuleContext ctx)
    {
        switch (attribute.ToLowerInvariant())
        {
            case "text":
                return Clean(element.TextContent);

            case "owntext":
            case "textnodes":
                return Clean(DirectText(element));

            case "html":
                return element.OuterHtml;

            case "innerhtml":
                return element.InnerHtml;

            case "content":
                return ContentText(element);

            case "href":
                return ctx.AbsoluteUrl(element.GetAttribute("href") ?? element.GetAttribute("data-href"));

            case "src":
                return ctx.AbsoluteUrl(element.GetAttribute("src")
                                      ?? element.GetAttribute("data-src")
                                      ?? element.GetAttribute("data-original"));

            case "all":
                return Clean(element.TextContent);

            default:
                return Clean(element.GetAttribute(attribute) ?? element.TextContent);
        }
    }

    private static string DirectText(IElement element)
    {
        var sb = new StringBuilder();
        foreach (var child in element.ChildNodes)
            if (child is IText text) sb.Append(text.Data);
        return sb.ToString();
    }

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "section", "article", "blockquote", "dd", "dt",
    };

    /// <summary>正文文本：块级元素之间保留换行（<c>text</c> 会把整段压成一行，正文必须保留段落）。</summary>
    private static string ContentText(IElement element)
    {
        var sb = new StringBuilder();
        Walk(element, sb);

        var lines = sb.ToString()
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();

        var result = new StringBuilder();
        var blank = false;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (blank || result.Length == 0) continue;
                blank = true;
                continue;
            }
            if (blank) { result.Append('\n'); blank = false; }
            result.Append(line).Append('\n');
        }

        return result.ToString().TrimEnd();
    }

    private static void Walk(INode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IText text) { sb.Append(text.Data); continue; }
            if (child is not IElement element) continue;

            var block = BlockTags.Contains(element.LocalName);
            if (block) sb.Append('\n');
            Walk(element, sb);
            if (block) sb.Append('\n');
        }
    }

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>单行文本归一化：连续空白压成一个空格。</summary>
    private static string Clean(string? text)
        => string.IsNullOrWhiteSpace(text) ? "" : Spaces.Replace(text!, " ").Trim();

    private static void Report(RuleContext ctx, string message, string rule)
    {
        var shortRule = rule.Length <= 120 ? rule : rule.Substring(0, 120) + "…";
        ctx.Errors.Add(message + " → " + shortRule);
    }
}
