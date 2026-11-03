using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NewToys.MediaAssets.Models;
using NewToys.Shared.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 资产库的数据访问入口（需求 §6.6）：存储源 / 资产 / 文件位置的读写 + **扫描匹配**所需的查询 + 扫描任务记录。
/// <para>
/// 匹配顺序（<see cref="FindMatch"/>）：① 存储源+相对路径 → ② quick_hash+size（同内容换路径 = 移动）→
/// ③ inode+device（仅本地）→ ④ 调用方算完完整 hash 后再走 <see cref="FindLocationByHash"/>。
/// ⛔ 全程参数化 SQL，⛔ 不拼字符串（路径里可能有引号）。
/// </para>
/// <para>物理操作（重命名 / 移动 / 复制 / 删除）与操作日志在 ；本类只做数据层。</para>
/// </summary>
public partial class AssetService
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

    /// <summary>
    /// 直接插入一个存储源并返回 id：**调用方必须已确认该 root 不存在**（如扫描时用内存字典判过）。
    /// <para>⛔ 不走 <see cref="UpsertStorage"/>：后者每次都 <c>ListStorages()</c> 全表查，一目录一源时会被放大成 N 次全表扫。</para>
    /// </summary>
    public long InsertStorageFast(string name, string rootPath)
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO storages(name,root_path,type,online,created_at,updated_at) " +
                              "VALUES($n,$p,'local',1,$c,$u); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$p", NormalizeRoot(rootPath));
            cmd.Parameters.AddWithValue("$c", Now());
            cmd.Parameters.AddWithValue("$u", Now());
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    /// <summary>只改存储源的显示名（⛔ 不动 `root_path`、⛔ 不动磁盘目录名）。</summary>
    public void RenameStorage(long id, string name)
    {
        if (!IsAvailable || id == 0 || string.IsNullOrWhiteSpace(name)) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE storages SET name=$n, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$n", name.Trim());
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
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

    /// <summary>改存储源类型（local / removable / nas）：只改分类，⛔ 不动位置索引与磁盘。</summary>
    public void SetStorageType(long id, string type)
    {
        if (!IsAvailable || id == 0 || string.IsNullOrWhiteSpace(type)) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE storages SET type=$t, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$t", type);
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

    // ==================== 存储源统计 / 错误（存储源页） ====================

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

    /// <summary>某存储源最近一次扫描的时间（列表「最近扫描」列；没有过扫描 → 空串）。</summary>
    /// <remarks>
    /// 先读 <c>storages.last_scan_at</c>：一次「扫描全部」只建一条 scan_jobs（storage_id 为 NULL），
    /// 按 job 查会把没单独扫过的源全显示成「从未扫描」。老库没这列的值时回退 scan_jobs。
    /// </remarks>
    public string LastScanText(long storageId)
    {
        if (!IsAvailable) return "";
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT IFNULL((SELECT last_scan_at FROM storages WHERE id=$sid AND last_scan_at IS NOT NULL), '');";
            cmd.Parameters.AddWithValue("$sid", storageId);
            if (cmd.ExecuteScalar() is string s && s.Length > 0) return s;

            using var jobCmd = conn.CreateCommand();
            jobCmd.CommandText =
                "SELECT IFNULL(MAX(started_at),'') FROM scan_jobs WHERE storage_id=$sid;";
            jobCmd.Parameters.AddWithValue("$sid", storageId);
            return jobCmd.ExecuteScalar() is string v ? v : "";
        }
        catch { return ""; }
    }

    /// <summary>记一次某存储源的扫描时刻（扫描逐个源完成时写，⛔ 不走 scan_jobs）。</summary>
    public void SetStorageLastScan(long storageId, string at)
    {
        if (!IsAvailable || storageId == 0) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE storages SET last_scan_at=$t WHERE id=$sid;";
            cmd.Parameters.AddWithValue("$t", at);
            cmd.Parameters.AddWithValue("$sid", storageId);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略：只是显示用的时间戳 */ }
    }

}
