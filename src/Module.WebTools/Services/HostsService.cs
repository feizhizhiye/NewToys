using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.Core.Services.DataMigrations;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>hosts 方案管理：多套方案落本模块库 web_docs 的 hosts-profiles 文档 （⛔ 不再写 config\hosts-profiles.json）。（WHY: WT-006）</summary>
public class HostsService
{
    /// <summary>受管块起始标记。</summary>
    public const string ManagedStart = "# === NewToys hosts start ===";

    /// <summary>受管块结束标记。</summary>
    public const string ManagedEnd = "# === NewToys hosts end ===";

    private readonly ConfigStoreService _cfg;
    private readonly JsonDocStore _docs;
    private readonly IHostsPlatform _platform;
    private readonly IWebToolsSettingsService _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public HostsService(
        ConfigStoreService cfg,
        WebDb db,
        IHostsPlatform platform,
        IWebToolsSettingsService settings)
    {
        _cfg = cfg;
        _docs = new JsonDocStore(db, "web");
        _platform = platform;
        _settings = settings;
    }

    /// <summary>旧文件（迁移源；搬完是 <c>hosts-profiles.json.legacy</c>）。</summary>
    private string LegacyFile => Path.Combine(_cfg.ConfigDir, WebToolsSchemaContributor.DocHostsProfiles + ".json");

    /// <summary>系统 hosts 文件路径。</summary>
    public string SystemHostsPath => _platform.SystemHostsPath;

    /// <summary>备份目录（<c>&lt;配置根&gt;\store\web\hosts-backup\</c>）。</summary>
    public string BackupDir => Path.Combine(_cfg.StoreDir, "web", "hosts-backup");

    // ==================== 方案 ====================

    public IReadOnlyList<HostsProfile> Load()
    {
        try
        {
            JsonDocMigration.ImportOnce(_docs, WebToolsSchemaContributor.DocHostsProfiles, LegacyFile);
            return _docs.Load<List<HostsProfile>>(WebToolsSchemaContributor.DocHostsProfiles) ?? new List<HostsProfile>();
        }
        catch { return new List<HostsProfile>(); }
    }

