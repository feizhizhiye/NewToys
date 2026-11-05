using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>书源能力判定（静态，不发请求）。（WHY: RD-025）</summary>
public static class SourceAnalyzer
{
    private static readonly string[] WebViewMarkers = { "webview", "startbrowserawait" };

    /// <summary>判定一条书源的档位与原因。</summary>
    public static (SourceCapability Capability, string Reason) Analyze(BookSource source)
    {
        // 🔴 先按类型分流：本软件只处理文本正文，图片 / 音频 / 视频源（type 1/2/3）一律不参与搜索与发现
        if (source.Type != 0)
            return (SourceCapability.Unsupported, $"类型为{source.TypeText}，本软件只支持文本源");

        // 「能搜索」与「能发现」是两条独立入口：真实书源里有一批只提供分类表、没有 searchUrl。
        var hasSearch = !string.IsNullOrWhiteSpace(source.SearchUrl)
                        && !string.IsNullOrWhiteSpace(source.Search?.BookList);
        var exploreBookList = string.IsNullOrWhiteSpace(source.Explore?.BookList)
            ? source.Search?.BookList
            : source.Explore!.BookList;
        var hasExplore = !string.IsNullOrWhiteSpace(source.ExploreUrl)
                         && !string.IsNullOrWhiteSpace(exploreBookList);

        var missing = new List<string>();
        if (!hasSearch && !hasExplore) missing.Add("搜索地址与书单");
        if (string.IsNullOrWhiteSpace(source.Toc?.ChapterList)) missing.Add("目录列表");
        if (string.IsNullOrWhiteSpace(source.Content?.Content)) missing.Add("正文规则");

        if (missing.Count > 0)
            return (SourceCapability.Incomplete, "缺 " + string.Join(" / ", missing));

        var webView = new List<string>();
        var js = new List<string>();
        var notes = new List<string>();

        if (hasSearch)
            Scan(webView, js, "搜索", source.SearchUrl, source.Search?.BookList, source.Search?.Name, source.Search?.BookUrl);
        else
            notes.Add("只能「发现」浏览（没有搜索地址）");

        if (hasExplore)
            Scan(webView, js, "发现", source.ExploreUrl, source.Explore?.BookList, source.Explore?.Name);

        Scan(webView, js, "目录", source.Toc?.ChapterList, source.Toc?.ChapterName, source.Toc?.ChapterUrl, source.Toc?.NextTocUrl);
        Scan(webView, js, "正文", source.Content?.Content, source.Content?.NextContentUrl);

        if (webView.Count > 0)
            return (SourceCapability.NeedsWebView, "需 WebView：" + string.Join("、", webView.Distinct()));

        if (js.Count > 0) notes.Insert(0, "含 JS：" + string.Join("、", js.Distinct()));

        return (SourceCapability.Ready, string.Join("；", notes));
    }

    private static void Scan(List<string> webView, List<string> js, string segment, params string?[] rules)
    {
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule)) continue;

            if (WebViewMarkers.Any(marker => rule!.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                webView.Add(segment);
                return;
            }

            if (RuleSyntax.ContainsJs(rule) || HasFunctionCall(rule)) js.Add(segment);
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
