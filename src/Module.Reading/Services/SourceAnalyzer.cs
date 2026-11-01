using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>
/// 书源能力判定（**静态**，不发请求）。
/// <para>判据只看三段（搜索 / 目录 / 正文）的规则字段：</para>
/// <list type="number">
///   <item>三段里有必填字段为空 ⇒ <see cref="SourceCapability.Incomplete"/>（连规则都不全，谈不上跑）；</item>
///   <item>三段任一含 <c>@js:</c> / <c>&lt;js&gt;</c> / 函数调用式插值 ⇒ <see cref="SourceCapability.NeedsJs"/>；</item>
///   <item>否则 ⇒ <see cref="SourceCapability.Ready"/>。</item>
/// </list>
/// </summary>
public static class SourceAnalyzer
{
    /// <summary>判定一条书源的档位与原因。</summary>
    public static (SourceCapability Capability, string Reason) Analyze(BookSource source)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(source.SearchUrl)) missing.Add("搜索地址");
        if (string.IsNullOrWhiteSpace(source.Search?.BookList)) missing.Add("搜索书单");
        if (string.IsNullOrWhiteSpace(source.Toc?.ChapterList)) missing.Add("目录列表");
        if (string.IsNullOrWhiteSpace(source.Content?.Content)) missing.Add("正文规则");

        if (missing.Count > 0)
            return (SourceCapability.Incomplete, "缺 " + string.Join(" / ", missing));

        var js = new List<string>();
        Check(js, "搜索", source.SearchUrl, source.Search?.BookList, source.Search?.Name, source.Search?.BookUrl);
        Check(js, "目录", source.Toc?.ChapterList, source.Toc?.ChapterName, source.Toc?.ChapterUrl, source.Toc?.NextTocUrl);
        Check(js, "正文", source.Content?.Content, source.Content?.NextContentUrl);

        return js.Count > 0
            ? (SourceCapability.NeedsJs, "含 JS：" + string.Join("、", js.Distinct()))
            : (SourceCapability.Ready, "");
    }

    private static void Check(List<string> sink, string segment, params string?[] rules)
    {
        foreach (var rule in rules)
            if (RuleSyntax.ContainsJs(rule) || HasFunctionCall(rule))
            {
                sink.Add(segment);
                return;
            }
    }

    /// <summary><c>{{java.xxx(...)}}</c> / <c>{{(a+b)}}</c> 这类插值要 JS 才能算。</summary>
    private static bool HasFunctionCall(string? rule)
    {
        if (string.IsNullOrEmpty(rule)) return false;
        var open = rule.IndexOf("{{", StringComparison.Ordinal);
        if (open < 0) return false;
        var close = rule.IndexOf("}}", open + 2, StringComparison.Ordinal);
        if (close < 0) return false;
        var expr = rule.Substring(open + 2, close - open - 2);
        return expr.IndexOfAny(new[] { '(', ')' }) >= 0;
    }
}
