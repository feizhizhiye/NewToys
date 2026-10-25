using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Shell.Services;

/// <summary>
/// <b>数据节 · 总库</b>（批次 B）：<c>data\newtoys.db</c> 的一致快照。
/// <para>
/// 🔴 导出**必须**走 <c>VACUUM INTO</c>（<see cref="SqliteDatabase.VacuumInto"/>）：
/// 库开了 WAL，直接 <c>File.Copy</c> 会漏掉还没落盘的 <c>-wal</c> 事务，恢复出来是"少一截"的库。
/// </para>
/// <para>
/// 🔴 恢复**落到新文件**：<c>data\restore-&lt;时间戳&gt;\newtoys.db</c> —— ⛔ 不覆盖当前库
/// （当前库还被进程持有，覆盖 = 让正在跑的页面全部指向一份半截数据）。
/// 用户要拿它当当前库，自己改名 / 换配置档。
/// </para>
/// <para>默认不勾：库可能几十上百 MB，绝大多数「备份设置」的场景不需要它。</para>
/// </summary>
public sealed class DataDbBackupSection : IBackupSection
{
    private const string EntryName = "newtoys.db";

    private readonly ConfigStoreService _store;
    private readonly SqliteDatabase _db;

    public DataDbBackupSection(ConfigStoreService store, SqliteDatabase db)
    {
        _store = store;
        _db = db;
    }

    public string Id => "data.db";

    public string Title => "数据库（newtoys.db）";

    public string Description => "索引与手工数据总库：资产 / 视频索引 / 脚本库 / 书签等（VACUUM INTO 一致快照）";

    public BackupScope Scope => BackupScope.Data;

    public int Order => 90;

    public bool DefaultSelected => false;

    public bool IsMachineBound => false;

    public int SchemaVersion => 1;

    public long EstimateBytes()
    {
        try
        {
            var path = _db.DatabasePath;
            long total = File.Exists(path) ? new FileInfo(path).Length : 0;

            // WAL 里可能还压着没 checkpoint 的事务（估算用，不必精确）
            var wal = path + "-wal";
            if (File.Exists(wal)) total += new FileInfo(wal).Length;
            return total;
        }
        catch { return 0; }
    }

    public void ExportTo(Stream stream)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "newtoys-bk-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tmpDir);
            // 🔴 与资产页「另存库文件」同一个实现（SqliteDatabase.VacuumIntoFile）
            var snapshot = Path.Combine(tmpDir, EntryName);
            if (!_db.VacuumIntoFile(snapshot, out var error))
                throw new InvalidOperationException("数据库快照失败：" + error);

            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            // db 是二进制、压缩收益低且慢 ⇒ 内层也存不压
            BackupPaths.AddFile(zip, EntryName, snapshot, CompressionLevel.NoCompression);
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* 临时目录清不掉不影响结果 */ }
        }
    }

    public (int Ok, int Skipped) RestoreFrom(ZipArchiveEntry entry)
    {
        using var inner = new ZipArchive(entry.Open(), ZipArchiveMode.Read);
        var item = inner.GetEntry(EntryName)
                   ?? inner.Entries.FirstOrDefault(e => !e.FullName.EndsWith("/", StringComparison.Ordinal));
        if (item == null) return (0, 1);

        var target = Path.Combine(BackupPaths.CurrentRestoreDir(_store.DataDir), EntryName);
        using var src = item.Open();
        using var dst = File.Create(target);
        src.CopyTo(dst);
        return (1, 0);
    }
}

/// <summary>
/// <b>数据节 · 缩略图</b>（批次 B）：<c>data\thumbs</c>（设置里改过目录则用改过的）。
/// <para>
/// 目录来源：<c>config\modules\media-assets.json</c> 的 <c>ThumbsDirectory</c>（空 = <c>data\thumbs</c>）。
/// Shell 只读这个字段，⛔ 不引 <c>Module.MediaAssets</c>（Release 下 Shell 不引任何模块）。
/// </para>
/// <para>恢复同样落到 <c>data\restore-&lt;时间戳&gt;\thumbs\</c> —— ⛔ 不覆盖当前缩略图目录。</para>
/// </summary>
public sealed class DataThumbsBackupSection : IBackupSection
{
    private readonly ConfigStoreService _store;

    public DataThumbsBackupSection(ConfigStoreService store) => _store = store;

    public string Id => "data.thumbs";

    public string Title => "缩略图缓存";

    public string Description => "data\\thumbs（媒体资产与视频库的抽帧 / 缩放图）";

    public BackupScope Scope => BackupScope.Data;

    public int Order => 91;

    public bool DefaultSelected => false;

    public bool IsMachineBound => false;

    public int SchemaVersion => 1;

    /// <summary>生效的缩略图目录（设置里改过就用改过的；配置坏 / 没有 ⇒ 默认 <c>data\thumbs</c>）。</summary>
    public string ThumbsDirectory
    {
        get
        {
            try
            {
                var dir = _store.ModulesConfigDir;
                var path = Path.Combine(dir, "media-assets.json");
                if (!File.Exists(path)) path = Path.Combine(_store.ConfigDir, "media-assets.json");   // 迁移前旧位置

                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject obj)
                {
                    var custom = (obj["thumbsDirectory"] ?? obj["ThumbsDirectory"])?.GetValue<string>() ?? "";
                    custom = custom.Trim();
                    if (custom.Length > 0) return custom;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[NewToys][备份] 读取缩略图目录失败（用默认）：" + ex.Message);
            }
            return Path.Combine(_store.DataDir, "thumbs");
        }
    }

    public long EstimateBytes()
    {
        try
        {
            var root = ThumbsDirectory;
            if (!Directory.Exists(root)) return 0;
            return new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch { return 0; }
    }

    public void ExportTo(Stream stream)
    {
        var root = ThumbsDirectory;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        if (!Directory.Exists(root)) return;

        foreach (var file in BackupPaths.EnumerateFiles(root))
        {
            var rel = Path.GetRelativePath(root, file);
            // jpg / png 本身已压缩 ⇒ 内层也存不压
            BackupPaths.AddFile(zip, rel, file, CompressionLevel.NoCompression);
        }
    }

    public (int Ok, int Skipped) RestoreFrom(ZipArchiveEntry entry)
    {
        var root = Path.Combine(BackupPaths.CurrentRestoreDir(_store.DataDir), "thumbs");
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

        if (ok > 0)
            System.Diagnostics.Debug.WriteLine($"[NewToys][备份] 缩略图已恢复到 {root}（{ok} 个文件）");

        return (ok, skipped);
    }
}
