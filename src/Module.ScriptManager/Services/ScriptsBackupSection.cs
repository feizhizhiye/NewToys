using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>数据节 · 脚本实体文件（批次 C）：脚本管理器采集的 .ps1 / .py / … 本体。（WHY: SM-052）</summary>
public sealed class ScriptsBackupSection : IBackupSection
{
    private const string DefaultAlias = "默认";
    private const string RootsFileName = "_roots.txt";

    private readonly IQuickActionService _quick;
    private readonly IScriptStoreService _store;
    private readonly ConfigStoreService _cfg;

    public ScriptsBackupSection(IQuickActionService quick, IScriptStoreService store, ConfigStoreService cfg)
    {
        _quick = quick;
        _store = store;
        _cfg = cfg;
    }

    public string Id => "data.scripts";

    public string Title => "脚本文件";

    public string Description => "被采集的脚本实体（默认脚本目录 + 各库自定义根）";

    public BackupScope Scope => BackupScope.Data;

    public int Order => 92;

    public bool DefaultSelected => false;

    public bool IsMachineBound => false;

    public int SchemaVersion => 1;

    /// <summary>
    /// 本次要打包的根目录：别名 + 绝对路径。
    /// <para>去重规则：已被前面某个根包含的目录跳过（常见于「集合根 = 默认目录的子目录」）。</para>
    /// </summary>
    private List<(string Alias, string Dir)> Roots()
    {
        var result = new List<(string Alias, string Dir)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string alias, string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar); }
            catch { return; }
            if (!Directory.Exists(full)) return;

            // 已被包含 ⇒ 跳过（避免同一批文件在包里出现两次）
            foreach (var parent in seen)
            {
                if (string.Equals(full, parent, StringComparison.OrdinalIgnoreCase)) return;
                if (full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            }
            seen.Add(full);
            result.Add((Unique(alias, result), full));
        }

        Add(DefaultAlias, Safe(() => _quick.ScriptsDirectory));

        try
        {
            foreach (var c in _store.Data.Collections)
            {
                if (string.IsNullOrWhiteSpace(c.RootPath)) continue;
                Add(string.IsNullOrWhiteSpace(c.Name) ? "库" : c.Name, c.RootPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][备份] 读取脚本库根目录失败（只备默认目录）：" + ex.Message);
        }

        return result;
    }

    private static string Safe(Func<string> get)
    {
        try { return get() ?? ""; } catch { return ""; }
    }

    /// <summary>别名去重（集合重名 / 与「默认」撞名时加序号）。</summary>
    private static string Unique(string alias, List<(string Alias, string Dir)> existing)
    {
        var name = Sanitize(alias);
        if (name.Length == 0) name = "scripts";
        var candidate = name;
        for (var i = 2; existing.Any(r => string.Equals(r.Alias, candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{name}-{i}";
        return candidate;
    }

    private static string Sanitize(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim())
            sb.Append(Array.IndexOf(invalid, ch) >= 0 || ch is '\\' or '/' ? '_' : ch);
        return sb.ToString();
    }

    public long EstimateBytes()
    {
        try
        {
            long total = 0;
            foreach (var (_, dir) in Roots())
                foreach (var file in BackupPaths.EnumerateFiles(dir))
                    total += new FileInfo(file).Length;
            return total;
        }
        catch { return 0; }
    }

    public void ExportTo(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        var roots = Roots();
        var manifest = new StringBuilder();

        foreach (var (alias, dir) in roots)
        {
            manifest.AppendLine($"{alias}\t{dir}");
            foreach (var file in BackupPaths.EnumerateFiles(dir))
            {
                var rel = Path.GetRelativePath(dir, file);
                BackupPaths.AddFile(zip, $"{alias}/{BackupPaths.ToEntryName(rel)}", file);
            }
        }

        if (manifest.Length > 0)
            BackupPaths.AddText(zip, RootsFileName, manifest.ToString());
    }

    public (int Ok, int Skipped) RestoreFrom(ZipArchiveEntry entry)
    {
        // ⚠️ 恢复不去碰原脚本目录：统一落到 store\restore-<ts>\scripts\（用户自己决定要不要覆盖回去）
        var root = Path.Combine(BackupPaths.CurrentRestoreDir(_cfg.StoreDir), "scripts");
        var ok = 0;
        var skipped = 0;

        using var inner = new ZipArchive(entry.Open(), ZipArchiveMode.Read);
        foreach (var item in inner.Entries)
        {
            if (item.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
            if (!BackupPaths.IsSafeRelativePath(item.FullName)) { skipped++; continue; }

            var target = Path.Combine(root, BackupPaths.ToLocalPath(item.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var src = item.Open();
            using var dst = File.Create(target);
            src.CopyTo(dst);
            ok++;
        }

        return (ok, skipped);
    }
}
