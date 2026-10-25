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

/// <summary>
/// 期②下载器：对目录条目跑 <c>content</c> 段 → 按产物形态落盘。
/// <para>
/// 🔴 <b>内置</b>（并发 / 限速 / 去重 / 续跑 / 命名 / 取消）—— ⛔ 不脚本化：脚本只负责「这一页能取出什么」。
/// </para>
/// <para>
/// 🔴 产物形态优先级链：<c>content</c> 返回 ＞ <c># artifact:</c> ＞ 分类缺省猜测 ＞ none。
/// 下载行为由 <c>content</c> 返回形态决定，⛔ 不由分类直接决定。
/// </para>
/// <para>
/// 🔴 试下载与批量下载共用同一条 content 管线（<see cref="RunItemAsync"/>）—— ⛔ 不写两套。
/// </para>
/// </summary>
public sealed class DownloadService
{
    /// <summary><c># max-pages</c> 缺省上限（防「下一页」永动机；0 = 不设上限时兜底）。</summary>
    public const int MaxPagesDefault = 50;

    /// <summary>连续 N 页取不到任何东西 ⇒ 当前条目收尾。</summary>
    public const int EmptyStreakStop = 2;

    /// <summary>连续失败 N 个条目 ⇒ 停批。</summary>
    public const int FailStreakStop = 3;

    /// <summary>试下载预览的正文截断（字）。</summary>
    public const int PreviewChars = 600;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly RuleFileService _files;
    private readonly RuleRunService _run;
    private readonly ConfigStoreService _cfg;

    /// <summary>同 host 间隔门：host → 下次可请求的时刻。</summary>
    private readonly Dictionary<string, DateTime> _hostNext = new(StringComparer.OrdinalIgnoreCase);

    private int _scratchSeq;

    public DownloadService(RuleFileService files, RuleRunService run, ConfigStoreService cfg)
    {
        _files = files;
        _run = run;
        _cfg = cfg;
    }

    /// <summary>最终产物：<c>&lt;DataDir&gt;\script\downloads\&lt;脚本&gt;\&lt;条目名&gt;\</c>（⛔ 不进备份，可再生）。</summary>
    public string DownloadsDir => Path.Combine(_cfg.DataDir, "script", "downloads");

    /// <summary>试下载临时产物：<c>&lt;DataDir&gt;\script\trials\&lt;脚本&gt;\&lt;条目名&gt;\</c>。</summary>
    public string TrialsDir => Path.Combine(_cfg.DataDir, "script", "trials");

    /// <summary>抓取暂存（每页一份 HTML，按序覆盖）：<c>cache\_dl\&lt;脚本&gt;\&lt;n&gt;.html</c>。</summary>
    private string NextScratch(string script)
    {
        var dir = Path.Combine(_cfg.DataDir, "script", "cache", "_dl", RuleFileService.SafeName(script));
        Directory.CreateDirectory(dir);
        var n = Interlocked.Increment(ref _scratchSeq);
        return Path.Combine(dir, n + ".html");
    }

