using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>书架批量更新里一本书的结论。</summary>
public sealed class ShelfUpdateResult
{
    public ShelfUpdateResult(ShelfBook book, bool ok, int newCount, string latestChapter, int chapterCount, string note)
    {
        Book = book;
        Ok = ok;
        NewCount = newCount;
        LatestChapter = latestChapter;
        ChapterCount = chapterCount;
        Note = note;
    }

    public ShelfBook Book { get; }

    public bool Ok { get; }

    /// <summary>比上次多出来的章数（首次检查 = 0，只建基线）。</summary>
    public int NewCount { get; }

    public string LatestChapter { get; }

    public int ChapterCount { get; }

    /// <summary>失败原因（成功 ⇒ 空）。</summary>
    public string Note { get; }

    public string Summary => Ok
        ? (NewCount > 0
            ? $"有 {NewCount} 章更新 · 最新「{LatestChapter}」"
            : $"已是最新（共 {ChapterCount} 章）")
        : Note;
}

/// <summary>书架批量更新（legado 的「更新书架」）：回源站抓每本书的详情 + 目录， 用目录章数跟上次的基线比，把「多了几章 / 最新一章叫什么」写回 shelf。（WHY: RD-024）</summary>
public sealed class ShelfUpdateService
{
    private readonly CrawlService _crawl;
    private readonly ShelfStore _shelf;
    private readonly SourceStore _sources;
    private readonly ReadingSettingsService _settings;

    public ShelfUpdateService(CrawlService crawl, ShelfStore shelf, SourceStore sources, ReadingSettingsService settings)
    {
        _crawl = crawl;
        _shelf = shelf;
        _sources = sources;
        _settings = settings;
    }

    /// <summary>
    /// 批量检查。<paramref name="onDone"/> 每完成一本回调一次（界面逐本上屏，⛔ 不攒到全部结束）。
    /// </summary>
    public async Task<IReadOnlyList<ShelfUpdateResult>> UpdateManyAsync(
        IReadOnlyList<ShelfBook> books, CancellationToken ct,
        IProgress<(int Done, int Total)>? progress = null,
        Action<ShelfUpdateResult>? onDone = null)
    {
        if (books.Count == 0) return Array.Empty<ShelfUpdateResult>();

        var sources = new Dictionary<string, BookSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources.LoadAll()) sources[source.Url] = source;

        var concurrency = Math.Max(1, _settings.Concurrency);
        using var gate = new SemaphoreSlim(concurrency, concurrency);

        var results = new ShelfUpdateResult[books.Count];
        var finished = 0;

        var tasks = books.Select((book, index) => Task.Run(async () =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);

            ShelfUpdateResult result;
            try
            {
                result = await CheckOneAsync(book, sources, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = new ShelfUpdateResult(book, false, 0, "", 0, "已取消");
            }
            catch (Exception ex)
            {
                result = new ShelfUpdateResult(book, false, 0, "", 0, "检查异常：" + ex.Message);
            }
            finally
            {
                gate.Release();
            }

            results[index] = result;
            onDone?.Invoke(result);
            progress?.Report((Interlocked.Increment(ref finished), books.Count));
        })).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(r => r is not null).ToList();
    }

    private async Task<ShelfUpdateResult> CheckOneAsync(
        ShelfBook book, Dictionary<string, BookSource> sources, CancellationToken ct)
    {
        if (!sources.TryGetValue(book.SourceUrl, out var source))
            return Fail(book, "书源已被删除");

        ct.ThrowIfCancellationRequested();

        var detail = await _crawl.DetailAsync(source, book.BookUrl, ct).ConfigureAwait(false);
        if (detail.Error is not null) return Fail(book, detail.Error.Display);

        var info = detail.Value!;
        var tocUrl = string.IsNullOrWhiteSpace(info.TocUrl) ? book.BookUrl : info.TocUrl;

        ct.ThrowIfCancellationRequested();

        var toc = await _crawl.ChaptersAsync(source, tocUrl, ct).ConfigureAwait(false);
        if (toc.Error is not null) return Fail(book, toc.Error.Display);

        var chapters = toc.Value ?? Array.Empty<Chapter>();
        if (chapters.Count == 0) return Fail(book, "目录是空的（规则没命中或站点改版）");

        var latest = chapters[^1].Name;
        var count = chapters.Count;

        // 首次检查（ChapterCount == 0）= 只建基线，不算「有新章节」
        var newCount = book.ChapterCount > 0 ? Math.Max(0, count - book.ChapterCount) : 0;

        _shelf.SetUpdate(book.BookUrl, latest, count, newCount, null, DateTimeOffset.UtcNow);

        book.LatestChapter = latest;
        book.ChapterCount = count;
        book.NewCount = newCount;
        book.CheckedAt = DateTimeOffset.UtcNow;
        book.UpdateNote = "";

        return new ShelfUpdateResult(book, true, newCount, latest, count, "");
    }

    private ShelfUpdateResult Fail(ShelfBook book, string note)
    {
        _shelf.SetUpdate(book.BookUrl, null, 0, 0, note, DateTimeOffset.UtcNow);

        book.UpdateNote = note;
        book.CheckedAt = DateTimeOffset.UtcNow;
        book.NewCount = 0;

        return new ShelfUpdateResult(book, false, 0, "", 0, note);
    }
}
