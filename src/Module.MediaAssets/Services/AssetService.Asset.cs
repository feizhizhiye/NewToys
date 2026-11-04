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

    /// <summary>
    /// 回收站里**过期**的资产（<c>status='trashed'</c> 且 <c>updated_at</c> 早于 cutoff）。
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
    /// 改**评分 + 描述**（详情卡的逻辑操作；两者一起写，避免两次 UPDATE）。
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
    /// 改**收藏**（详情卡图标组的「」；即时生效即落盘，⛔ 不走「保存」、⛔ 不动磁盘）。
    /// </summary>
    public bool SetFavorite(long assetId, bool favorite)
    {
        if (assetId <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET is_favorite=$f, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$f", favorite ? 1 : 0);
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", assetId);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// 删掉一条文件位置（**永久删除**流程用 —— 磁盘文件已经真删了，库里这条索引就没意义了）。
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
    /// 删掉资产本体（**永久删除**流程用）—— 连带它的文件位置 / 标签 / 分类 / 集合挂接 / 关联。
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

}
