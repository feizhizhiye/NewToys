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
    /// 批量下载：<paramref name="req"/> 带勾选的行 = 非链式；只带 <see cref="DownloadRequest.StartUrl"/> = 链式
    /// （content + next 逐页跟进，rows 出来的子条目入队 ⇒ 目录边跑边累积）。
    /// </summary>
    public async Task<DownloadOutcome> DownloadAsync(DownloadRequest req, IProgress<DownloadEvent>? progress, CancellationToken ct = default)
    {
        var outcome = new DownloadOutcome();
        var script = (req.Script ?? "").Trim();
        if (script.Length == 0 || !_files.Exists(script))
        {
            outcome.StopReason = "先在「内容脚本 ▾」里选一个脚本。";
            return outcome;
        }

        var meta = Meta(script);
        var cap = Math.Clamp(meta?.Concurrency ?? 1, 1, 8);
        using var sem = new SemaphoreSlim(cap);

        var queue = new Queue<(string url, string title)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failStreak = 0;
        var done = 0;
        var okCount = 0;
        var skipCount = 0;
        var failCount = 0;
        var stopped = false;

        if (req.IsChain)
        {
            var start = (req.StartUrl ?? "").Trim();
            if (start.Length == 0)
            {
                outcome.StopReason = "链式模式（没勾选行）要先填起始网址。";
                return outcome;
            }
            queue.Enqueue((start, start));
            seen.Add(NormUrl(start));
        }
        else
        {
            foreach (var e in req.Entries)
            {
                if (e.url.Trim().Length == 0 || !seen.Add(NormUrl(e.url))) continue;
                queue.Enqueue((e.url.Trim(), e.title));
            }
            if (queue.Count == 0)
            {
                outcome.StopReason = "没有可下载的行（勾选几行，或清空勾选 + 填起始网址走链式模式）。";
                return outcome;
            }
        }

        var total = req.IsChain ? -1 : queue.Count;
        progress?.Report(new DownloadEvent { Phase = "start", Total = total, Name = script });

        var tasks = new List<Task>();
        try
        {
        while (true)
        {
            tasks.RemoveAll(t => t.IsCompleted);

            (bool has, string url, string title) item;
            lock (queue)
            {
                if (queue.Count == 0)
                {
                    // 🔴 链式模式：队列空了但还有任务在跑 ⇒ 它们可能往里加子条目（rows），等一个跑完再看
                    if (stopped || tasks.Count == 0) break;
                    item = (false, "", "");
                }
                else
                {
                    var q = queue.Dequeue();
                    item = (true, q.url, q.title);
                }
            }
            if (stopped) break;

            if (!item.has)
            {
                await Task.WhenAny(tasks).ConfigureAwait(false);
                continue;
            }

            await sem.WaitAsync(ct).ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var (plan, pages, _, error, _) =
                        await RunItemAsync(script, meta, item.url, ct).ConfigureAwait(false);

                    if (error is not null)
                    {
                        Interlocked.Increment(ref failCount);
                        Report(progress, item.title, item.url, "fail", error + (pages > 0 ? "（第 " + pages + " 页后）" : ""), ref done, total);
                        if (Interlocked.Increment(ref failStreak) >= FailStreakStop)
                        {
                            stopped = true;
                            outcome.StopReason = "连续失败 " + FailStreakStop + " 次，已停批。最后一条：" + error;
                        }
                        return;
                    }

                    Interlocked.Exchange(ref failStreak, 0);

                    // rows ⇒ 容器：子条目入队（全局去重；目录边跑边累积），本身 ⛔ 不下盘
                    if (plan.Kind == ArtifactKind.Rows)
                    {
                        lock (queue)
                        {
                            foreach (var r in plan.Rows)
                                if (seen.Add(NormUrl(r.url))) queue.Enqueue(r);
                        }
                        Interlocked.Increment(ref okCount);
                        Report(progress, item.title, item.url, "ok",
                            "容器页：解析出 " + plan.Rows.Count + " 个子条目（已入队）", ref done, total);
                        return;
                    }

                    if (plan.Kind == ArtifactKind.None)
                    {
                        Interlocked.Increment(ref skipCount);
                        Report(progress, item.title, item.url, "skip", "没取出产物（" + plan.Reason + "）", ref done, total);
                        return;
                    }

                    // 续跑：目标目录已有文件 ⇒ 跳过
                    var dir = EntryDir(script, item.title.Length > 0 ? item.title : item.url);
                    if (HasFiles(dir))
                    {
                        Interlocked.Increment(ref skipCount);
                        Report(progress, item.title, item.url, "skip", "已存在，跳过（续跑）", ref done, total);
                        return;
                    }

                    var saved = await SaveAsync(plan, dir, ct).ConfigureAwait(false);
                    if (saved.StartsWith("⚠", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref failCount);
                        Report(progress, item.title, item.url, "fail", saved, ref done, total);
                        if (Interlocked.Increment(ref failStreak) >= FailStreakStop)
                        {
                            stopped = true;
                            outcome.StopReason = "连续失败 " + FailStreakStop + " 次，已停批。最后一条：" + saved;
                        }
                        return;
                    }

                    Interlocked.Exchange(ref failStreak, 0);
                    Interlocked.Increment(ref okCount);
                    Report(progress, item.title, item.url, "ok",
                        plan.Kind == ArtifactKind.Text ? "正文 " + plan.Text.Length + " 字 · " + pages + " 页" : saved,
                        ref done, total);
                }
                finally
                {
                    sem.Release();
                }
            }, ct));

        }
        }
        catch (OperationCanceledException) { outcome.Cancelled = true; }

        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { outcome.Cancelled = true; }

        outcome.Ok = okCount;
        outcome.Skipped = skipCount;
        outcome.Failed = failCount;
        if (stopped && outcome.StopReason.Length == 0)
            outcome.StopReason = "连续失败 " + FailStreakStop + " 次，已停批。";
        return outcome;
    }

    private static void Report(IProgress<DownloadEvent>? progress, string name, string url, string status, string detail,
        ref int done, int total)
    {
        progress?.Report(new DownloadEvent
        {
            Phase = "item",
            Name = name,
            Url = url,
            Status = status,
            Detail = detail,
            Done = Interlocked.Increment(ref done),
            Total = total
        });
    }

    /// <summary>条目目录：<c>downloads\&lt;脚本&gt;\&lt;条目名&gt;</c>。</summary>
    public string EntryDir(string script, string entryName)
        => Path.Combine(DownloadsDir, RuleFileService.SafeName(script), SafeEntry(entryName));

    private static bool HasFiles(string dir)
    {
        try
        {
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any();
        }
        catch { return false; }
    }

    // ==================== 共用 content 管线（试下载 / 批量下载同一套） ====================

}
