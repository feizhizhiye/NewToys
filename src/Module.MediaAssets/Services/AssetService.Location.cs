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

public partial class AssetService
{

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

    // ==================== 手动校验文件是否还在磁盘上 ====================

    /// <summary>手动校验的目标：一条文件位置（id + 路径 + 当前状态）。</summary>
    public sealed record LocationCheckTarget(long Id, string Path, string Status);

    /// <summary>
    /// 取**可校验**的位置：只含 <c>active</c> / <c>missing</c>。
    /// <para>deleted / moved / error 由写操作自己维护，手动查盘不该把它们翻回 active。</para>
    /// </summary>
    public List<LocationCheckTarget> ListCheckTargets()
    {
        var list = new List<LocationCheckTarget>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, path, status FROM file_locations WHERE status IN ('active','missing') ORDER BY id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LocationCheckTarget(r.GetInt64(0), r.IsDBNull(1) ? "" : r.GetString(1),
                    r.IsDBNull(2) ? LocationStatuses.Active : r.GetString(2)));
        }
        catch { /* 返回已读到的部分 */ }
        return list;
    }

    /// <summary>
    /// 回写手动校验结果：<paramref name="missingIds"/> 置 <c>missing</c>、<paramref name="restoredIds"/> 置 <c>active</c>。
    /// 返回 (标记缺失数, 恢复数)。⛔ 不碰 <c>assets.status</c>、⛔ 不碰磁盘。
    /// </summary>
    public (int Marked, int Restored) ApplyLocationCheck(IReadOnlyList<long> missingIds, IReadOnlyList<long> restoredIds)
    {
        if (!IsAvailable || (missingIds.Count == 0 && restoredIds.Count == 0)) return (0, 0);
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var marked = SetLocationStatus(conn, tx, LocationStatuses.Missing, missingIds);
            var restored = SetLocationStatus(conn, tx, LocationStatuses.Active, restoredIds);
            tx.Commit();
            return (marked, restored);
        }
        catch { return (0, 0); }
    }

    /// <summary>按 id 批量改位置状态（分块 IN，⛔ 不逐条 UPDATE）。</summary>
    private int SetLocationStatus(SqliteConnection conn, SqliteTransaction tx, string status, IReadOnlyList<long> ids)
    {
        const int chunk = 500;
        var total = 0;
        for (var i = 0; i < ids.Count; i += chunk)
        {
            var batch = ids.Skip(i).Take(chunk).ToList();
            var marks = string.Join(",", batch.Select((_, k) => "$p" + k));
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE file_locations SET status=$st, updated_at=$u WHERE id IN ({marks}) AND status IN ('active','missing');";
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$u", Now());
            for (var k = 0; k < batch.Count; k++) cmd.Parameters.AddWithValue("$p" + k, batch[k]);
            total += cmd.ExecuteNonQuery();
        }
        return total;
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
    /// 一次读多个存储源的全部位置（扫描提速：整棵子树一次查完，按源分组 → 换目录不再逐源查库）。
    /// </summary>
    public Dictionary<long, List<FileLocation>> ListLocationsForStorages(IReadOnlyCollection<long> storageIds)
    {
        var map = new Dictionary<long, List<FileLocation>>();
        if (!IsAvailable || storageIds.Count == 0) return map;

        foreach (var id in storageIds) map[id] = new List<FileLocation>();

        // SQLite 参数上限 999 → 分批 IN
        var ids = storageIds.ToList();
        for (var i = 0; i < ids.Count; i += 500)
        {
            var chunk = ids.Skip(i).Take(500).ToList();
            var names = new List<string>(chunk.Count);
            try
            {
                using var conn = _db.Db.Open();
                using var cmd = conn.CreateCommand();
                for (var k = 0; k < chunk.Count; k++)
                {
                    names.Add("$s" + k);
                    cmd.Parameters.AddWithValue("$s" + k, chunk[k]);
                }
                cmd.CommandText = "SELECT " + LocationColumns + " FROM file_locations WHERE storage_id IN (" +
                                  string.Join(",", names) + ");";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var loc = MapLocation(r);
                    if (map.TryGetValue(loc.StorageId, out var list)) list.Add(loc);
                    else map[loc.StorageId] = new List<FileLocation> { loc };
                }
            }
            catch { /* 读不了就退化成空索引：扫描照常建新位置 */ }
        }
        return map;
    }

    /// <summary>
    /// 扫描收尾（**一个事务**）：把所有本次没见到的位置标 missing + 给这些源记「最近扫描」。
    /// <para>一次扫描现在会涉及整棵子树的多个源 ⇒ 逐个源跑两遍 UPDATE 会变成几千条往返。</para>
    /// 返回标成 missing 的位置数。
    /// </summary>
    public int FinishStorageScans(IReadOnlyCollection<long> storageIds, string scannedFrom)
    {
        if (!IsAvailable || storageIds.Count == 0) return 0;

        var ids = storageIds.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return 0;

        var missing = 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();

            for (var i = 0; i < ids.Count; i += 500)
            {
                var chunk = ids.Skip(i).Take(500).ToList();
                var names = new List<string>(chunk.Count);
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                for (var k = 0; k < chunk.Count; k++)
                {
                    names.Add("$s" + k);
                    cmd.Parameters.AddWithValue("$s" + k, chunk[k]);
                }
                var inList = string.Join(",", names);

                cmd.CommandText =
                    "UPDATE file_locations SET status='missing', updated_at=$u " +
                    "WHERE status='active' AND (last_seen_at IS NULL OR last_seen_at < $from) " +
                    "AND storage_id IN (" + inList + ");";
                cmd.Parameters.AddWithValue("$u", Now());
                cmd.Parameters.AddWithValue("$from", scannedFrom);
                missing += cmd.ExecuteNonQuery();

                using var stamp = conn.CreateCommand();
                stamp.Transaction = tx;
                for (var k = 0; k < chunk.Count; k++) stamp.Parameters.AddWithValue("$s" + k, chunk[k]);
                stamp.CommandText = "UPDATE storages SET last_scan_at=$t WHERE id IN (" + inList + ");";
                stamp.Parameters.AddWithValue("$t", scannedFrom);
                stamp.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { /* 校准失败不影响已入库的数据 */ }
        return missing;
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

    // ==================== 列表页行查询 ====================

}
