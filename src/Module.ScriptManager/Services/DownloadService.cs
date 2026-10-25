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
public sealed partial class DownloadService
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

}
