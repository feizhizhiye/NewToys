using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NewToys.BackupCenter.Models;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 归档包清单（SQLite <c>backup.db</c> 的 <c>archives</c> 表）。
/// <para>
/// 记录与磁盘**双向对齐**：<see cref="SyncWithDisk"/> 把目录里没登记过的 <c>.7z</c> 补进来（顺便算 SHA256），
/// 把文件已不存在的记录剔掉 —— 用户手工删包 / 拷包进来都能反映出来。
/// </para>
/// </summary>
public sealed class ArchiveCatalog
{
    private const string LegacyMigration = "catalog-json";

    private readonly BackupDb _db;
    private readonly BackupSettingsService _paths;
    private readonly BackupPlanStore _plans;

    public ArchiveCatalog(BackupDb db, BackupSettingsService paths, BackupPlanStore plans)
    {
        _db = db;
        _paths = paths;
        _plans = plans;
        Load();
    }

    public List<ArchiveRecord> Items { get; private set; } = new();

    /// <summary>库文件全路径（界面显示用）。</summary>
    public string FilePath => _db.DatabasePath;

    public string ArchiveDir => _paths.ArchiveDir;

    public void Load()
    {
        Items = _db.IsReady ? ReadAll() : new();
        MigrateLegacyJson();
    }

    /// <summary>全量写回（包数量级小，整表重写比逐条 diff 便宜）。</summary>
    public void Save()
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM archives;";
                del.ExecuteNonQuery();
            }
            foreach (var r in Items)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO archives (file_name, plan_id, plan_name, size_bytes, created_at, sha256) " +
                    "VALUES ($f, $pid, $pname, $size, $created, $sha);";
                cmd.Parameters.AddWithValue("$f", r.FileName);
                cmd.Parameters.AddWithValue("$pid", r.PlanId ?? "");
                cmd.Parameters.AddWithValue("$pname", r.PlanName ?? "");
                cmd.Parameters.AddWithValue("$size", r.SizeBytes);
                cmd.Parameters.AddWithValue("$created", r.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("$sha", r.Sha256 ?? "");
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 包清单写入失败：" + ex.Message);
        }
    }

    public void Add(ArchiveRecord record)
    {
        Items.Add(record);
        Save();
    }

    public bool Remove(ArchiveRecord record)
    {
        if (!Items.Remove(record)) return false;
        Save();
        return true;
    }

    /// <summary>与磁盘对齐并重算文件大小（SHA256 只给新出现的文件算，已有记录不重算）。</summary>
    public (int Added, int Removed) SyncWithDisk()
    {
        var dir = ArchiveDir;
        var added = 0;
        var removed = 0;

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return (0, 0);
        }

        var files = Directory.GetFiles(dir, "*.7z").ToList();

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var rec = Items[i];
            if (!File.Exists(Path.Combine(dir, rec.FileName))) { Items.RemoveAt(i); removed++; }
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (Items.Exists(r => string.Equals(r.FileName, name, StringComparison.OrdinalIgnoreCase))) continue;

            var info = new FileInfo(file);
            Items.Add(new ArchiveRecord
            {
                FileName = name,
                PlanId = GuessPlanId(name),
                PlanName = GuessPlanName(name),
                SizeBytes = info.Length,
                CreatedAt = info.LastWriteTime,
                Sha256 = ComputeSha256(file)
            });
            added++;
        }

        if (added > 0 || removed > 0) Save();
        return (added, removed);
    }

    /// <summary>整包 SHA256（大写十六进制）。</summary>
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>按文件名反查任务：<c>&lt;任务名&gt;-&lt;yyyyMMdd-HHmmss&gt;.7z</c>。</summary>
    private string GuessPlanId(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var idx = stem.LastIndexOf('-');
        if (idx <= 0) return "";
        var prefix = stem[..idx];
        var plan = _plans.Items.FirstOrDefault(p => string.Equals(p.Name, prefix, StringComparison.OrdinalIgnoreCase));
        return plan?.Id ?? "";
    }

    private string GuessPlanName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var idx = stem.LastIndexOf('-');
        return idx <= 0 ? stem : stem[..idx];
    }

    private List<ArchiveRecord> ReadAll()
    {
        var list = new List<ArchiveRecord>();
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT file_name, plan_id, plan_name, size_bytes, created_at, sha256 " +
                "FROM archives ORDER BY created_at DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ArchiveRecord
                {
                    FileName = reader.GetString(0),
                    PlanId = reader.GetString(1),
                    PlanName = reader.GetString(2),
                    SizeBytes = reader.GetInt64(3),
                    CreatedAt = ParseDate(reader.GetString(4)),
                    Sha256 = reader.GetString(5)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 包清单读取失败：" + ex.Message);
        }
        return list;
    }

    private static DateTime ParseDate(string text)
        => DateTime.TryParse(text, out var v) ? v : default;

    /// <summary>首次启动把旧 <c>catalog.json</c> 导进库（导完标记，⛔ 旧文件不删）。</summary>
    private void MigrateLegacyJson()
    {
        if (!_db.IsReady || _db.IsMigrated(LegacyMigration)) return;
        try
        {
            if (File.Exists(_paths.CatalogFile))
            {
                var loaded = JsonSerializer.Deserialize<List<ArchiveRecord>>(
                    File.ReadAllText(_paths.CatalogFile));
                if (loaded is { Count: > 0 })
                {
                    Items = loaded;
                    Save();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] catalog.json 迁移失败（保持空清单）：" + ex.Message);
        }
        _db.MarkMigrated(LegacyMigration);
    }
}
