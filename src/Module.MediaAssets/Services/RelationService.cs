using System;
using System.Collections.Generic;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 资产关联服务（NT-018）：`relations` —— 字幕之于正片 / 封面之于作品 / 同一作品的不同版本。
/// <para>关联是**逻辑数据**（不动磁盘），每次增删都写 <c>operation_logs</c>（由调用方 VM 负责记）。</para>
/// </summary>
public class RelationService
{
    private readonly AssetDatabase _db;

    public RelationService(AssetDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    /// <summary>某个资产的全部关联（两个方向都算），带对端名字。返回 id（0 = 失败）。</summary>
    public List<Relation> ListFor(long assetId, int limit = 100)
    {
        var list = new List<Relation>();
        if (assetId <= 0 || !IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT r.id, r.source_asset_id, r.target_asset_id, r.relation_type, r.created_at,
                                       COALESCE(NULLIF(a.display_name,''), NULLIF(a.title,''), '资产 #' || a.id) AS other_name
                                FROM relations r
                                LEFT JOIN assets a
                                       ON a.id = CASE WHEN r.source_asset_id = $id THEN r.target_asset_id ELSE r.source_asset_id END
                                WHERE r.source_asset_id = $id OR r.target_asset_id = $id
                                ORDER BY r.id DESC
                                LIMIT $lim;";
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.Parameters.AddWithValue("$lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Relation
                {
                    Id = r.GetInt64(0),
                    SourceAssetId = r.GetInt64(1),
                    TargetAssetId = r.GetInt64(2),
                    RelationType = r.GetString(3),
                    CreatedAt = r.GetString(4),
                    TargetName = r.IsDBNull(5) ? "" : r.GetString(5)
                });
            }
        }
        catch { /* 降级为空 */ }
        return list;
    }

    /// <summary>建立关联（已存在同向同类型 → 返回已有的 id，⛔ 不重复插入）。</summary>
    public long Add(long sourceAssetId, long targetAssetId, string relationType)
    {
        if (sourceAssetId <= 0 || targetAssetId <= 0 || sourceAssetId == targetAssetId || !IsAvailable) return 0;
        if (string.IsNullOrWhiteSpace(relationType)) relationType = RelationTypes.RelatedTo;

        try
        {
            using var conn = _db.Db.Open();
            using (var check = conn.CreateCommand())
            {
                check.CommandText = @"SELECT id FROM relations
                                      WHERE source_asset_id=$s AND target_asset_id=$t AND relation_type=$r LIMIT 1;";
                check.Parameters.AddWithValue("$s", sourceAssetId);
                check.Parameters.AddWithValue("$t", targetAssetId);
                check.Parameters.AddWithValue("$r", relationType);
                var v = check.ExecuteScalar();
                if (v != null && v is not DBNull) return Convert.ToInt64(v);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO relations(source_asset_id, target_asset_id, relation_type, metadata_json, created_at)
                                VALUES($s,$t,$r,'{}',$c); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$s", sourceAssetId);
            cmd.Parameters.AddWithValue("$t", targetAssetId);
            cmd.Parameters.AddWithValue("$r", relationType);
            cmd.Parameters.AddWithValue("$c", AssetService.Now());
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        }
        catch { return 0; }
    }

    /// <summary>删一条关联（⛔ 不动资产）。</summary>
    public bool Remove(long id)
    {
        if (id <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM relations WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }
}
