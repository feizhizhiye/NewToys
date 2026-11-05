using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Shell.Services;

/// <summary>数据节 · 模块数据库：store\&lt;库键&gt;\&lt;库键&gt;.db 全部库文件的一致快照。（WHY: SHL-022）</summary>
public sealed class DataDbBackupSection : IBackupSection
{
    private readonly ConfigStoreService _store;

    public DataDbBackupSection(ConfigStoreService store) => _store = store;

    public string Id => "data.db";

    public string Title => "模块数据库";

    public string Description => "store\\<模块>\\<模块>.db（媒体资产 / 视频 / 网络 / 脚本，逐个 VACUUM INTO 快照）";

    public BackupScope Scope => BackupScope.Data;

    public int Order => 90;

    public bool DefaultSelected => false;

    public bool IsMachineBound => false;

    public int SchemaVersion => 2;

    /// <summary>要打包的库文件（<c>store\</c> 下的 <c>*.db</c>）。</summary>
    private List<string> EnumerateDatabases()
    {
        var root = _store.StoreDir;
        if (!Directory.Exists(root)) return new List<string>();

        return Directory.EnumerateFiles(root, "*.db", SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(root, f);
                // 归档（legacy\）与恢复副本（restore-*）、半成品（*.partial）都不是"当前数据"
                if (rel.StartsWith("legacy" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
                if (rel.StartsWith("restore-", StringComparison.OrdinalIgnoreCase)) return false;
                if (f.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public long EstimateBytes()
    {
        try
        {
            long total = 0;
            foreach (var path in EnumerateDatabases())
            {
                if (File.Exists(path)) total += new FileInfo(path).Length;
                var wal = path + "-wal";                       // WAL 里可能还压着没 checkpoint 的事务（估算用，不必精确）
                if (File.Exists(wal)) total += new FileInfo(wal).Length;
            }
            return total;
        }
        catch { return 0; }
    }

    public void ExportTo(Stream stream)
    {
        var root = _store.StoreDir;
        var tmpDir = Path.Combine(Path.GetTempPath(), "newtoys-bk-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tmpDir);

            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var db in EnumerateDatabases())
            {
                var rel = Path.GetRelativePath(root, db);      // assets\assets.db
                var snapshot = Path.Combine(tmpDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);

                if (!Snapshot(db, snapshot))
                {
                    System.Diagnostics.Debug.WriteLine($"[NewToys][备份] 快照失败，跳过：{db}");
                    continue;
                }

                // db 是二进制、压缩收益低且慢 ⇒ 内层也存不压
                BackupPaths.AddFile(zip, rel, snapshot, CompressionLevel.NoCompression);
            }
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* 临时目录清不掉不影响结果 */ }
        }
    }

    /// <summary>单库一致快照（<c>VACUUM INTO</c>；只读打开源库，不干扰正在跑的应用）。</summary>
    private static bool Snapshot(string source, string target)
    {
        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = source,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $p;";
            cmd.Parameters.AddWithValue("$p", target);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][备份] VACUUM INTO 失败：" + ex.Message);
            return false;
        }
    }

    public (int Ok, int Skipped) RestoreFrom(ZipArchiveEntry entry)
    {
        var root = BackupPaths.CurrentRestoreDir(_store.StoreDir);
        var ok = 0;
        var skipped = 0;

        using var inner = new ZipArchive(entry.Open(), ZipArchiveMode.Read);
        foreach (var item in inner.Entries)
        {
            if (item.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
            if (!item.FullName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
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

/// <summary>数据节 · 缩略图：cache\assets\thumbs（设置里改过目录则用改过的）。（WHY: SHL-023）</summary>
public sealed class DataThumbsBackupSection : IBackupSection
{
    private readonly ConfigStoreService _store;

    public DataThumbsBackupSection(ConfigStoreService store) => _store = store;

    public string Id => "data.thumbs";

    public string Title => "缩略图缓存";

    public string Description => "cache\\assets\\thumbs（媒体资产与视频库的抽帧 / 缩放图）";

    public BackupScope Scope => BackupScope.Data;

    public int Order => 91;

    public bool DefaultSelected => false;

    public bool IsMachineBound => false;

    public int SchemaVersion => 2;

    /// <summary>生效的缩略图目录（设置里改过就用改过的；配置坏 / 没有 ⇒ 默认 <c>cache\assets\thumbs</c>）。</summary>
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
            return Path.Combine(_store.CacheDir, "assets", "thumbs");
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
        var root = Path.Combine(BackupPaths.CurrentRestoreDir(_store.StoreDir), "thumbs");
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
