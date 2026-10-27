using System;
using System.IO;
using System.IO.Compression;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <b>数据节 · 抓取规则</b>：<c>&lt;StoreDir&gt;\script\rules\*.py</c> 本体。
/// <para>
/// 🔴 为什么**默认勾选**且必须由本模块注册：规则是「唯一无法重建的资产」（缓存 HTML 可重建、
/// 库里的元数据可重建，唯有用户手写的表达式丢了就没了）；而规则目录的位置只有 RuleFileService 知道，
/// 框架层 ⛔ 不引模块 ⇒ 按 <see cref="IBackupSection"/> 契约由模块自己提供（与脚本文件节同一套通道）。
/// </para>
/// <para>只备 <c>rules\</c>，⛔ 不备 <c>cache\</c>（抓下来的 HTML 随时能重抓）。</para>
/// <para>恢复落到 <c>store\restore-&lt;时间戳&gt;\web-rules\</c> —— ⛔ 不覆盖现有规则目录。</para>
/// </summary>
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