    private RuleSetMeta? Meta(string script)
    {
        try
        {
            return _files.Scan().FirstOrDefault(m => string.Equals(m.Name, script, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    // ==================== 🧪 试下载（单条预览，⛔ 不落最终产物） ====================

    /// <summary>
    /// 对一条条目跑 content 段（含 next 跟进）→ 预览正文前 N 字 / 文件清单 / 页数 / 耗时。
    /// 临时落 <see cref="TrialsDir"/>，⛔ 不算一次下载。
    /// </summary>
    public async Task<TrialResult> TrialAsync(string script, string url, string title, CancellationToken ct = default)
    {
        var result = new TrialResult { Title = title.Length > 0 ? title : url };
        if (script.Length == 0 || !_files.Exists(script))
        {
            result.Error = "content 脚本不存在：「" + script + "」。";
            return result;
        }
        if (url.Trim().Length == 0)
        {
            result.Error = "这一行没有 url，没法试下载。";
            return result;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
        var meta = Meta(script);
        var (plan, pages, _, error, diag) =
            await RunItemAsync(script, meta, url.Trim(), ct).ConfigureAwait(false);
            sw.Stop();

            result.HasResult = true;
            result.Pages = pages;
            result.Seconds = sw.Elapsed.TotalSeconds;
            result.Diag = diag;

            if (error is not null)
            {
                result.Error = error;
                return result;
            }

            result.KindText = KindLabel(plan.Kind) + " · " + plan.Reason;
            switch (plan.Kind)
            {
                case ArtifactKind.Text:
                    result.TextPreview = Trim(plan.Text, PreviewChars);
                    break;
                case ArtifactKind.Files:
                    foreach (var f in plan.Files) result.FileLines.Add(f.name + "  ·  " + f.url);
                    result.TextPreview = "解析出 " + plan.Files.Count + " 个文件（下载时才真正落盘）。";
                    break;
                case ArtifactKind.Rows:
                    foreach (var r in plan.Rows) result.FileLines.Add(r.title + "  ·  " + r.url);
                    result.TextPreview = "这一页是容器：解析出 " + plan.Rows.Count
                                       + " 个子条目（下载时才递归跟进，⛔ 容器本身不下盘）。";
                    break;
                default:
                    result.TextPreview = "content 段没取出东西（表达式没命中，或停在了第 " + pages + " 页）。";
                    break;
            }

            result.NotifyFileLines();

            // 临时落 trials\（⛔ 不落最终产物；失败只当没落）
            try
            {
                var dir = Path.Combine(TrialsDir, RuleFileService.SafeName(script), SafeEntry(title.Length > 0 ? title : url));
                WriteArtifact(plan, dir);
            }
            catch { /* 预览优先 */ }
        }
        catch (Exception ex)
        {
            result.Error = FirstLine(ex.Message);
        }
        return result;
    }

    // ==================== ⬇ 批量下载 ====================

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

    /// <summary>写产物（返回 "" = 成功；"⚠ …" = 失败原因）。</summary>
    private static async Task<string> SaveAsync(ArtifactPlan plan, string dir, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(dir);
            if (plan.Kind == ArtifactKind.Text)
            {
                var path = Path.Combine(dir, "内容.txt");
                await File.WriteAllTextAsync(path, plan.Text, new UTF8Encoding(true), ct).ConfigureAwait(false);
                return "正文 " + plan.Text.Length + " 字 → 内容.txt";
            }

            var okCount = 0;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in plan.Files)
            {
                var name = UniqueName(SafeFile(f.name), used);
                var path = Path.Combine(dir, name);
                using var resp = await Http.GetAsync(f.url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(path);
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
                okCount++;
            }
            return okCount == plan.Files.Count ? okCount + " 个文件" : "⚠ 只下成 " + okCount + "/" + plan.Files.Count + " 个文件";
        }
        catch (Exception ex)
        {
            return "⚠ 落盘失败：" + FirstLine(ex.Message);
        }
    }

    /// <summary>试下载的临时落盘（⛔ 不是最终产物）。</summary>
    private static void WriteArtifact(ArtifactPlan plan, string dir)
    {
        Directory.CreateDirectory(dir);
        if (plan.Kind == ArtifactKind.Text)
            File.WriteAllText(Path.Combine(dir, "内容.txt"), plan.Text, new UTF8Encoding(true));
        else if (plan.Kind == ArtifactKind.Files)
            File.WriteAllLines(Path.Combine(dir, "文件清单.txt"),
                plan.Files.Select(f => f.name + "\t" + f.url), new UTF8Encoding(true));
        else if (plan.Kind == ArtifactKind.Rows)
            File.WriteAllLines(Path.Combine(dir, "子条目清单.txt"),
                plan.Rows.Select(r => r.title + "\t" + r.url), new UTF8Encoding(true));
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = name;
        var i = 1;
        while (!used.Add(candidate)) candidate = stem + "(" + i++ + ")" + ext;
        return candidate;
    }

    // ==================== 小工具 ====================

    /// <summary>同 host 间隔门（<c># delay-ms</c>）：同一 host 两次请求之间至少隔这么久。</summary>
    private async Task GateAsync(string url, int delayMs, CancellationToken ct)
    {
        if (delayMs <= 0) return;
        string host;
        try { host = new Uri(url).Host; }
        catch { return; }

        while (true)
        {
            DateTime next;
            lock (_hostNext)
            {
                if (!_hostNext.TryGetValue(host, out var last) || DateTime.UtcNow >= last)
                {
                    _hostNext[host] = DateTime.UtcNow.AddMilliseconds(delayMs);
                    return;
                }
                next = last;
            }
            var wait = next - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait > TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : wait, ct).ConfigureAwait(false);
        }
    }

    private static string NormUrl(string url)
    {
        var s = (url ?? "").Trim();
        if (Uri.TryCreate(s, UriKind.Absolute, out var u))
            return u.Scheme.ToLowerInvariant() + "://" + u.Host.ToLowerInvariant()
                 + (u.IsDefaultPort ? "" : ":" + u.Port) + u.AbsolutePath.TrimEnd('/') + u.Query;
        return s.TrimEnd('/');
    }

    private static string AbsUrl(string pageUrl, string url)
    {
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, url, out var abs))
            return abs.ToString();
        return url;
    }

    private static string FileNameOf(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var name = Uri.UnescapeDataString(Path.GetFileName(path.TrimEnd('/')));
            return name.Length > 0 ? name : "file";
        }
        catch { return "file"; }
    }

    private static string SafeFile(string name)
    {
        var s = name ?? "";
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Length == 0 ? "file" : s;
    }

    /// <summary>条目名 → 目录名（与规则文件同一套清洗口径）。</summary>
    public static string SafeEntry(string name) => RuleFileService.SafeName(name);

    private static string Trim(string text, int max)
    {
        var t = (text ?? "").Trim();
        return t.Length <= max ? t : t[..max] + "\n……（预览截断，完整内容以下载产物为准）";
    }

    private static string FirstLine(string? text)
    {
        var t = (text ?? "").Trim();
        var nl = t.IndexOf('\n');
        return nl > 0 ? t[..nl] : t;
    }
}
