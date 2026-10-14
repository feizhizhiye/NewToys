using System;
using System.Collections.Generic;
using System.IO;
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

    // ==================== 存储源统计 / 错误（NT-020 存储源页） ====================

    /// <summary>
    /// 一个存储源的统计：文件位置数 / 已用大小 / 缺失位置数 / 按资产类型分布。
    /// ⛔ 三次查询封一个方法（页面临时刷新用，⛔ 不 per-row 查）。
    /// </summary>
    public StorageStats GetStorageStats(long storageId)
    {
        var stats = new StorageStats { StorageId = storageId };
        if (!IsAvailable) return stats;
        try
        {
            using var conn = _db.Db.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(*), IFNULL(SUM(size),0) FROM file_locations WHERE storage_id=$sid;";
                cmd.Parameters.AddWithValue("$sid", storageId);
                using var r = cmd.ExecuteReader();
                if (r.Read()) { stats.FileCount = r.GetInt32(0); stats.TotalSize = r.GetInt64(1); }
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(*) FROM file_locations WHERE storage_id=$sid AND status='missing';";
                cmd.Parameters.AddWithValue("$sid", storageId);
                stats.MissingCount = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = conn.CreateCommand())
            {
                // 按类型分布要 join assets（位置表本身没有类型）—— 一次 GROUP BY 拿全
                cmd.CommandText =
                    "SELECT a.type, COUNT(*), IFNULL(SUM(l.size),0) " +
                    "FROM file_locations l JOIN assets a ON a.id = l.asset_id " +
                    "WHERE l.storage_id=$sid GROUP BY a.type ORDER BY COUNT(*) DESC;";
                cmd.Parameters.AddWithValue("$sid", storageId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    stats.ByType.Add((r.GetString(0), r.GetInt32(1), r.GetInt64(2)));
            }
        }
        catch { /* 返回已读到的部分 */ }
        return stats;
    }

    /// <summary>一个存储源最近的扫描错误（`scan_errors` ⋈ `scan_jobs` 按 storage_id 过滤；新的在前）。</summary>
    public List<(string Path, string Message, string CreatedAt)> ListStorageScanErrors(long storageId, int limit = 5)
    {
        var list = new List<(string, string, string)>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT IFNULL(e.path,''), e.message, e.created_at " +
                "FROM scan_errors e JOIN scan_jobs j ON j.id = e.scan_job_id " +
                "WHERE j.storage_id=$sid ORDER BY e.id DESC LIMIT $lim;";
            cmd.Parameters.AddWithValue("$sid", storageId);
            cmd.Parameters.AddWithValue("$lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        }
        catch { /* 返回已读到的 */ }
        return list;
    }

    /// <summary>某存储源最近一次扫描的开始时间（列表「最近扫描」列；没有过扫描 → 空串）。</summary>
    public string LastScanText(long storageId)
    {
        if (!IsAvailable) return "";
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT IFNULL(MAX(started_at),'') FROM scan_jobs WHERE storage_id=$sid;";
            cmd.Parameters.AddWithValue("$sid", storageId);
            var v = cmd.ExecuteScalar();
            return v is string s ? s : "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// NT-021：回收站里**过期**的资产（<c>status='trashed'</c> 且 <c>updated_at</c> 早于 cutoff）。
    /// 供「到期自动清理」用；只读 id + 名字（⛔ 不读位置——删除时再按需取）。
    /// </summary>
    public List<(long Id, string Name)> ListExpiredTrashedAssets(string cutoffText)
    {
        var list = new List<(long, string)>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id, IFNULL(display_name, IFNULL(title,'')) FROM assets " +
                "WHERE status='trashed' AND (updated_at IS NULL OR updated_at < $cut) ORDER BY updated_at;";
            cmd.Parameters.AddWithValue("$cut", cutoffText);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(1);
                list.Add((r.GetInt64(0), name.Length > 0 ? name : "资产 #" + r.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
        catch { /* 返回已读到的 */ }
        return list;
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

    /// <summary>
    /// NT-018：改**评分 + 描述**（详情卡的逻辑操作；两者一起写，避免两次 UPDATE）。
    /// ⛔ 不动磁盘、⛔ 不动显示名（显示名走 <see cref="SetDisplayName"/>）。
    /// </summary>
    public bool SetRatingAndDescription(long assetId, int rating, string? description)
    {
        if (assetId <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET rating=$r, description=$d, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$r", Math.Clamp(rating, 0, 5));
            cmd.Parameters.AddWithValue("$d", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", assetId);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// NT-019：删掉一条文件位置（**永久删除**流程用 —— 磁盘文件已经真删了，库里这条索引就没意义了）。
    /// ⛔ 只删索引行，⛔ 不碰磁盘。
    /// </summary>
    public bool DeleteLocation(long locationId)
    {
        if (locationId <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM file_locations WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", locationId);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// NT-019：删掉资产本体（**永久删除**流程用）—— 连带它的文件位置 / 标签 / 分类 / 集合挂接 / 关联。
    /// ⛔ 只删索引，⛔ 不碰磁盘（磁盘文件的删除由 <c>FileOperationService</c> 负责）。
    /// </summary>
    public bool DeleteAsset(long assetId)
    {
        if (assetId <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            foreach (var sql in new[]
                     {
                         "DELETE FROM file_locations WHERE asset_id=$id;",
                         "DELETE FROM asset_tags WHERE asset_id=$id;",
                         "DELETE FROM asset_categories WHERE asset_id=$id;",
                         "DELETE FROM collection_assets WHERE asset_id=$id;",
                         "DELETE FROM relations WHERE source_asset_id=$id OR target_asset_id=$id;",
                         "DELETE FROM assets WHERE id=$id;"
                     })
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$id", assetId);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch { return false; }
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

    /// <summary>取某个存储源下的**全部**文件位置（扫描前一次性建内存索引用；⛔ 别在遍历里 per-file 调）。</summary>
    public List<FileLocation> ListStorageLocations(long storageId)
    {
        var list = new List<FileLocation>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT " + LocationColumns + " FROM file_locations WHERE storage_id=$sid;";
            cmd.Parameters.AddWithValue("$sid", storageId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(MapLocation(r));
        }
        catch { /* 返回已读到的部分 */ }
        return list;
    }

    /// <summary>
    /// **一个事务**批量写入新位置并回填 Id（扫描性能的关键之一）。
    /// 逐条 INSERT 时每条都要等一次 WAL 提交；成批提交通常快一个数量级。
    /// </summary>
    public void InsertLocations(IReadOnlyList<FileLocation> list)
    {
        if (!IsAvailable || list.Count == 0) return;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO file_locations(asset_id,storage_id,path,relative_path,file_name,ext,mime_type,size,mtime," +
                "hash,quick_hash,inode,device,is_primary,status,last_seen_at,created_at,updated_at) " +
                "VALUES($aid,$sid,$path,$rel,$fn,$ext,$mime,$size,$mtime,$hash,$qhash,$inode,$dev,$prim,$st,$seen,$c,$u); " +
                "SELECT last_insert_rowid();";
            foreach (var l in list)
            {
                cmd.Parameters.Clear();
                BindLocation(cmd, l);
                l.Id = Convert.ToInt64(cmd.ExecuteScalar());
            }
            tx.Commit();
        }
        catch { /* 出错时这批的 Id 保持原值（0）→ 调用方跳过即可 */ }
    }

    /// <summary>
    /// 批量刷新「本次见到了」：只更新 <c>last_seen_at</c>（扫描收尾据此判 missing）。
    /// 分批用 <c>IN (...)</c>，⛔ 不逐条 UPDATE（1 万条逐条 = 1 万次写事务）。
    /// </summary>
    public void TouchLocations(IReadOnlyList<long> ids, string seenAt)
    {
        if (!IsAvailable || ids.Count == 0) return;
        const int chunk = 500;                  // SQLite 的参数上限远大于此，500 是语句长度与事务成本的折中
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            for (var i = 0; i < ids.Count; i += chunk)
            {
                var batch = ids.Skip(i).Take(chunk).ToList();
                var marks = string.Join(",", batch.Select((_, k) => "$p" + k));
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE file_locations SET last_seen_at=$seen, status='active' WHERE id IN ({marks});";
                cmd.Parameters.AddWithValue("$seen", seenAt);
                for (var k = 0; k < batch.Count; k++) cmd.Parameters.AddWithValue("$p" + k, batch[k]);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { /* 忽略：最坏结果是这些位置被判 missing，下次扫描会回来 */ }
    }

    // ==================== 列表页行查询（NT-016） ====================

    /// <summary>
    /// 一次查询拼出列表页的行（**资产 + 主位置 + 存储源名**），再把标签 / 分类 / 集合**批量**补齐。
    /// <para>
    /// ⛔ 为什么不在 VM 里对每个资产再查位置：那是 N+1（500 行 = 500 次查询）。这里主位置用
    /// <c>LEFT JOIN (子查询取 is_primary 优先的第一条)</c> 一次带出；三类组织信息各用**一条**查询
    /// 拉回全部映射再拼，总查询数恒定 = 4。
    /// </para>
    /// </summary>
    public List<AssetRow> QueryRows(string? type = null, bool includeTrashed = false, int limit = 500)
    {
        var rows = new List<AssetRow>();
        if (!IsAvailable) return rows;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            var sql =
                "SELECT a.id, a.type, a.title, a.display_name, a.status, a.rating, a.description, " +
                "       IFNULL(l.file_name,''), IFNULL(l.path,''), IFNULL(l.size,0), l.mtime, " +
                "       IFNULL(l.status,'active'), IFNULL(l.storage_id,0), IFNULL(s.name,''), a.updated_at " +
                "FROM assets a " +
                "LEFT JOIN file_locations l ON l.id = (SELECT f.id FROM file_locations f " +
                "    WHERE f.asset_id = a.id ORDER BY f.is_primary DESC, f.id LIMIT 1) " +
                "LEFT JOIN storages s ON s.id = l.storage_id " +
                "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(type)) { sql += " AND a.type=$t"; cmd.Parameters.AddWithValue("$t", type); }
            if (!includeTrashed) sql += " AND a.status <> 'trashed'";
            sql += " ORDER BY a.updated_at DESC LIMIT $lim;";
            cmd.Parameters.AddWithValue("$lim", limit);
            cmd.CommandText = sql;

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new AssetRow
                {
                    Id = r.GetInt64(0),
                    Type = r.IsDBNull(1) ? AssetTypes.Other : r.GetString(1),
                    Title = r.IsDBNull(2) ? null : r.GetString(2),
                    DisplayName = r.IsDBNull(3) ? null : r.GetString(3),
                    Status = r.IsDBNull(4) ? AssetStatuses.Active : r.GetString(4),
                    Rating = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    Description = r.IsDBNull(6) ? null : r.GetString(6),
                    FileName = r.GetString(7),
                    FilePath = r.GetString(8),
                    Size = r.GetInt64(9),
                    Mtime = r.IsDBNull(10) ? null : r.GetString(10),
                    LocationStatus = r.IsDBNull(11) ? LocationStatuses.Active : r.GetString(11),
                    StorageId = r.GetInt64(12),
                    StorageName = r.GetString(13),
                    UpdatedAt = r.IsDBNull(14) ? null : r.GetString(14),
                    Name = !r.IsDBNull(3) && r.GetString(3).Length > 0 ? r.GetString(3)
                        : !r.IsDBNull(2) && r.GetString(2).Length > 0 ? r.GetString(2)
                        : "资产 #" + r.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            }
        }
        catch { return rows; }

        // 三类组织信息：各一条查询拉全量映射（行数有限，⛔ 不 per-row 查）
        var tags = LoadNameMap("asset_tags", "tags");
        var cats = LoadNameMap("asset_categories", "categories");
        var cols = LoadNameMap("collection_assets", "collections");
        foreach (var row in rows)
        {
            if (tags.TryGetValue(row.Id, out var t)) row.TagsText = t;
            if (cats.TryGetValue(row.Id, out var c)) row.CategoriesText = c;
            if (cols.TryGetValue(row.Id, out var k)) row.CollectionsText = k;
        }
        return rows;
    }

    /// <summary>
    /// 取「资产 id → 逗号分隔的名字串」（标签 / 分类 / 集合通用）。
    /// <paramref name="linkTable"/> 必须有 <c>asset_id</c> 与 <c>{tag|category|collection}_id</c> 两列。
    /// </summary>
    private Dictionary<long, string> LoadNameMap(string linkTable, string nameTable)
    {
        var map = new Dictionary<long, string>();
        if (!IsAvailable) return map;

        // 三类链接表的外键列名各不相同（tag_id / category_id / collection_id）→ 由名字表单数推出
        var fk = nameTable switch
        {
            "tags" => "tag_id",
            "categories" => "category_id",
            "collections" => "collection_id",
            _ => "tag_id"
        };

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            // ⛔ 表名 / 列名来自上面的固定映射（⛔ 不接受外部输入），不是用户数据 → 拼接安全
            cmd.CommandText = $"SELECT l.asset_id, n.name FROM {linkTable} l JOIN {nameTable} n ON n.id = l.{fk} ORDER BY n.name;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                var name = r.GetString(1);
                map[id] = map.TryGetValue(id, out var old) ? old + ", " + name : name;
            }
        }
        catch { /* 表还没数据 / 库只读 → 返回已读到的 */ }
        return map;
    }

    /// <summary>按**绝对路径**找资产 id（NT-022 导入去重 / 用户数据恢复按路径重建映射用；找不到 → null）。</summary>
    public long? FindAssetIdByPath(string path)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT asset_id FROM file_locations WHERE path=$p LIMIT 1;";
            cmd.Parameters.AddWithValue("$p", path);
            var v = cmd.ExecuteScalar();
            return v is null or DBNull ? null : Convert.ToInt64(v);
        }
        catch { return null; }
    }

    /// <summary>
    /// 批量取「资产 id → 组织名**列表**」（标签 / 分类 / 集合；NT-022 导出用，保真名字不丢逗号）。
    /// 分块 <c>IN (...)</c>（⛔ 不 per-row 查；SQLite 参数上限远大于每块 400）。
    /// </summary>
    public Dictionary<long, List<string>> LoadOrgLists(IReadOnlyCollection<long> assetIds, string linkTable, string nameTable)
    {
        var map = new Dictionary<long, List<string>>();
        if (!IsAvailable || assetIds.Count == 0) return map;

        var fk = nameTable switch
        {
            "tags" => "tag_id",
            "categories" => "category_id",
            "collections" => "collection_id",
            _ => "tag_id"
        };

        var ids = assetIds.Distinct().ToList();
        const int chunk = 400;
        try
        {
            using var conn = _db.Db.Open();
            for (var i = 0; i < ids.Count; i += chunk)
            {
                var batch = ids.Skip(i).Take(chunk).ToList();
                var marks = string.Join(",", batch.Select((_, k) => "$p" + k));
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    $"SELECT l.asset_id, n.name FROM {linkTable} l JOIN {nameTable} n ON n.id = l.{fk} " +
                    $"WHERE l.asset_id IN ({marks}) ORDER BY n.name;";
                for (var k = 0; k < batch.Count; k++) cmd.Parameters.AddWithValue("$p" + k, batch[k]);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var id = r.GetInt64(0);
                    if (!map.TryGetValue(id, out var list)) map[id] = list = new List<string>();
                    list.Add(r.GetString(1));
                }
            }
        }
        catch { /* 返回已读到的 */ }
        return map;
    }

    /// <summary>筛选下拉用：全部标签（id + 名字）。NT-018 建好标签后才有内容，之前为空列表。</summary>
    public List<(long Id, string Name)> ListTagOptions() => LoadOptions("tags");

    /// <summary>筛选下拉用：全部分类（id + 名字）。</summary>
    public List<(long Id, string Name)> ListCategoryOptions() => LoadOptions("categories");

    /// <summary>筛选下拉用：全部集合（id + 名字）。</summary>
    public List<(long Id, string Name)> ListCollectionOptions() => LoadOptions("collections");

    private List<(long Id, string Name)> LoadOptions(string table)
    {
        var list = new List<(long, string)>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT id, name FROM {table} ORDER BY name;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1)));
        }
        catch { /* 忽略 */ }
        return list;
    }

    /// <summary>
    /// 只改显示名（`display_name`）—— 详情卡改名走这里。
    /// ⛔ **绝不动磁盘文件**：物理改名是显式操作，归 NT-019。
    /// </summary>
    public void SetDisplayName(long assetId, string? name)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET display_name=$n, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$n", string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim());
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// v3.29「💾 加入库」：把**预览扫描**出来的内存行批量落库（一个事务，全部成功或全部不写）。
    /// <para>
    /// 行可能来自多个目录（预览一个目录 / 多选文件都走这里）→ 按所在目录分组，
    /// 各自登记或复用存储源（<c>root_path</c> 去重）。⛔ 只建索引，⛔ 不碰磁盘文件。
    /// </para>
    /// </summary>
    public int CommitRows(IReadOnlyList<AssetRow> rows)
    {
        if (!IsAvailable || rows.Count == 0) return 0;

        // ① 按目录分组（同目录共用一个存储源）
        var groups = new Dictionary<string, List<AssetRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var dir = Path.GetDirectoryName(r.FilePath);
            if (string.IsNullOrEmpty(dir)) continue;
            if (!groups.TryGetValue(dir, out var list)) groups[dir] = list = new List<AssetRow>();
            list.Add(r);
        }
        if (groups.Count == 0) return 0;

        var done = 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();

            foreach (var (dir, list) in groups)
            {
                var trimmed = dir.TrimEnd('\\', '/');
                var dirName = Path.GetFileName(trimmed);
                if (string.IsNullOrEmpty(dirName)) dirName = trimmed;

                var storage = FindStorageByRoot(dir);
                if (storage == null)
                {
                    // ⚠️ 内联登记（⛔ 不能调 UpsertStorage —— 它自己开连接，会跑出本事务）
                    using var sCmd = conn.CreateCommand();
                    sCmd.Transaction = tx;
                    sCmd.CommandText =
                        "INSERT INTO storages(name,root_path,type,online,created_at,updated_at) " +
                        "VALUES($n,$p,'local',1,$c,$u); SELECT last_insert_rowid();";
                    sCmd.Parameters.AddWithValue("$n", dirName);
                    sCmd.Parameters.AddWithValue("$p", trimmed);
                    sCmd.Parameters.AddWithValue("$c", Now());
                    sCmd.Parameters.AddWithValue("$u", Now());
                    storage = new StorageSource { Id = Convert.ToInt64(sCmd.ExecuteScalar()), RootPath = trimmed, Name = dirName };
                }

                foreach (var r in list)
                {
                    using var aCmd = conn.CreateCommand();
                    aCmd.Transaction = tx;
                    aCmd.CommandText =
                        "INSERT INTO assets(type,title,display_name,status,rating,metadata_json,created_at,updated_at) " +
                        "VALUES($type,$title,$disp,'active',0,'{}',$c,$u); SELECT last_insert_rowid();";
                    aCmd.Parameters.AddWithValue("$type", r.Type);
                    aCmd.Parameters.AddWithValue("$title", (object?)r.Title ?? DBNull.Value);
                    aCmd.Parameters.AddWithValue("$disp", (object?)r.DisplayName ?? DBNull.Value);
                    aCmd.Parameters.AddWithValue("$c", Now());
                    aCmd.Parameters.AddWithValue("$u", Now());
                    var assetId = Convert.ToInt64(aCmd.ExecuteScalar());

                    var ext = Path.GetExtension(r.FileName);
                    using var lCmd = conn.CreateCommand();
                    lCmd.Transaction = tx;
                    lCmd.CommandText =
                        "INSERT INTO file_locations(asset_id,storage_id,path,relative_path,file_name,ext,mime_type,size,mtime," +
                        "is_primary,status,last_seen_at,created_at,updated_at) " +
                        "VALUES($aid,$sid,$path,$rel,$fn,$ext,$mime,$size,$mtime,1,'active',$seen,$c,$u);";
                    lCmd.Parameters.AddWithValue("$aid", assetId);
                    lCmd.Parameters.AddWithValue("$sid", storage.Id);
                    lCmd.Parameters.AddWithValue("$path", r.FilePath);
                    lCmd.Parameters.AddWithValue("$rel", Path.GetRelativePath(trimmed, r.FilePath));
                    lCmd.Parameters.AddWithValue("$fn", r.FileName);
                    lCmd.Parameters.AddWithValue("$ext", (object?)ext ?? DBNull.Value);
                    lCmd.Parameters.AddWithValue("$mime", (object?)ScanService.GuessMimeType(ext) ?? DBNull.Value);
                    lCmd.Parameters.AddWithValue("$size", r.Size);
                    lCmd.Parameters.AddWithValue("$mtime", (object?)r.Mtime ?? DBNull.Value);
                    lCmd.Parameters.AddWithValue("$seen", Now());
                    lCmd.Parameters.AddWithValue("$c", Now());
                    lCmd.Parameters.AddWithValue("$u", Now());
                    lCmd.ExecuteNonQuery();

                    r.Id = assetId;
                    r.StorageId = storage.Id;
                    r.StorageName = storage.Name;
                    done++;
                }
            }

            tx.Commit();
        }
        catch { /* 事务已回滚：返回实际提交数（0 = 一条没进） */ }
        return done;
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
