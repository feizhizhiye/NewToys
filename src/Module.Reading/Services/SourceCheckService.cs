using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>一条书源的校验结论（界面逐行上屏 + 落库）。</summary>
public sealed class SourceCheckRow
{
    public SourceCheckRow(BookSource source, SourceCheckState state, string note)
    {
        Source = source;
        State = state;
        Note = note;
    }

    public BookSource Source { get; }

    public SourceCheckState State { get; }

    /// <summary>命中几条 / 失败原因。</summary>
    public string Note { get; }

    public string Name => Source.Name;

    public string StateText => State switch
    {
        SourceCheckState.Passed => "✅ 通过",
        SourceCheckState.NoResult => "⚠ 无结果",
        SourceCheckState.Failed => "⛔ 失败",
        SourceCheckState.Skipped => "— 跳过",
        _ => "未校验",
    };
}

/// <summary>
/// 书源校验（legado 的「校验书源」）：拿一个关键词对一批源各跑一次**搜索段**，
/// 把结论落库（通过 / 无结果 / 失败），用来一次性筛掉失效源。
/// <para>🔴 只跑搜索段：整条四段管线 × 几百个源会跑到几分钟，且「搜不到书」已足够判死一个源。</para>
/// <para>🔴 只读探测：⛔ 不写书架 / 进度 / 正文缓存，只写 <c>book_sources</c> 的三列校验结果。</para>
/// </summary>
public sealed class SourceCheckService
{
    private readonly CrawlService _crawl;
    private readonly SourceStore _store;

    public SourceCheckService(CrawlService crawl, SourceStore store)
    {
        _crawl = crawl;
        _store = store;
    }

    /// <summary>
    /// 批量校验。<paramref name="onDone"/> 每完成一条回调一次（界面逐行上屏，⛔ 不攒到全部结束）；
    /// 结论在完成的当下就落库，中途取消已跑完的那部分也留得住。
    /// </summary>
    public async Task<IReadOnlyList<SourceCheckRow>> CheckManyAsync(
        IReadOnlyList<BookSource> sources, string keyword, CancellationToken ct,
        IProgress<(int Done, int Total)>? progress = null,
        Action<SourceCheckRow>? onDone = null)
    {
        if (sources.Count == 0) return Array.Empty<SourceCheckRow>();

        var key = (keyword ?? "").Trim();
        if (key.Length == 0) throw new ArgumentException("校验需要关键词", nameof(keyword));

        // 🔴 没有搜索接口的源（只提供 exploreUrl 分类表）跑不了搜索 ⇒ 标「跳过」，
        //    不能判失败：否则「删除全部失效」会把这类好源一起删掉。
        var checkable = new List<BookSource>(sources.Count);
        var skipped = new List<SourceCheckRow>();
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.SearchUrl) || string.IsNullOrWhiteSpace(source.Search?.BookList))
            {
                const string note = "没有搜索接口（只能「发现」浏览），未参与校验";
                _store.SetCheck(source.Url, SourceCheckState.Skipped, note, DateTimeOffset.UtcNow);
                var row = new SourceCheckRow(source, SourceCheckState.Skipped, note);
                skipped.Add(row);
                onDone?.Invoke(row);
            }
            else checkable.Add(source);
        }

        if (checkable.Count == 0) return skipped;

        // 直接复用多源搜索：并发门 / 取消 / 逐源回调都是现成的
        var onSource = new Progress<SourceSearchResult>(result =>
        {
            var (state, note) = Classify(result);

            // 跑完一条就落库：中途取消，已跑完的结论也留得住
            _store.SetCheck(result.Source.Url, state, note, DateTimeOffset.UtcNow);
            onDone?.Invoke(new SourceCheckRow(result.Source, state, note));
        });

        var results = await _crawl.SearchManyAsync(checkable, key, 1, ct, progress, onSource).ConfigureAwait(false);

        return skipped.Concat(results.Select(r =>
        {
            var (state, note) = Classify(r);
            return new SourceCheckRow(r.Source, state, note);
        })).ToList();
    }

    /// <summary>把搜索结论翻成校验档。命中 ≥1 条 = 通过；通了但 0 条 = 无结果；有错 = 失败。</summary>
    public static (SourceCheckState State, string Note) Classify(SourceSearchResult result)
    {
        if (result.Error is not null)
            return (SourceCheckState.Failed, result.Error.Display);

        var count = result.Items.Count;
        if (count == 0) return (SourceCheckState.NoResult, "搜索通了但一条都没搜到（站点改版 / 关键词不对 / 需要登录）");

        var first = result.Items[0];
        var author = string.IsNullOrWhiteSpace(first.Author) ? "" : " / " + first.Author;
        return (SourceCheckState.Passed, $"命中 {count} 条 · 首条《{first.Name}》{author}");
    }
}
