using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 运行记录：每次「▶ 跑这段」落一条
/// <c>runs\&lt;脚本&gt;\&lt;时间戳&gt;_&lt;URL哈希&gt;\</c>（<c>result.json</c> + <c>page.html</c> + <c>diag.txt</c>）。
/// <para>
/// 🔴 每脚本只保留最近 <see cref="KeepRuns"/> 条（目录名时间戳前缀 ⇒ 字典序 = 时间序）；
/// 🔴 <c>runs\</c> ⛔ 不进备份（可重建）；试跑推荐 ⛔ 不记（那是 N 个候选的横向比较，不是一次运行）。
/// </para>
/// </summary>
public sealed class RunRecordService
{
    /// <summary>每脚本保留的最近运行条数。</summary>
    public const int KeepRuns = 10;

    private readonly ConfigStoreService _cfg;
    private readonly RuleCatalogService _catalog;
    private bool _legacyMigrated;

    public RunRecordService(ConfigStoreService cfg, RuleCatalogService catalog)
    {
        _cfg = cfg;
        _catalog = catalog;
    }

    /// <summary>运行记录根：<c>&lt;StoreDir&gt;\script\runs</c>（用户数据，进备份）。</summary>
    public string RunsRoot
    {
        get
        {
            var dir = Path.Combine(_cfg.StoreDir, "script", "runs");
            MigrateLegacyRuns(dir);
            return dir;
        }
    }

    /// <summary>
    /// 一次性把旧 <c>web-tools\runs</c> 下的**脚本子目录**整体搬进新根
    /// （只搬一次、同名目标已存在则跳过、整段 try/catch）。
    /// </summary>
    private void MigrateLegacyRuns(string dir)
    {
        if (_legacyMigrated) return;
        _legacyMigrated = true;
        try
        {
            var old = Path.Combine(_cfg.StoreDir, "web-tools", "runs");
            if (!Directory.Exists(old)) return;
            foreach (var d in Directory.GetDirectories(old))
            {
                var dst = Path.Combine(dir, Path.GetFileName(d));
                if (!Directory.Exists(dst))
                {
                    Directory.CreateDirectory(dir);
                    Directory.Move(d, dst);
                }
            }
        }
        catch
        {
            // 搬不动 ⇒ 按没有历史处理。
        }
    }

