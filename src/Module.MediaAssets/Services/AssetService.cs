using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 资产库的数据访问入口（需求 §6.6）：存储源 / 资产 / 文件位置的读写 + **扫描匹配**所需的查询 + 扫描任务记录。
/// <para>
/// 匹配顺序（<see cref="FindMatch"/>）：① 存储源+相对路径 → ② quick_hash+size（同内容换路径 = 移动）→
/// ③ inode+device（仅本地）→ ④ 调用方算完完整 hash 后再走 <see cref="FindLocationByHash"/>。
/// ⛔ 全程参数化 SQL，⛔ 不拼字符串（路径里可能有引号）。
/// </para>
/// <para>物理操作（重命名 / 移动 / 复制 / 删除）与操作日志在 NT-019；本类只做数据层。</para>
/// </summary>
public class AssetService
{
    private readonly AssetDatabase _db;

    public AssetService(AssetDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;
    public string DatabasePath => _db.DatabasePath;

    /// <summary>统一时间戳格式（可排序、可读；对齐 DDL 的 TEXT 列）。</summary>
    public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    // ==================== 存储源 ====================

    public List<StorageSource> ListStorages()
    {
        var list = new List<StorageSource>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id,name,root_path,type,online,created_at,updated_at FROM storages ORDER BY id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new StorageSource
                {
                    Id = r.GetInt64(0),
                    Name = r.GetString(1),
                    RootPath = r.GetString(2),
                    Type = r.GetString(3),
                    Online = r.GetInt64(4) != 0,
                    CreatedAt = r.IsDBNull(5) ? "" : r.GetString(5),
                    UpdatedAt = r.IsDBNull(6) ? "" : r.GetString(6)
                });
            }
        }
        catch { /* 库不可用 → 返回空表，页面自行提示 */ }
        return list;
    }

    /// <summary>按根路径找存储源（扫描前登记用；路径比较忽略大小写与结尾分隔符）。</summary>
    public StorageSource? FindStorageByRoot(string rootPath)
    {
        var normalized = NormalizeRoot(rootPath);
        foreach (var s in ListStorages())
            if (string.Equals(NormalizeRoot(s.RootPath), normalized, StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }

    /// <summary>新增或更新存储源（按 <c>root_path</c> 去重），返回其 id。</summary>
    public long UpsertStorage(StorageSource s)
    {
        if (!IsAvailable) return 0;
        var existing = FindStorageByRoot(s.RootPath);
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            if (existing != null)
            {
                s.Id = existing.Id;
                cmd.CommandText = "UPDATE storages SET name=$n, root_path=$p, type=$t, online=$o, updated_at=$u WHERE id=$id;";
                cmd.Parameters.AddWithValue("$id", existing.Id);
            }
            else
            {
                cmd.CommandText = "INSERT INTO storages(name,root_path,type,online,created_at,updated_at) " +
                                  "VALUES($n,$p,$t,$o,$c,$u); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$c", Now());
            }
            cmd.Parameters.AddWithValue("$n", s.Name);
            cmd.Parameters.AddWithValue("$p", NormalizeRoot(s.RootPath));
            cmd.Parameters.AddWithValue("$t", s.Type);
            cmd.Parameters.AddWithValue("$o", s.Online ? 1 : 0);
            cmd.Parameters.AddWithValue("$u", Now());

            if (existing != null)
            {
                // UPDATE 没有返回值（ExecuteScalar 得到 null → Convert 会变 0）→ 用 ExecuteNonQuery，
                // id 沿用查到的那条，⛔ 别让 s.Id 被 0 覆盖（否则调用方拿到的"存储源 id"全成了 0）。
                cmd.ExecuteNonQuery();
                return existing.Id;
            }

            var id = Convert.ToInt64(cmd.ExecuteScalar());
            s.Id = id;
            return id;
        }
        catch { return 0; }
    }

    /// <summary>置在线状态（移动硬盘 / NAS 离线 = 0 → 扫描**不动**其下位置）。</summary>
    public void SetStorageOnline(long id, bool online)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE storages SET online=$o, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$o", online ? 1 : 0);
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>移除存储源：**只删索引**（其下的文件位置随之删除），⛔ 绝不碰磁盘文件。</summary>
    public void DeleteStorage(long id)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            using (var c1 = conn.CreateCommand())
            {
                c1.Transaction = tx;
                c1.CommandText = "DELETE FROM file_locations WHERE storage_id=$id;";
                c1.Parameters.AddWithValue("$id", id);
                c1.ExecuteNonQuery();
            }
            using (var c2 = conn.CreateCommand())
            {
                c2.Transaction = tx;
                c2.CommandText = "DELETE FROM storages WHERE id=$id;";
                c2.Parameters.AddWithValue("$id", id);
                c2.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { /* 忽略 */ }
    }

    // ==================== 资产 ====================

    public Asset? FindAsset(long id)
    {
        if (!IsAvailable) return null;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id,type,title,display_name,description,status,rating,metadata_json,created_at,updated_at " +
                              "FROM assets WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? MapAsset(r) : null;
        }
        catch { return null; }
    }

    /// <summary>列出资产（按类型过滤；默认不含回收站；按更新时间倒序）。</summary>
    public List<Asset> ListAssets(string? type = null, bool includeTrashed = false, int limit = 500)
    {
        var list = new List<Asset>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            var sql = "SELECT id,type,title,display_name,description,status,rating,metadata_json,created_at,updated_at FROM assets WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(type)) { sql += " AND type=$t"; cmd.Parameters.AddWithValue("$t", type); }
            if (!includeTrashed) sql += " AND status <> 'trashed'";
            sql += " ORDER BY updated_at DESC LIMIT $lim;";
            cmd.Parameters.AddWithValue("$lim", limit);
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(MapAsset(r));
        }
        catch { /* 返回已读到的部分 */ }
        return list;
    }

    public long InsertAsset(Asset a)
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO assets(type,title,display_name,description,status,rating,metadata_json,created_at,updated_at) " +
                "VALUES($type,$title,$disp,$desc,$status,$rating,$meta,$c,$u); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$type", a.Type);
            cmd.Parameters.AddWithValue("$title", (object?)a.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$disp", (object?)a.DisplayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$desc", (object?)a.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", a.Status);
            cmd.Parameters.AddWithValue("$rating", a.Rating);
            cmd.Parameters.AddWithValue("$meta", a.MetadataJson);
            cmd.Parameters.AddWithValue("$c", string.IsNullOrEmpty(a.CreatedAt) ? Now() : a.CreatedAt);
            cmd.Parameters.AddWithValue("$u", Now());
            a.Id = Convert.ToInt64(cmd.ExecuteScalar());
            return a.Id;
        }
        catch { return 0; }
    }

    public void UpdateAsset(Asset a)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE assets SET type=$type, title=$title, display_name=$disp, description=$desc, " +
                "status=$status, rating=$rating, metadata_json=$meta, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$type", a.Type);
            cmd.Parameters.AddWithValue("$title", (object?)a.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$disp", (object?)a.DisplayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$desc", (object?)a.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", a.Status);
            cmd.Parameters.AddWithValue("$rating", a.Rating);
            cmd.Parameters.AddWithValue("$meta", a.MetadataJson);
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", a.Id);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>软删除 / 恢复 / 标丢失（只改状态，⛔ 不删行）。</summary>
    public void SetAssetStatus(long id, string status)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET status=$s, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    // ==================== 文件位置 ====================

    public List<FileLocation> ListLocations(long assetId)
    {
        var list = new List<FileLocation>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT " + LocationColumns + " FROM file_locations WHERE asset_id=$id ORDER BY is_primary DESC, id;";
            cmd.Parameters.AddWithValue("$id", assetId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(MapLocation(r));
        }
        catch { /* 忽略 */ }
        return list;
    }

    /// <summary>① 存储源 + 相对路径（同存储源内最可靠的匹配键）。</summary>
    public FileLocation? FindLocationByRelativePath(long storageId, string relativePath)
        => FindLocation("storage_id=$a AND relative_path=$b", ("$a", storageId), ("$b", relativePath));

    /// <summary>② quick_hash + size（同内容换路径 → 视为**移动**）。</summary>
    public FileLocation? FindLocationByQuickHash(string quickHash, long size)
        => FindLocation("quick_hash=$a AND size=$b", ("$a", quickHash), ("$b", size));

    /// <summary>③ inode + device（仅本地存储源；改名 / 移动识别的兜底）。</summary>
    public FileLocation? FindLocationByInode(string inode, string device)
        => FindLocation("inode=$a AND device=$b", ("$a", inode), ("$b", device));

    /// <summary>④ 完整 hash + size（只有前三步都没命中时才计算完整 hash，再走这里）。</summary>
    public FileLocation? FindLocationByHash(string hash, long size)
        => FindLocation("hash=$a AND size=$b", ("$a", hash), ("$b", size));

    private FileLocation? FindLocation(string where, params (string Name, object Value)[] args)
    {
        if (!IsAvailable) return null;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT " + LocationColumns + $" FROM file_locations WHERE {where} LIMIT 1;";
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
            using var r = cmd.ExecuteReader();
            return r.Read() ? MapLocation(r) : null;
        }
        catch { return null; }
    }

    /// <summary>主位置（详情卡 / 打开文件优先用；没有主位置就取第一条）。</summary>
    public FileLocation? FindPrimaryLocation(long assetId)
    {
        var list = ListLocations(assetId);
        foreach (var l in list) if (l.IsPrimary) return l;
        return list.Count > 0 ? list[0] : null;
    }

    public long InsertLocation(FileLocation l)
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO file_locations(asset_id,storage_id,path,relative_path,file_name,ext,mime_type,size,mtime," +
                "hash,quick_hash,inode,device,is_primary,status,last_seen_at,created_at,updated_at) " +
                "VALUES($aid,$sid,$path,$rel,$fn,$ext,$mime,$size,$mtime,$hash,$qhash,$inode,$dev,$prim,$st,$seen,$c,$u); " +
                "SELECT last_insert_rowid();";
            BindLocation(cmd, l);
            l.Id = Convert.ToInt64(cmd.ExecuteScalar());
            return l.Id;
        }
        catch { return 0; }
    }

    public void UpdateLocation(FileLocation l)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE file_locations SET asset_id=$aid, storage_id=$sid, path=$path, relative_path=$rel, file_name=$fn, " +
                "ext=$ext, mime_type=$mime, size=$size, mtime=$mtime, hash=$hash, quick_hash=$qhash, inode=$inode, " +
                "device=$dev, is_primary=$prim, status=$st, last_seen_at=$seen, updated_at=$u WHERE id=$id;";
            BindLocation(cmd, l);
            cmd.Parameters.AddWithValue("$id", l.Id);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>扫描「见到」了这个位置：刷新 last_seen_at（判 missing 的依据）与状态。</summary>
    public void TouchLocation(long id, string lastSeenAt)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE file_locations SET last_seen_at=$seen, status='active', updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$seen", lastSeenAt);
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 扫描收尾：把该存储源下**本次没见到**（last_seen_at &lt; 本次开始时刻）的位置标 <c>missing</c>。
    /// ⛔ **不删 Asset**（需求 §6.6：资产是库里的主人，位置只是它的落点）。返回标记数量。
    /// </summary>
    public int MarkMissingLocations(long storageId, string scannedFrom)
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE file_locations SET status='missing', updated_at=$u " +
                "WHERE storage_id=$sid AND status='active' AND (last_seen_at IS NULL OR last_seen_at < $from);";
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$sid", storageId);
            cmd.Parameters.AddWithValue("$from", scannedFrom);
            return cmd.ExecuteNonQuery();
        }
        catch { return 0; }
    }

    // ==================== 扫描任务 / 错误 ====================

    public long CreateScanJob(long? storageId, string mode)
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO scan_jobs(storage_id,mode,status,started_at,created_at) VALUES($sid,$mode,'running',$s,$c); " +
                "SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$sid", (object?)storageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mode", mode);
            cmd.Parameters.AddWithValue("$s", Now());
            cmd.Parameters.AddWithValue("$c", Now());
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    /// <summary>回填任务进度 / 结果（结束时传 status = success / cancelled / failed）。</summary>
    public void UpdateScanJob(ScanJob job)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE scan_jobs SET status=$st, total_files=$t, scanned_files=$s, added_files=$a, " +
                "updated_files=$u, missing_files=$m, error_count=$e, finished_at=$f WHERE id=$id;";
            cmd.Parameters.AddWithValue("$st", job.Status);
            cmd.Parameters.AddWithValue("$t", job.TotalFiles);
            cmd.Parameters.AddWithValue("$s", job.ScannedFiles);
            cmd.Parameters.AddWithValue("$a", job.AddedFiles);
            cmd.Parameters.AddWithValue("$u", job.UpdatedFiles);
            cmd.Parameters.AddWithValue("$m", job.MissingFiles);
            cmd.Parameters.AddWithValue("$e", job.ErrorCount);
            cmd.Parameters.AddWithValue("$f", (object?)job.FinishedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", job.Id);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>记一条扫描错误（单个文件失败 ⛔ 不中断整次扫描）。</summary>
    public void AddScanError(long jobId, string? path, string errorType, string message)
    {
        if (!IsAvailable || jobId == 0) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO scan_errors(scan_job_id,path,error_type,message,created_at) VALUES($j,$p,$t,$m,$c);";
            cmd.Parameters.AddWithValue("$j", jobId);
            cmd.Parameters.AddWithValue("$p", (object?)path ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", errorType);
            cmd.Parameters.AddWithValue("$m", message);
            cmd.Parameters.AddWithValue("$c", Now());
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    public List<ScanJob> ListRecentScanJobs(int limit = 50)
    {
        var list = new List<ScanJob>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id,storage_id,mode,status,total_files,scanned_files,added_files,updated_files," +
                "missing_files,error_count,started_at,finished_at,created_at FROM scan_jobs ORDER BY id DESC LIMIT $lim;";
            cmd.Parameters.AddWithValue("$lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ScanJob
                {
                    Id = r.GetInt64(0),
                    StorageId = r.IsDBNull(1) ? null : r.GetInt64(1),
                    Mode = r.GetString(2),
                    Status = r.GetString(3),
                    TotalFiles = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    ScannedFiles = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    AddedFiles = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                    UpdatedFiles = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                    MissingFiles = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                    ErrorCount = r.IsDBNull(9) ? 0 : r.GetInt32(9),
                    StartedAt = r.IsDBNull(10) ? null : r.GetString(10),
                    FinishedAt = r.IsDBNull(11) ? null : r.GetString(11),
                    CreatedAt = r.IsDBNull(12) ? "" : r.GetString(12)
                });
            }
        }
        catch { /* 忽略 */ }
        return list;
    }

    // ==================== 内部：列清单 / 绑定 / 映射 ====================

    private const string LocationColumns =
        "id,asset_id,storage_id,path,relative_path,file_name,ext,mime_type,size,mtime,hash,quick_hash,inode,device," +
        "is_primary,status,last_seen_at,created_at,updated_at";

    private static void BindLocation(SqliteCommand cmd, FileLocation l)
    {
        cmd.Parameters.AddWithValue("$aid", l.AssetId);
        cmd.Parameters.AddWithValue("$sid", l.StorageId);
        cmd.Parameters.AddWithValue("$path", l.Path);
        cmd.Parameters.AddWithValue("$rel", (object?)l.RelativePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fn", l.FileName);
        cmd.Parameters.AddWithValue("$ext", (object?)l.Ext ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mime", (object?)l.MimeType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", l.Size);
        cmd.Parameters.AddWithValue("$mtime", (object?)l.Mtime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", (object?)l.Hash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$qhash", (object?)l.QuickHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inode", (object?)l.Inode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dev", (object?)l.Device ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$prim", l.IsPrimary ? 1 : 0);
        cmd.Parameters.AddWithValue("$st", l.Status);
        cmd.Parameters.AddWithValue("$seen", (object?)l.LastSeenAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", string.IsNullOrEmpty(l.CreatedAt) ? Now() : l.CreatedAt);
        cmd.Parameters.AddWithValue("$u", Now());
    }

    private static FileLocation MapLocation(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        AssetId = r.GetInt64(1),
        StorageId = r.GetInt64(2),
        Path = r.GetString(3),
        RelativePath = r.IsDBNull(4) ? null : r.GetString(4),
        FileName = r.GetString(5),
        Ext = r.IsDBNull(6) ? null : r.GetString(6),
        MimeType = r.IsDBNull(7) ? null : r.GetString(7),
        Size = r.IsDBNull(8) ? 0 : r.GetInt64(8),
        Mtime = r.IsDBNull(9) ? null : r.GetString(9),
        Hash = r.IsDBNull(10) ? null : r.GetString(10),
        QuickHash = r.IsDBNull(11) ? null : r.GetString(11),
        Inode = r.IsDBNull(12) ? null : r.GetString(12),
        Device = r.IsDBNull(13) ? null : r.GetString(13),
        IsPrimary = !r.IsDBNull(14) && r.GetInt64(14) != 0,
        Status = r.IsDBNull(15) ? LocationStatuses.Active : r.GetString(15),
        LastSeenAt = r.IsDBNull(16) ? null : r.GetString(16),
        CreatedAt = r.IsDBNull(17) ? "" : r.GetString(17),
        UpdatedAt = r.IsDBNull(18) ? "" : r.GetString(18)
    };

    private static Asset MapAsset(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Type = r.GetString(1),
        Title = r.IsDBNull(2) ? null : r.GetString(2),
        DisplayName = r.IsDBNull(3) ? null : r.GetString(3),
        Description = r.IsDBNull(4) ? null : r.GetString(4),
        Status = r.IsDBNull(5) ? AssetStatuses.Active : r.GetString(5),
        Rating = r.IsDBNull(6) ? 0 : r.GetInt32(6),
        MetadataJson = r.IsDBNull(7) ? "{}" : r.GetString(7),
        CreatedAt = r.IsDBNull(8) ? "" : r.GetString(8),
        UpdatedAt = r.IsDBNull(9) ? "" : r.GetString(9)
    };

    /// <summary>根路径规范化：去尾部分隔符（⛔ 不解析符号链接 —— 那要读盘，扫描时再按需做）。</summary>
    private static string NormalizeRoot(string rootPath)
    {
        var p = (rootPath ?? "").Trim();
        if (p.Length > 3) p = p.TrimEnd('\\', '/');
        return p;
    }
}
