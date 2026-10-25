using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Shell.Services;

/// <summary>
/// <b>设置节</b>：<c>&lt;配置根&gt;\config\**\*.json</c> —— <c>shell.json</c> + <c>modules\&lt;模块&gt;.json</c>。
/// <para>
/// 这是「设置备份」入口的**主体**（框架与模块设置都在 <c>config</c> 下，一层递归就够，不必按模块拆节）。
/// 恢复 = 覆盖原文件，覆盖前先把现有配置整体归档到 <c>config\.bak-&lt;时间戳&gt;\</c>。
/// </para>
/// <para>
/// 🔴 <c>data\</c>（库 / 缩略图）**不在**本节里 —— 它属 <c>DataBackupSection</c>（批次 B），默认不勾。
/// </para>
/// </summary>
public sealed class ShellBackupSection : IBackupSection
{
    private readonly ConfigStoreService _store;

    public ShellBackupSection(ConfigStoreService store) => _store = store;

    public string Id => "shell";

    public string Title => "应用与模块设置";

    public string Description => "config 目录下的全部设置（框架偏好 shell.json + 各模块设置 modules\\*.json）";

    public BackupScope Scope => BackupScope.Shell;

    public int Order => 20;

    public bool DefaultSelected => true;

    public bool IsMachineBound => false;

    public int SchemaVersion => 1;

    public long EstimateBytes()
    {
        try
        {
            return BackupPaths.EnumerateFiles(_store.ConfigDir, "*.json")
                .Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    public void ExportTo(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in BackupPaths.EnumerateFiles(_store.ConfigDir, "*.json"))
        {
            var rel = Path.GetRelativePath(_store.ConfigDir, file);
            BackupPaths.AddFile(zip, rel, file);
        }
    }

    public (int Ok, int Skipped) RestoreFrom(ZipArchiveEntry entry)
    {
        var configDir = _store.ConfigDir;
        var bakDir = Path.Combine(configDir, $".bak-{DateTime.Now:yyyyMMdd-HHmmss}");

        // ① 先归档现有配置（⛔ 不删；同一秒内重入则复用同一目录）
        foreach (var file in BackupPaths.EnumerateFiles(configDir, "*.json"))
        {
            var rel = Path.GetRelativePath(configDir, file);
            var dst = Path.Combine(bakDir, rel);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                if (!File.Exists(dst)) File.Copy(file, dst);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NewToys][备份] 归档失败（继续恢复）：{rel} — {ex.Message}");
            }
        }

        // ② 逐条覆盖（路径防御在 BackupPaths 里，顺序：先判穿越 → 再跳归档产物）
        var ok = 0;
        var skipped = 0;
        using var inner = new ZipArchive(entry.Open(), ZipArchiveMode.Read);
        foreach (var item in inner.Entries)
        {
            if (item.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
            if (!BackupPaths.IsSafeRelativePath(item.FullName)) { skipped++; continue; }
            if (!item.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }

            var target = Path.Combine(configDir, BackupPaths.ToLocalPath(item.FullName));
            using var reader = new StreamReader(item.Open());
            AtomicJsonFile.Write(target, reader.ReadToEnd());
            ok++;
        }

        return (ok, skipped);
    }
}
