using System;
using System.IO;
using System.IO.Compression;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>数据节 · 抓取规则：&lt;StoreDir&gt;\script\rules\*.py 本体。（WHY: SM-045）</summary>
public sealed class RulesBackupSection : IBackupSection
{
    private readonly RuleFileService _files;
    private readonly ConfigStoreService _cfg;

    public RulesBackupSection(RuleFileService files, ConfigStoreService cfg)
    {
        _files = files;
        _cfg = cfg;
    }

    public string Id => "data.web-rules";

    public string Title => "抓取规则";

    public string Description => "抓取规则集（*.py，含手写表达式）—— 丢了无法重建";

    public BackupScope Scope => BackupScope.Data;

    public int Order => 91;

    /// <summary>🔴 唯一默认勾选的数据节：规则不可重建（其余数据节一律默认不勾）。</summary>
    public bool DefaultSelected => true;

    public bool IsMachineBound => false;

    public int SchemaVersion => 1;

    public long EstimateBytes()
    {
        try
        {
            long total = 0;
            foreach (var file in BackupPaths.EnumerateFiles(_files.RulesDir))
                total += new FileInfo(file).Length;
            return total;
        }
        catch { return 0; }
    }

    public void ExportTo(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        var dir = _files.RulesDir;
        if (!Directory.Exists(dir)) return;

        foreach (var file in BackupPaths.EnumerateFiles(dir))
        {
            var rel = Path.GetRelativePath(dir, file);
            BackupPaths.AddFile(zip, BackupPaths.ToEntryName(rel), file);
        }
    }

    public (int Ok, int Skipped) RestoreFrom(ZipArchiveEntry entry)
    {
        // ⚠️ 恢复不覆盖现有规则：统一落到 store\restore-<ts>\web-rules\（用户自己决定要不要挪回去）
        var root = Path.Combine(BackupPaths.CurrentRestoreDir(_cfg.StoreDir), "web-rules");
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
