using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>调试里的一步（搜索 / 详情 / 目录 / 正文）。</summary>
public sealed class DebugStep
{
    public DebugStep(string name, bool ok, long elapsedMs, string summary)
    {
        Name = name;
        Ok = ok;
        ElapsedMs = elapsedMs;
        Summary = summary;
    }

    public string Name { get; }

    public bool Ok { get; }

    public long ElapsedMs { get; }

    /// <summary>结果摘要，或失败原因（「哪条规则、什么错」的原文）。</summary>
    public string Summary { get; }

    public string ElapsedText => ElapsedMs < 1000 ? $"{ElapsedMs} ms" : $"{ElapsedMs / 1000.0:0.0} s";

    public string StateText => Ok ? "✅" : "⛔";
}

/// <summary>
/// 书源试跑（legado 的「调试」）：拿一个关键词把四段管线依次跑一遍，
/// 每段给出「耗时 + 结果摘要 / 失败规则」，用来判断一个源到底断在哪一步。
/// <para>🔴 这是**只读探测**：⛔ 不写库、⛔ 不碰书架 / 进度 / 正文缓存。</para>
/// </summary>
public sealed class SourceDebugService
{
    private readonly CrawlService _crawl;

    public SourceDebugService(CrawlService crawl) => _crawl = crawl;

    public async Task<IReadOnlyList<DebugStep>> RunAsync(BookSource source, string keyword, CancellationToken ct)
    {
        var steps = new List<DebugStep>();
        var key = (keyword ?? "").Trim();

        // ---------- 1. 搜索 ----------
        var watch = Stopwatch.StartNew();
        var search = await _crawl.SearchAsync(source, key, 1, ct).ConfigureAwait(false);
        watch.Stop();

        if (search.Error is not null)
        {
            steps.Add(new DebugStep("搜索", false, watch.ElapsedMilliseconds, search.Error.Display));
            return steps;
        }

        var items = search.Value ?? Array.Empty<SearchItem>();
        steps.Add(new DebugStep("搜索", true, watch.ElapsedMilliseconds,
            $"命中 {items.Count} 条 · 首条《{items[0].Name}》{(string.IsNullOrWhiteSpace(items[0].Author) ? "" : " / " + items[0].Author)}"));

        var book = items[0];
        if (string.IsNullOrWhiteSpace(book.BookUrl))
        {
            steps.Add(new DebugStep("详情", false, 0, "首条结果没有详情地址（ruleSearch.bookUrl 为空且节点内没链接）"));
            return steps;
        }

        // ---------- 2. 详情 ----------
        ct.ThrowIfCancellationRequested();
        watch.Restart();
        var detail = await _crawl.DetailAsync(source, book.BookUrl, ct).ConfigureAwait(false);
        watch.Stop();

        if (detail.Error is not null)
        {
            steps.Add(new DebugStep("详情", false, watch.ElapsedMilliseconds, detail.Error.Display));
            return steps;
        }

        var info = detail.Value!;
        steps.Add(new DebugStep("详情", true, watch.ElapsedMilliseconds,
            $"《{info.Name}》 / {info.Author} · 目录地址 {(string.IsNullOrWhiteSpace(info.TocUrl) ? "（回退到详情页）" : "已取到")}"));

        // ---------- 3. 目录 ----------
        ct.ThrowIfCancellationRequested();
        watch.Restart();
        var toc = await _crawl.ChaptersAsync(source,
            string.IsNullOrWhiteSpace(info.TocUrl) ? book.BookUrl : info.TocUrl, ct).ConfigureAwait(false);
        watch.Stop();

        var chapters = toc.Value ?? Array.Empty<Chapter>();
        steps.Add(new DebugStep("目录", toc.Error is null, watch.ElapsedMilliseconds,
            toc.Error is null ? $"{chapters.Count} 章 · 第一章「{chapters[0].Name}」" : toc.Error.Display));

        if (toc.Error is not null || chapters.Count == 0) return steps;

        // ---------- 4. 正文 ----------
        ct.ThrowIfCancellationRequested();
        watch.Restart();
        var content = await _crawl.ContentAsync(source, chapters[0].Url, ct).ConfigureAwait(false);
        watch.Stop();

        if (content.Error is not null)
        {
            steps.Add(new DebugStep("正文", false, watch.ElapsedMilliseconds, content.Error.Display));
            return steps;
        }

        var text = (content.Value ?? "").Replace("\r", "").Replace("\n", " ").Trim();
        var preview = text.Length <= 60 ? text : text[..60] + "…";
        steps.Add(new DebugStep("正文", true, watch.ElapsedMilliseconds, $"{text.Length} 字 · {preview}"));

        return steps;
    }
}