    /// <summary>
    /// 某脚本最近的运行记录（「运行结果」页 ④ 节用）：按目录名字典序倒序（= 时间倒序）取前 <paramref name="take"/> 条。
    /// <para>🔴 读不出来 / 坏 json 一律跳过（⛔ 不打断页面）。</para>
    /// </summary>
    public IReadOnlyList<RunRecord> History(string script, int take = KeepRuns)
    {
        var list = new List<RunRecord>();
        try
        {
            var dir = ScriptDir(script);
            if (!Directory.Exists(dir)) return list;
            foreach (var d in Directory.GetDirectories(dir)
                         .OrderByDescending(d => d, StringComparer.Ordinal)
                         .Take(take))
            {
                var file = Path.Combine(d, "result.json");
                if (!File.Exists(file)) continue;
                try
                {
                    var rec = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file, Encoding.UTF8));
                    if (rec is null) continue;
                    rec.Dir = d;
                    list.Add(rec);
                }
                catch { /* 坏记录跳过 */ }
            }
        }
        catch { return list; }
        return list;
    }

    /// <summary>某脚本的运行记录目录（名字做文件名清洗）。</summary>
    public string ScriptDir(string script) => Path.Combine(RunsRoot, RuleFileService.SafeName(script));

    /// <summary>最近一次运行记录（没有返回 null）。</summary>
    public RunRecord? Latest(string script)
    {
        try
        {
            var dir = ScriptDir(script);
            if (!Directory.Exists(dir)) return null;

            var last = Directory.GetDirectories(dir)
                .OrderByDescending(d => d, StringComparer.Ordinal)
                .FirstOrDefault();
            if (last is null) return null;

            var file = Path.Combine(last, "result.json");
            if (!File.Exists(file)) return null;

            var rec = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file, Encoding.UTF8));
            if (rec is null) return null;
            rec.Dir = last;
            return rec;
        }
        catch { return null; }
    }

    /// <summary>
    /// 写一条运行记录 + 修剪到最近 <see cref="KeepRuns"/> 条 + 回写目录页「最近运行」元数据。
    /// </summary>
    /// <returns>落盘的记录（<see cref="RunRecord.Dir"/> = 本次目录）。</returns>
    public RunRecord Save(string script, string url, IReadOnlyList<RuleCommand> rows, string? htmlPath)
        => SaveFields(script, url, rows.Select(r => new RunField
        {
            Key = r.Key,
            Expr = r.Expr,
            Count = r.Count,
            Error = r.Error,
            Preview = (r.Preview ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Take(3)
                .ToList()
        }).ToList(), htmlPath);

    /// <summary>
    /// 写一条运行记录（字段版：爬虫面板的行表结果走这里；调试集合走 <see cref="Save"/>）。
    /// 修剪 + 元数据回写与 <see cref="Save"/> 同一套。
    /// </summary>
    public RunRecord SaveFields(string script, string url, IReadOnlyList<RunField> fields, string? htmlPath)
    {
        var rec = new RunRecord
        {
            Script = script,
            Url = url ?? "",
            At = DateTime.Now,
            Total = TotalOf(fields.ToList()),
            Fields = fields.ToList()
        };

        var dir = NewDir(script, rec.Url);
        Directory.CreateDirectory(dir);
        rec.Dir = dir;

        if (!string.IsNullOrEmpty(htmlPath) && File.Exists(htmlPath))
        {
            var html = Path.Combine(dir, "page.html");
            File.Copy(htmlPath, html, overwrite: true);
            try { rec.HtmlChars = (int)new FileInfo(html).Length; } catch { /* 读不到长度不打断 */ }
        }

        File.WriteAllText(Path.Combine(dir, "result.json"), JsonSerializer.Serialize(rec, Opts), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "diag.txt"), BuildDiag(rec), new UTF8Encoding(false));

        Prune(dir);
        _catalog.MarkRun(script, rec.At, rec.Total);
        return rec;
    }

    /// <summary>
    /// 导出 CSV（元信息 + 字段汇总 + 前 3 条预览；⛔ 不引 Excel 库）。
    /// 🔴 调用方写盘时带 BOM（<c>new UTF8Encoding(true)</c>）⇒ Excel 才认中文。
    /// </summary>
    public string ToCsv(RunRecord rec)
    {
        var sb = new StringBuilder();
        sb.AppendLine("脚本,网址,时间,HTML字符,条目数");
        sb.AppendLine(string.Join(",",
            Esc(rec.Script), Esc(rec.Url), Esc(rec.At.ToString("yyyy-MM-dd HH:mm:ss")), rec.HtmlChars, rec.Total));
        sb.AppendLine();
        sb.AppendLine("字段,表达式,命中数,预览1,预览2,预览3,错误");
        foreach (var f in rec.Fields)
        {
            sb.AppendLine(string.Join(",",
                Esc(f.Key), Esc(f.Expr), f.Count,
                Esc(f.Preview.Count > 0 ? f.Preview[0] : ""),
                Esc(f.Preview.Count > 1 ? f.Preview[1] : ""),
                Esc(f.Preview.Count > 2 ? f.Preview[2] : ""),
                Esc(f.Error)));
        }
        return sb.ToString();
    }

    // ==================== 内部 ====================

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // 中文不转 \uXXXX
    };

    /// <summary>条目数 = url 字段命中（段间契约），没有 url 取最大字段命中。</summary>
    private static int TotalOf(List<RunField> fields)
    {
        var url = fields.FirstOrDefault(f => string.Equals(f.Key, "url", StringComparison.OrdinalIgnoreCase));
        if (url is { Count: > 0 }) return url.Count;
        return fields.Count == 0 ? 0 : fields.Max(f => f.Count);
    }

    /// <summary>新目录名：<c>&lt;yyyyMMdd-HHmmss&gt;_&lt;URL哈希8位&gt;</c>（同秒同 URL 加序号）。</summary>
    private string NewDir(string script, string url)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var hash = Hash8(url);
        var dir = Path.Combine(ScriptDir(script), stamp + "_" + hash);
        for (var i = 2; Directory.Exists(dir); i++)
            dir = Path.Combine(ScriptDir(script), stamp + "_" + hash + "-" + i);
        return dir;
    }

    private static string Hash8(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        var sb = new StringBuilder(8);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
            if (sb.Length >= 8) break;
        }
        return sb.ToString();
    }

    /// <summary>修剪：只留最近 <see cref="KeepRuns"/> 条（⛔ 失败不打断）。</summary>
    private static void Prune(string justWritten)
    {
        try
        {
            var parent = Path.GetDirectoryName(justWritten);
            if (parent is null || !Directory.Exists(parent)) return;

            foreach (var old in Directory.GetDirectories(parent)
                         .OrderByDescending(d => d, StringComparer.Ordinal)
                         .Skip(KeepRuns))
            {
                try { Directory.Delete(old, recursive: true); }
                catch { /* 被占用 ⇒ 下次运行再清 */ }
            }
        }
        catch { /* 修剪失败不打断 */ }
    }

    private static string BuildDiag(RunRecord rec)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NewToys 运行记录（调试集合 · ▶ 跑这段）");
        sb.AppendLine("脚本 ：" + rec.Script);
        sb.AppendLine("网址 ：" + rec.Url);
        sb.AppendLine("时间 ：" + rec.At.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("HTML ：" + rec.HtmlChars + " 字符");
        sb.AppendLine("命中 ：" + rec.Total + " 条");
        sb.AppendLine();
        foreach (var f in rec.Fields)
        {
            sb.AppendLine(f.Key.PadRight(12) + f.Count + " 条   " + f.Expr);
            foreach (var p in f.Preview) sb.AppendLine("    | " + p);
            if (f.Error.Length > 0) sb.AppendLine("    ⚠ " + f.Error);
        }
        return sb.ToString();
    }

    /// <summary>CSV 转义（含逗号 / 引号 / 换行的进引号；⛔ 公式注入不防 —— 自家工具）。</summary>
    private static string Esc(string s)
    {
        var t = (s ?? "").Replace("\r", " ").Replace("\n", " ");
        return t.Contains(',') || t.Contains('"') ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;
    }
}
