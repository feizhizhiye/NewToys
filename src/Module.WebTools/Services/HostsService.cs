using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>
/// hosts 方案管理：多套方案存在 <c>config\hosts-profiles.json</c>（可被数据管理备份）。
/// <para>
/// <b>切换到系统 hosts</b>：在系统 <c>drivers\etc\hosts</c> 里维护一段
/// <c># === NewToys hosts start === … end ===</c> 的受管块，应用某方案时只替换这段块，
/// 用户自己写在其它的行原样保留。写入系统文件需要管理员权限，无权限时返回明确提示。
/// </para>
/// <para><b>网络导入</b>：下载一份 hosts 文本（普通 hosts 或 Netscape 书签都按行解析），解析成条目。</para>
/// </summary>
public class HostsService
{
    private static readonly string ManagedStart = "# === NewToys hosts start ===";
    private static readonly string ManagedEnd = "# === NewToys hosts end ===";

    private readonly ConfigStoreService _cfg;
    private readonly HttpClient _http = new();

    public HostsService(ConfigStoreService cfg) => _cfg = cfg;

    private string FilePath => Path.Combine(_cfg.ConfigDir, "hosts-profiles.json");

    /// <summary>系统 hosts 文件路径（Windows）。</summary>
    public static string SystemHostsPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public IReadOnlyList<HostsProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<HostsProfile>();
            var list = JsonSerializer.Deserialize<List<HostsProfile>>(File.ReadAllText(FilePath));
            return list ?? new List<HostsProfile>();
        }
        catch { return new List<HostsProfile>(); }
    }

    public void Save(IEnumerable<HostsProfile> profiles)
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败不打断 UI */ }
    }

    /// <summary>把一份 hosts 文本解析成条目（忽略空行 / 纯注释 / 非法行）。</summary>
    public static List<HostsEntry> ParseText(string text)
    {
        var entries = new List<HostsEntry>();
        if (string.IsNullOrWhiteSpace(text)) return entries;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("!")) continue;

            var enabled = true;
            if (line.StartsWith("#"))
            {
                enabled = false;
                line = line[1..].Trim();
                if (line.StartsWith("===")) continue;   // 跳过 NewToys 受管块标记
            }

            // 去掉行尾注释
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash].Trim();

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!System.Net.IPAddress.TryParse(parts[0], out _)) continue;

            entries.Add(new HostsEntry
            {
                Ip = parts[0],
                Host = parts[1],
                Enabled = enabled
            });
        }
        return entries;
    }

    /// <summary>下载并解析远程 hosts 文本。</summary>
    public async Task<(bool Ok, string Msg, List<HostsEntry> Entries)> ImportFromUrlAsync(string url)
    {
        try
        {
            var text = await _http.GetStringAsync(url).ConfigureAwait(false);
            var entries = ParseText(text);
            return (true, $"已导入 {entries.Count} 条（来自 {url}）", entries);
        }
        catch (Exception ex)
        {
            return (false, "导入失败：" + ex.Message, new List<HostsEntry>());
        }
    }

    /// <summary>把方案写入系统 hosts 的受管块。需要管理员权限；无权限返回提示。</summary>
    public (bool Ok, string Msg) Apply(HostsProfile profile)
    {
        try
        {
            var sysPath = SystemHostsPath;
            var sb = new StringBuilder();
            sb.AppendLine(ManagedStart);
            foreach (var e in profile.Entries) sb.AppendLine(e.ToLine());
            sb.AppendLine(ManagedEnd);
            var block = sb.ToString();

            var existing = File.Exists(sysPath) ? File.ReadAllText(sysPath) : "";
            string next;
            var start = existing.IndexOf(ManagedStart, StringComparison.Ordinal);
            var end = existing.IndexOf(ManagedEnd, StringComparison.Ordinal);

            if (start >= 0 && end >= 0 && end > start)
            {
                // 替换原有受管块
                var before = existing[..start].TrimEnd('\n', '\r');
                var after = existing[(end + ManagedEnd.Length)..].TrimStart('\n', '\r');
                next = before + "\n\n" + block + "\n" + after;
            }
            else
            {
                next = (existing.TrimEnd('\n', '\r') + "\n\n" + block).TrimEnd('\n') + "\n";
            }

            File.WriteAllText(sysPath, next);
            return (true, $"已应用「{profile.Name}」到系统 hosts（{profile.Entries.Count} 条）。");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "没有权限写入系统 hosts：请以管理员身份运行本程序，或用右侧「导出」复制后手动粘贴。");
        }
        catch (Exception ex)
        {
            return (false, "应用失败：" + ex.Message);
        }
    }

    /// <summary>把方案的「受管块」文本导出（无权限写入时的兜底）。</summary>
    public string ExportBlock(HostsProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ManagedStart);
        foreach (var e in profile.Entries) sb.AppendLine(e.ToLine());
        sb.AppendLine(ManagedEnd);
        return sb.ToString();
    }
}
