using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

public sealed partial class DownloadService
{

    /// <summary>
    /// 跑一个条目：抓页 → <c>mode=step</c> 求值 <c>content</c> + <c>next</c> → 跟 next 翻页 → 合成产物计划。
    /// 三保险：URL 去重 + <c># max-pages</c> 上限 + 连续 <see cref="EmptyStreakStop"/> 页空即停。
    /// </summary>
    private async Task<(ArtifactPlan plan, int pages, List<(string url, string title)> rows, string? error, string diag)>
        RunItemAsync(string script, RuleSetMeta? meta, string url, CancellationToken ct)
    {
        var plan = new ArtifactPlan { Kind = ArtifactKind.None, Reason = "没抓到页" };
        var diag = new StringBuilder();
        var texts = new List<string>();
        var files = new List<(string url, string name)>();
        var rows = new List<(string url, string title)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(url);
        var pages = 0;
        var emptyStreak = 0;
        // ⚠️ MaxPages 是 int（不是 int?）⇒ `meta?.MaxPages is > 0 ? meta.MaxPages.Value` 会 CS1061（int 没有 .Value）
        var maxPages = (meta?.MaxPages ?? 0) > 0 ? meta!.MaxPages : MaxPagesDefault;
        var delayMs = meta?.DelayMs ?? 0;
        string? error = null;
        string? reason = null;

        while (queue.Count > 0 && pages < maxPages && emptyStreak < EmptyStreakStop && !ct.IsCancellationRequested)
        {
            var page = queue.Dequeue();
            if (!seen.Add(NormUrl(page))) continue;

            await GateAsync(page, delayMs, ct).ConfigureAwait(false);

            var scratch = NextScratch(script);
            var (ok, _, fetchError) = await _run.FetchIntoAsync(page, meta?.Encoding ?? "utf8", scratch).ConfigureAwait(false);
            if (!ok)
            {
                error = "抓取失败：" + fetchError;
                diag.AppendLine("页 " + page + " 抓取失败：" + fetchError);
                break;
            }
            pages++;

            StepResult step;
            try
            {
                step = await _run.RunStepAsync(script, scratch, page, "content,next", ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = "求值失败：" + FirstLine(ex.Message);
                diag.AppendLine("页 " + page + " 求值失败：" + ex.Message);
                break;
            }

            var warn = step.ErrorOf("content") ?? step.ErrorOf("next");
            if (warn is not null) diag.AppendLine("求值警告：" + warn);

            var pagePlan = ResolveArtifacts(step.Get("content"), meta, page);
            reason ??= pagePlan.Reason;

            var got = false;
            if (pagePlan.Kind == ArtifactKind.Files) { files.AddRange(pagePlan.Files); got = true; }
            else if (pagePlan.Kind == ArtifactKind.Rows)
            {
                foreach (var r in pagePlan.Rows)
                {
                    rows.Add(r);
                    if (seen.Add(NormUrl(r.url))) queue.Enqueue(r.url);
                }
                got = true;
            }
            else if (pagePlan.Kind == ArtifactKind.Text) { texts.Add(pagePlan.Text); got = true; }

            var next = step.Get("next").FirstOrDefault(v => v.Trim().Length > 0);
            if (next is not null && seen.Add(NormUrl(next)))
            {
                queue.Enqueue(next);
                got = true;
            }

            emptyStreak = got ? 0 : emptyStreak + 1;
        }

        if (ct.IsCancellationRequested) error ??= "已取消";

        // 合成：Files ＞ Rows ＞ Text ＞ None（同一条目跨页累积）
        if (files.Count > 0)
        {
            plan = new ArtifactPlan { Kind = ArtifactKind.Files, Reason = reason ?? "" };
            plan.Files.AddRange(files);
        }
        else if (rows.Count > 0)
        {
            plan = new ArtifactPlan { Kind = ArtifactKind.Rows, Reason = reason ?? "" };
            plan.Rows.AddRange(rows);
        }
        else if (texts.Count > 0)
        {
            plan = new ArtifactPlan { Kind = ArtifactKind.Text, Text = string.Join("\n\n", texts), Reason = reason ?? "缺省文本" };
        }

        diag.AppendLine("条目 " + url + "：" + pages + " 页 · " + KindLabel(plan.Kind) + " · " + plan.Reason);
        return (plan, pages, rows, error, diag.ToString().TrimEnd());
    }

    // ==================== 产物形态判定（优先级链） ====================

    /// <summary>
    /// content 值 → 产物计划：<c>content</c> 返回（JSON text/files/rows）＞ <c># artifact:</c> ＞ 分类缺省猜测 ＞ 缺省文本。
    /// </summary>
    public static ArtifactPlan ResolveArtifacts(IReadOnlyList<string> values, RuleSetMeta? meta, string pageUrl)
    {
        var texts = new List<string>();
        var files = new List<(string url, string name)>();
        var rows = new List<(string url, string title)>();
        var manifests = 0;
        string? reason = null;

        foreach (var v in values)
        {
            var t = (v ?? "").TrimStart();
            if (t.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(v ?? "");
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        manifests++;
                        if (root.TryGetProperty("text", out var te) && te.ValueKind == JsonValueKind.String)
                        {
                            texts.Add(te.GetString() ?? "");
                            reason = "content 返回 JSON.text";
                            continue;
                        }
                        if (root.TryGetProperty("files", out var fs) && fs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var f in fs.EnumerateArray())
                            {
                                var u = f.TryGetProperty("url", out var ue) ? ue.GetString() ?? "" : "";
                                if (u.Length == 0) continue;
                                var n = f.TryGetProperty("name", out var ne) ? ne.GetString() ?? "" : "";
                                files.Add((AbsUrl(pageUrl, u), n.Length > 0 ? n : FileNameOf(u)));
                            }
                            reason = "content 返回 JSON.files";
                            continue;
                        }
                        if (root.TryGetProperty("rows", out var rs) && rs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var r in rs.EnumerateArray())
                            {
                                var u = r.TryGetProperty("url", out var ue) ? ue.GetString() ?? "" : "";
                                if (u.Length == 0) continue;
                                var ti = r.TryGetProperty("title", out var tpe) ? tpe.GetString() ?? "" : "";
                                rows.Add((AbsUrl(pageUrl, u), ti));
                            }
                            reason = "content 返回 JSON.rows";
                            continue;
                        }
                    }
                }
                catch { /* 不是合法 JSON ⇒ 当普通文本 */ }
            }
            texts.Add(v ?? "");
        }

        // 没有任何 JSON 清单 ⇒ 看 # artifact: 与分类缺省猜测（只认「像 URL」的值）
        if (manifests == 0 && values.Count > 0 && values.All(LooksLikeUrl))
        {
            if (string.Equals(meta?.Artifact, "files", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var v in values) files.Add((AbsUrl(pageUrl, v), FileNameOf(v)));
                reason = "# artifact: files";
            }
            else if (IsMediaCategory(meta?.Category))
            {
                foreach (var v in values) files.Add((AbsUrl(pageUrl, v), FileNameOf(v)));
                reason = "分类 " + meta!.Category + "（缺省猜测）";
            }
        }

        if (files.Count > 0)
        {
            var p = new ArtifactPlan { Kind = ArtifactKind.Files, Reason = reason ?? "文件清单" };
            p.Files.AddRange(files);
            return p;
        }
        if (rows.Count > 0)
        {
            var p = new ArtifactPlan { Kind = ArtifactKind.Rows, Reason = reason ?? "子条目清单" };
            p.Rows.AddRange(rows);
            return p;
        }
        if (texts.Count > 0)
        {
            return new ArtifactPlan
            {
                Kind = ArtifactKind.Text,
                Text = string.Join("\n\n", texts),
                Reason = reason ?? "缺省文本"
            };
        }
        return new ArtifactPlan { Kind = ArtifactKind.None, Reason = "content 段没取出东西" };
    }

    private static bool LooksLikeUrl(string v)
        => v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || v.StartsWith('/');

    private static bool IsMediaCategory(string? category)
    {
        var c = (category ?? "").ToLowerInvariant();
        return c is "images" or "media" or "video" or "audio" or "图片" or "媒体";
    }

    public static string KindLabel(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Text => "正文文本",
        ArtifactKind.Files => "文件",
        ArtifactKind.Rows => "子条目容器",
        _ => "无产物"
    };

    // ==================== 落盘 ====================

}