    public void Save(IEnumerable<HostsProfile> profiles)
    {
        try
        {
            var list = profiles.ToList();
            if (list.Count == 0) { _docs.Delete(WebToolsSchemaContributor.DocHostsProfiles); return; }   // 空列表即删（懒创建）
            _docs.Save(WebToolsSchemaContributor.DocHostsProfiles, list);
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
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith("!")) continue;

            var enabled = true;
            if (line.StartsWith("#"))
            {
                enabled = false;
                line = line[1..].Trim();
                if (line.StartsWith("===")) continue;   // 跳过 NewToys 受管块标记
            }

            // 去掉行尾注释（保留它，供行级编辑回显）
            var hash = line.IndexOf('#');
            var comment = "";
            if (hash >= 0)
            {
                comment = line[(hash + 1)..].Trim();
                line = line[..hash].Trim();
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!System.Net.IPAddress.TryParse(parts[0], out _)) continue;

            entries.Add(new HostsEntry
            {
                Ip = parts[0],
                Host = parts[1],
                Comment = comment,
                Enabled = enabled
            });
        }
        return entries;
    }

    /// <summary>把一份 hosts 文本拆成行模型（保留非条目行原文 + 标出受管块区间）。</summary>
    public static List<HostsLine> ParseLines(string text)
    {
        var lines = new List<HostsLine>();
        if (text is null) return lines;

        var arr = text.Replace("\r\n", "\n").Split('\n');
        var (found, startLine, endLine, _) = ManagedRange(text);

        for (var i = 0; i < arr.Length; i++)
        {
            var no = i + 1;
            var line = new HostsLine
            {
                No = no,
                Raw = arr[i],
                InManaged = found && no >= startLine && no <= endLine
            };

            var t = arr[i].Trim().TrimStart('\uFEFF');
            if (t.Length == 0 || t.StartsWith('!')) { lines.Add(line); continue; }

            var enabled = true;
            if (t.StartsWith('#'))
            {
                enabled = false;
                t = t[1..].Trim();
                if (t.StartsWith("===")) { lines.Add(line); continue; }   // 受管块标记行
            }

            var hash = t.IndexOf('#');
            var comment = "";
            if (hash >= 0)
            {
                comment = t[(hash + 1)..].Trim();
                t = t[..hash].Trim();
            }

            var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !System.Net.IPAddress.TryParse(parts[0], out _)) { lines.Add(line); continue; }

            line.IsEntry = true;
            line.Enabled = enabled;
            line.Ip = parts[0];
            line.Host = parts[1];
            line.Comment = comment;
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>把行模型还原成 hosts 全文（条目行重新对齐渲染，非条目行原样输出）。</summary>
    public static string RenderLines(IEnumerable<HostsLine> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines)
            sb.AppendLine(l.IsEntry ? HostsText.RenderEntry(l.Ip, l.Host, l.Comment, l.Enabled) : l.Raw);
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>读任意 hosts 文本文件（备份快照 / 导入用）。</summary>
    public static (bool Ok, string Text, string Msg) ReadTextFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return (false, "", "文件不存在：" + path);
            return (true, File.ReadAllText(Path.GetFullPath(path)), "");
        }
        catch (Exception ex) { return (false, "", "读取失败：" + ex.Message); }
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

    /// <summary>读本地 hosts 文本文件（导入用）。</summary>
    public static (bool Ok, string Msg, List<HostsEntry> Entries) ImportFile(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var entries = ParseText(File.ReadAllText(full));
            return (true, $"已导入 {entries.Count} 条（来自 {Path.GetFileName(full)}）", entries);
        }
        catch (Exception ex)
        {
            return (false, "读取失败：" + ex.Message, new List<HostsEntry>());
        }
    }

    // ==================== 系统 hosts 读写 ====================

    /// <summary>读系统 hosts 原文。</summary>
    public (bool Ok, string Text, string Msg) ReadSystemHosts()
    {
        if (!_platform.IsSupported) return (false, "", "当前平台不支持读取系统 hosts");
        try
        {
            var path = _platform.SystemHostsPath;
            if (!File.Exists(path)) return (false, "", "找不到系统 hosts 文件：" + path);
            return (true, File.ReadAllText(path), "");
        }
        catch (Exception ex)
        {
            return (false, "", "读取失败：" + ex.Message);
        }
    }

    /// <summary>探测系统 hosts 是否可写（无管理员时给明确提示，⛔ 静默失败）。</summary>
    public (bool Ok, string Why) CanWrite()
    {
        if (!_platform.IsSupported) return (false, "当前平台不支持修改系统 hosts");
        var path = _platform.SystemHostsPath;
        if (!File.Exists(path)) return (false, "找不到系统 hosts 文件");
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return (true, "");
        }
        catch (UnauthorizedAccessException) { return (false, "需要管理员权限：请以管理员身份运行本程序"); }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>受管块在文本里的行号区间（1 起）与条目数；<c>Found=false</c> 表示没有受管块。</summary>
    public static (bool Found, int StartLine, int EndLine, int Count) ManagedRange(string text)
    {
        if (string.IsNullOrEmpty(text)) return (false, 0, 0, 0);
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var start = -1;
        var end = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (start < 0 && t.StartsWith(ManagedStart, StringComparison.Ordinal)) { start = i; continue; }
            if (start >= 0 && t.StartsWith(ManagedEnd, StringComparison.Ordinal)) { end = i; break; }
        }

        if (start < 0 || end < 0) return (false, 0, 0, 0);

        var count = 0;
        for (var i = start + 1; i < end; i++)
        {
            var body = lines[i].Trim();
            if (body.Length == 0) continue;
            if (body.StartsWith('#')) body = body[1..].Trim();
            if (body.Length == 0) continue;
            var parts = body.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && System.Net.IPAddress.TryParse(parts[0], out _)) count++;
        }

        return (true, start + 1, end + 1, count);
    }

    /// <summary>抽出系统 hosts 受管块里的条目（没有受管块返回空集合）。</summary>
    public static List<HostsEntry> ExtractManagedEntries(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var inside = false;
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith(ManagedStart, StringComparison.Ordinal)) { inside = true; continue; }
            if (t.StartsWith(ManagedEnd, StringComparison.Ordinal)) { inside = false; continue; }
            if (inside) sb.AppendLine(line);
        }
        return ParseText(sb.ToString());
    }

    /// <summary>生成的受管块文本（含起止标记）。</summary>
    public static string BuildManagedBlock(HostsProfile profile) => BuildManagedBlock(profile.Entries);

    /// <summary>生成的受管块文本（含起止标记）。</summary>
    public static string BuildManagedBlock(IEnumerable<HostsEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ManagedStart);
        foreach (var e in entries) sb.AppendLine(e.ToLine());
        sb.AppendLine(ManagedEnd);
        return sb.ToString();
    }

    /// <summary>与当前系统受管块比对（应用前预检）。</summary>
    public HostsDiff DiffAgainstManaged(HostsProfile profile)
    {
        var diff = new HostsDiff();
        var (ok, text, _) = ReadSystemHosts();
        if (!ok) return diff;

        var (found, _, _, _) = ManagedRange(text);
        var mine = profile.Entries.Where(e => e.Enabled).ToList();

        if (!found)
        {
            diff.NoManagedBlock = true;
            diff.Added = mine.Count;
            return diff;
        }

        var before = ToMap(ExtractManagedEntries(text));
        var after = ToMap(mine);

        diff.Added = after.Keys.Count(k => !before.ContainsKey(k));
        diff.Removed = before.Keys.Count(k => !after.ContainsKey(k));
        diff.Changed = after.Count(kv => before.TryGetValue(kv.Key, out var ip) && !string.Equals(ip, kv.Value, StringComparison.OrdinalIgnoreCase));
        return diff;

        static Dictionary<string, string> ToMap(IEnumerable<HostsEntry> entries)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries) map[e.Host] = e.Ip;   // 同域名重复时以最后一条为准（与系统实际生效一致）
            return map;
        }
    }

    /// <summary>把方案写入系统 hosts 的受管块（先备份；成功后可刷新 DNS）。</summary>
    public async Task<(bool Ok, string Msg)> ApplyAsync(HostsProfile profile)
    {
        var (ok, text, msg) = ReadSystemHosts();
        if (!ok) return (false, msg);

        var block = BuildManagedBlock(profile);
        var next = ReplaceManagedBlock(text, block);

        var (written, writeMsg) = WriteWithBackup(next, $"应用方案「{profile.Name}」");
        if (!written) return (false, writeMsg);
        if (!_settings.HostsFlushDnsAfterApply) return (true, writeMsg);

        var flush = await _platform.FlushDnsAsync().ConfigureAwait(false);
        return (true, writeMsg + "；" + (flush.Ok ? flush.Msg : "⚠ " + flush.Msg));
    }

    /// <summary>清除本程序的受管块（用户自有行保留）。</summary>
    public (bool Ok, string Msg) ClearManagedBlock()
    {
        var (ok, text, msg) = ReadSystemHosts();
        if (!ok) return (false, msg);

        var (found, startLine, endLine, _) = ManagedRange(text);
        if (!found) return (true, "系统 hosts 里没有本程序的受管块，无需清除。");

        var next = StripManagedBlock(text);
        var (written, writeMsg) = WriteWithBackup(next, "清除受管块");
        return written ? (true, $"已清除受管块（原第 {startLine}~{endLine} 行）· {writeMsg}") : (false, writeMsg);
    }

    /// <summary>去掉受管块后的全文（纯函数，供草稿用；⛔ 不落盘）。</summary>
    public static string StripManagedBlock(string text)
    {
        var (found, startLine, endLine, _) = ManagedRange(text);
        if (!found) return text;

        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        lines.RemoveRange(startLine - 1, endLine - startLine + 1);
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    }

    /// <summary>手动保存系统 hosts 全文（高危：调用方必须先二次确认）。</summary>
    public (bool Ok, string Msg) WriteRaw(string text)
        => WriteWithBackup(text.Replace("\r\n", "\n"), "手动保存");

    /// <summary>把一段受管块替换进文本（没有则追加到末尾）。</summary>
    private static string ReplaceManagedBlock(string existing, string block)
    {
        var (found, _, _, _) = ManagedRange(existing);
        if (!found) return (existing.TrimEnd('\n', '\r') + "\n\n" + block).TrimEnd('\n') + "\n";

        var text = existing.Replace("\r\n", "\n");
        var lines = text.Split('\n').ToList();
        var (_, startLine, endLine, _) = ManagedRange(text);
        lines.RemoveRange(startLine - 1, endLine - startLine + 1);

        var head = string.Join("\n", lines.Take(startLine - 1)).TrimEnd('\n');
        var tail = string.Join("\n", lines.Skip(startLine - 1)).TrimStart('\n');
        var sb = new StringBuilder();
        if (head.Length > 0) sb.Append(head).Append("\n\n");
        sb.Append(block);
        if (tail.Length > 0) sb.Append('\n').Append(tail);
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    // ==================== 备份 ====================

    public List<HostsBackupInfo> ListBackups()
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return new List<HostsBackupInfo>();
            return new DirectoryInfo(BackupDir)
                .GetFiles("hosts-*.bak")
                .Select(f => new HostsBackupInfo
                {
                    Path = f.FullName,
                    Time = f.LastWriteTime,
                    Bytes = f.Length
                })
                .OrderByDescending(b => b.Time)
                .ToList();
        }
        catch { return new List<HostsBackupInfo>(); }
    }

    /// <summary>手动/自动备份系统 hosts 原文（返回备份文件路径）。</summary>
    public (bool Ok, string Msg, string Path) BackupNow(string reason)
    {
        try
        {
            Directory.CreateDirectory(BackupDir);
            var path = _platform.SystemHostsPath;
            var text = File.Exists(path) ? File.ReadAllText(path) : "";
            var file = Path.Combine(BackupDir, $"hosts-{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak");
            File.WriteAllText(file, text);

            PruneBackups(_settings.HostsBackupKeep);
            return (true, $"已备份（{reason}）→ {Path.GetFileName(file)}", file);
        }
        catch (Exception ex)
        {
            return (false, "备份失败：" + ex.Message, "");
        }
    }

    /// <summary>还原某份备份（还原前先把当前文件也备份一份，可反悔）。</summary>
    public (bool Ok, string Msg) Restore(string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath)) return (false, "备份文件不存在：" + backupPath);

            var pre = BackupNow("还原前");
            if (!pre.Ok) return (false, "还原前备份失败，已放弃还原：" + pre.Msg);

            var text = File.ReadAllText(backupPath);
            var (ok, why) = CanWrite();
            if (!ok) return (false, why);

            File.WriteAllText(_platform.SystemHostsPath, text);
            return (true, $"已还原 {Path.GetFileName(backupPath)}（还原前的文件也已备份）");
        }
        catch (Exception ex)
        {
            return (false, "还原失败：" + ex.Message);
        }
    }

    public (bool Ok, string Msg) DeleteBackup(string backupPath)
    {
        try
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            return (true, "已删除备份 " + Path.GetFileName(backupPath));
        }
        catch (Exception ex)
        {
            return (false, "删除备份失败：" + ex.Message);
        }
    }

    /// <summary>只保留最近 N 份（超出删最旧）。</summary>
    private void PruneBackups(int keep)
    {
        try
        {
            var files = new DirectoryInfo(BackupDir).GetFiles("hosts-*.bak")
                .OrderByDescending(f => f.LastWriteTime)
                .Skip(Math.Max(1, keep))
                .ToList();
            foreach (var f in files) f.Delete();
        }
        catch { /* 清理失败不影响主流程 */ }
    }

    // ==================== 内部 ====================

    /// <summary>写系统 hosts（先备份；备份失败 ⇒ 放弃写入）。</summary>
    private (bool Ok, string Msg) WriteWithBackup(string text, string reason)
    {
        var (canWrite, why) = CanWrite();
        if (!canWrite) return (false, "没有权限写系统 hosts：" + why + "（可改用「导出受管块」手动粘贴）");

        var backup = BackupNow(reason);
        if (!backup.Ok) return (false, "备份未成功，已放弃写入 —— " + backup.Msg);

        try
        {
            File.WriteAllText(_platform.SystemHostsPath, text);
            return (true, $"已写入系统 hosts（{reason}；原文件备份为 {Path.GetFileName(backup.Path)}）");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "没有权限写入系统 hosts：请以管理员身份运行本程序，或用「导出」复制后手动粘贴。");
        }
        catch (Exception ex)
        {
            return (false, "写入失败：" + ex.Message);
        }
    }

    /// <summary>把方案的「受管块」文本导出（无权限写入时的兜底）。</summary>
    public string ExportBlock(HostsProfile profile) => BuildManagedBlock(profile);
}
