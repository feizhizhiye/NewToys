using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

public sealed partial class RuleRunService
{

    /// <summary>
    /// 试跑推荐：**一次 Python 进程**把 N 个候选脚本对<b>同一份 HTML</b>
    /// 全求值，按「条数 &gt; 0 且字段非空率最高」排序返回。
    /// <para>🔴 一个脚本一次进程会慢到没法用（N 个候选 = N 次解释器启动），故循环放在 Python 侧。</para>
    /// </summary>
    public async Task<List<CandidateResult>> RunProbeAsync(string htmlPath, string baseUrl,
        IEnumerable<(string Name, string Path)> scripts, CancellationToken ct = default)
    {
        if (!File.Exists(htmlPath)) throw new InvalidOperationException("还没抓到 HTML —— 先点「🌐 抓页」。");

        var items = scripts.Select(s => new Dictionary<string, string> { ["name"] = s.Name, ["path"] = s.Path }).ToList();
        if (items.Count == 0) return new List<CandidateResult>();

        var req = new PyRequest
        {
            Mode = "code",
            TimeoutSeconds = 120,
            ArgsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["mode"] = "probe",
                ["html_path"] = htmlPath,
                ["base"] = baseUrl ?? "",
                ["paths"] = JsonSerializer.Serialize(items)
            }),
            Code = Program
        };

        var res = await _py.RunAsync(req, ct).ConfigureAwait(false);
        if (!res.Ok || !string.IsNullOrEmpty(res.Error))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(res.Traceback) ? res.Error : res.Traceback);

        var list = new List<CandidateResult>();
        var json = LastLine(res.Out);
        if (json.Length == 0) return list;

        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var c = new CandidateResult
            {
                Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Count = el.TryGetProperty("count", out var cnt) && cnt.TryGetInt32(out var ci) ? ci : 0,
                Fill = el.TryGetProperty("fill", out var fv) && fv.TryGetDouble(out var fd) ? fd : 0d,
                Error = el.TryGetProperty("error", out var pe) ? pe.GetString() ?? "" : ""
            };

            if (el.TryGetProperty("fields", out var fs) && fs.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fs.EnumerateArray())
                    c.Fields.Add((
                        f.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                        f.TryGetProperty("count", out var fc) && fc.TryGetInt32(out var fci) ? fci : 0,
                        f.TryGetProperty("error", out var fe) ? fe.GetString() ?? "" : ""));
            }

            list.Add(c);
        }

        list.Sort(CandidateResult.Compare);
        return list;
    }

    /// <summary>
    /// 站点探测：对站点根试各预设的探测路径 —— 抓下来后用预设解析，
    /// 以「**能解析出条目**」为准（⛔ 不看 HTTP 200），🔴 **只推荐不代选**。
    /// <para>抓取并行；评估只起**一次** Python 进程（⛔ 不逐候选启动解释器）。</para>
    /// </summary>
    public async Task<SiteProbeResult> ProbeSiteAsync(string siteUrl, IReadOnlyList<PresetInfo> presets,
        CancellationToken ct = default)
    {
        var result = new SiteProbeResult();

        var raw = (siteUrl ?? "").Trim();
        if (raw.Length == 0) { result.Error = "站点网址为空。"; return result; }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var site)
            && !Uri.TryCreate("https://" + raw, UriKind.Absolute, out site))
        {
            result.Error = "网址无法识别：" + raw;
            return result;
        }

        var probeDir = Path.Combine(_cfg.DataDir, "script", "cache", "_probe");
        var candidates = new List<(PresetInfo Preset, string Path, string Url, string File)>();
        var seq = 0;
        foreach (var p in presets)
        {
            foreach (var path in p.ProbePaths)
            {
                string url;
                try { url = new Uri(site, path).ToString(); } catch { continue; }
                candidates.Add((p, path, url, Path.Combine(probeDir, p.Id + "-" + seq++ + ".html")));
            }
        }
        if (candidates.Count == 0) { result.Error = "没有可探测的预设路径。"; return result; }

        // 抓取（并行；单个失败只算那个路径没命中）
        var fetches = candidates.Select(c => FetchIntoAsync(c.Url, "", c.File, ct)).ToArray();
        var fetched = await Task.WhenAll(fetches).ConfigureAwait(false);

        var live = new List<int>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (fetched[i].ok) { live.Add(i); continue; }
            result.Hits.Add(new SiteProbeHit
            {
                PresetId = c.Preset.Id,
                PresetTitle = c.Preset.Title,
                Path = c.Path,
                Url = c.Url,
                Count = 0,
                Error = fetched[i].error
            });
        }
        if (live.Count == 0) return result;

        var items = live.Select(i =>
        {
            var c = candidates[i];
            return new Dictionary<string, string>
            {
                // 带序号 ⇒ 回值时按名精确对应（同一预设可有多条探测路径）
                ["name"] = c.Preset.Id + "#" + i,
                ["path"] = _files.PresetPath(c.Preset.Id),
                ["html_path"] = c.File,
                ["base"] = c.Url
            };
        }).ToList();

        var req = new PyRequest
        {
            Mode = "code",
            TimeoutSeconds = 120,
            ArgsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["mode"] = "probe",
                ["html_path"] = "",
                ["base"] = "",
                ["paths"] = JsonSerializer.Serialize(items)
            }),
            Code = Program
        };

        var res = await _py.RunAsync(req, ct).ConfigureAwait(false);
        if (!res.Ok || !string.IsNullOrEmpty(res.Error))
        {
            result.Error = string.IsNullOrWhiteSpace(res.Traceback) ? res.Error : res.Traceback;
            return result;
        }

        var json = LastLine(res.Out);
        if (json.Length == 0) { result.Error = "Python 没有返回探测结果。"; return result; }

        var counts = new Dictionary<string, (int Count, string Error)>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var count = el.TryGetProperty("count", out var cnt) && cnt.TryGetInt32(out var ci) ? ci : 0;
                var error = el.TryGetProperty("error", out var pe) ? pe.GetString() ?? "" : "";
                counts[name] = (count, error);
            }
        }
        catch (Exception ex)
        {
            result.Error = "探测结果无法解析：" + ex.Message;
            return result;
        }

        foreach (var i in live)
        {
            var c = candidates[i];
            counts.TryGetValue(c.Preset.Id + "#" + i, out var hit);
            result.Hits.Add(new SiteProbeHit
            {
                PresetId = c.Preset.Id,
                PresetTitle = c.Preset.Title,
                Path = c.Path,
                Url = c.Url,
                Count = hit.Count,
                Error = hit.Error
            });
        }

        return result;
    }

}
