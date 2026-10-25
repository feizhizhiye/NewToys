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
