using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>hosts 源订阅：预设 + 自定义源清单（落本模块库 web_docs 的 hosts-sources 文档）与拉取解析。（WHY: WT-007）</summary>
public class HostsSourceService
{
    private const string DocSources = "hosts-sources";
    private const int MinPresetEntries = 20;

    private readonly ConfigStoreService _cfg;
    private readonly JsonDocStore _docs;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public HostsSourceService(ConfigStoreService cfg, WebDb db)
    {
        _cfg = cfg;
        _docs = new JsonDocStore(db, "web");
    }

    private string SeededMarker => Path.Combine(_cfg.ConfigDir, "hosts-sources.seeded");

    /// <summary>随包预设（2026-10-26 实测可达的 GitHub 加速类 hosts 项目）。</summary>
    private static IReadOnlyList<HostsSource> Presets() => new List<HostsSource>
    {
        new()
        {
            Id = "github520",
            Name = "GitHub520",
            Builtin = true,
            Urls = new List<string>
            {
                "https://raw.hellogithub.com/hosts",
                "https://fastly.jsdelivr.net/gh/521xueweihan/GitHub520@main/hosts"
            }
        },
        new()
        {
            Id = "ineo6-hosts",
            Name = "ineo6/hosts",
            Builtin = true,
            Urls = new List<string>
            {
                "https://fastly.jsdelivr.net/gh/ineo6/hosts@master/hosts",
                "https://raw.githubusercontent.com/ineo6/hosts/master/hosts"
            }
        }
    };

    public IReadOnlyList<HostsSource> Load()
    {
        List<HostsSource> list;
        try { list = _docs.Load<List<HostsSource>>(DocSources) ?? new List<HostsSource>(); }
        catch { list = new List<HostsSource>(); }

        if (File.Exists(SeededMarker)) return list;

        // 首次：把预设并进清单（按 Id 幂等），写标记
        foreach (var p in Presets())
        {
            if (list.Any(s => string.Equals(s.Id, p.Id, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(p);
        }

        Save(list);
        try { File.WriteAllText(SeededMarker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); }
        catch { /* 标记写不出来最多下次再播一次（幂等） */ }

        return list;
    }

    public void Save(IEnumerable<HostsSource> sources)
    {
        try
        {
            var list = sources.ToList();
            if (list.Count == 0) { _docs.Delete(DocSources); return; }
            _docs.Save(DocSources, list);
        }
        catch { /* 保存失败不打断 UI */ }
    }

    /// <summary>
    /// 拉取一个源：按 <see cref="HostsSource.Urls"/> 顺序试，第一个「有有效条目」的地址胜出。
    /// <para>成功时回写 <c>LastOkUrl</c> / <c>LastFetchedAt</c> / <c>LastCount</c>（不自动落库，调用方决定）。</para>
    /// </summary>
    public async Task<HostsFetchResult> FetchAsync(HostsSource source, CancellationToken ct = default)
    {
        if (source.Urls.Count == 0) return HostsFetchResult.Fail("这个源没有填写任何地址");

        var tried = new List<string>();
        foreach (var raw in source.Urls)
        {
            var url = (raw ?? "").Trim();
            if (url.Length == 0) continue;

            try
            {
                var text = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                var entries = HostsService.ParseText(text);
                // 预设源要求「至少 20 条」防抓到一个错误页；自定义源只要求解析出 1 条
                var min = source.Builtin ? MinPresetEntries : 1;
                if (entries.Count < min)
                {
                    tried.Add($"{url}（只解析出 {entries.Count} 条）");
                    continue;
                }

                var last = source.LastCount;
                var stale = last > 0 && entries.Count < last * 0.5;

                source.LastOkUrl = url;
                source.LastFetchedAt = DateTime.Now;
                source.LastCount = entries.Count;

                var staleMsg = stale ? $"；⚠ 比上次（{last} 条）少了超过一半，源可能已失效" : "";
                return new HostsFetchResult
                {
                    Ok = true,
                    Entries = entries,
                    UsedUrl = url,
                    Stale = stale,
                    Message = $"「{source.Name}」拉到 {entries.Count} 条（{url}）{staleMsg}"
                };
            }
            catch (Exception ex)
            {
                tried.Add($"{url}（{ex.Message}）");
            }
        }

        return HostsFetchResult.Fail($"所有地址都失败：\n" + string.Join("\n", tried));
    }

    /// <summary>新增一个自定义源（Id 自动生成）。</summary>
    public static HostsSource NewCustom(string name, IEnumerable<string> urls)
        => new()
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = string.IsNullOrWhiteSpace(name) ? "自定义源" : name.Trim(),
            Builtin = false,
            Urls = urls.Select(u => (u ?? "").Trim()).Where(u => u.Length > 0).ToList()
        };
}

/// <summary>一次源拉取的结果。</summary>
public class HostsFetchResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string UsedUrl { get; init; } = "";
    public bool Stale { get; init; }
    public List<HostsEntry> Entries { get; init; } = new();

    public static HostsFetchResult Fail(string msg) => new() { Ok = false, Message = msg };
}
