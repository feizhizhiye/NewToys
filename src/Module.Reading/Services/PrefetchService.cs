using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>
/// 阅读时向后预抓若干章（legado 的「预下载」）：当前章读起来的同时，后台把后面几章抓进正文缓存，
/// 翻页时直接命中缓存，不用等网络。
/// <para>
/// 🔴 不依赖「缓存正文」开关：那个开关管的是「每章读完是否顺手存」，预下载是用户显式要的「先存这几章」，
/// 所以这里自己写缓存。
/// </para>
/// <para>串行抓（一次一章）：预取是背景活，⛔ 不和用户正在读的那一章抢并发。</para>
/// </summary>
public sealed class PrefetchService
{
    private readonly CrawlService _crawl;
    private readonly ContentCacheStore _cache;

    public PrefetchService(CrawlService crawl, ContentCacheStore cache)
    {
        _crawl = crawl;
        _cache = cache;
    }

    /// <summary>预取从 <paramref name="from"/> 开始的 <paramref name="count"/> 章（已是缓存的不重抓）。返回实际新抓的章数。</summary>
    public async Task<int> RunAsync(
        BookSource source, IReadOnlyList<Chapter> chapters, int from, int count, CancellationToken ct)
    {
        if (count <= 0 || !_cache.IsAvailable) return 0;

        var fetched = 0;
        var end = Math.Min(chapters.Count, from + count);

        for (var i = Math.Max(0, from); i < end; i++)
        {
            if (ct.IsCancellationRequested) break;

            var url = chapters[i].Url;
            if (string.IsNullOrWhiteSpace(url)) continue;
            if (_cache.Get(url) is not null) continue;   // 已有缓存，跳过

            try
            {
                var result = await _crawl.ContentAsync(source, url, ct).ConfigureAwait(false);
                if (result.Error is not null) continue;

                _cache.Put(url, result.Value ?? "");
                fetched++;
            }
            catch (OperationCanceledException) { break; }
            catch { /* 预取失败不打扰阅读，翻到那章时会再正常抓一次 */ }
        }

        return fetched;
    }
}
