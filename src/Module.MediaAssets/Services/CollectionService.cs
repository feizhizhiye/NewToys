using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 集合服务（NT-018）：`collections` + `collection_assets`（成员 + 拖拽排序）。
/// <para>手动集合成员可增删 / 可拖排序（写 `sort_order`）；智能集合成员由 <c>rule_json</c> 决定，⛔ 手工增删要被拒。</para>
/// <para>⛔ 库不可用时降级（空 / 0），⛔ 不抛。</para>
/// </summary>
public class CollectionService
{
    private readonly AssetDatabase _db;

    public CollectionService(AssetDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    // ==================== 读 ====================

    public List<CollectionRow> List()
    {
        var list = new List<CollectionRow>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT c.id, c.name, c.description, c.type, c.rule_json, c.created_at,
                                       (SELECT COUNT(1) FROM collection_assets ca WHERE ca.collection_id = c.id) AS member_count
                                FROM collections c
                                ORDER BY c.name COLLATE NOCASE;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new CollectionRow
                {
                    Id = r.GetInt64(0),
                    Name = r.GetString(1),
                    Description = r.IsDBNull(2) ? null : r.GetString(2),
                    Type = r.IsDBNull(3) ? CollectionTypes.Manual : r.GetString(3),
                    RuleJson = r.IsDBNull(4) ? null : r.GetString(4),
                    CreatedAt = r.GetString(5),
                    MemberCount = r.IsDBNull(6) ? 0 : Convert.ToInt32(r.GetInt64(6))
                });
            }
        }
        catch { /* 降级为空 */ }
        return list;
    }

    /// <summary>集合成员 id（**已按 `sort_order` 排序** → 右列表就是用户拖出来的顺序）。</summary>
    public List<long> ListMemberIds(long collectionId)
    {
        var list = new List<long>();
        if (collectionId <= 0 || !IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT asset_id FROM collection_assets WHERE collection_id=$c ORDER BY sort_order, added_at;";
            cmd.Parameters.AddWithValue("$c", collectionId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetInt64(0));
        }
        catch { /* 降级为空 */ }
        return list;
    }

    /// <summary>某个资产属于哪些集合（详情卡多选初始态）。</summary>
    public List<long> ListCollectionIdsOfAsset(long assetId)
    {
        var list = new List<long>();
        if (assetId <= 0 || !IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT collection_id FROM collection_assets WHERE asset_id=$a;";
            cmd.Parameters.AddWithValue("$a", assetId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetInt64(0));
        }
        catch { /* 降级为空 */ }
        return list;
    }

    // ==================== 写 ====================

    /// <summary>新建集合。返回 id（0 = 失败）。</summary>
    public long Create(string name, string type = CollectionTypes.Manual, string? ruleJson = null, string? description = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || !IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            var now = AssetService.Now();
            cmd.CommandText = @"INSERT INTO collections(name, description, type, rule_json, sort_mode, created_at, updated_at)
                                VALUES($n,$d,$t,$r,$m,$c,$c); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$d", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", string.IsNullOrWhiteSpace(type) ? CollectionTypes.Manual : type);
            cmd.Parameters.AddWithValue("$r", (object?)ruleJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$m", "manual");
            cmd.Parameters.AddWithValue("$c", now);
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        }
        catch { return 0; }
    }

    public bool Rename(long id, string name)
    {
        name = (name ?? "").Trim();
        if (id <= 0 || name.Length == 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE collections SET name=$n, updated_at=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$t", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    public bool SetDescription(long id, string? description)
    {
        if (id <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE collections SET description=$d, updated_at=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$d", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>写智能集合的规则（`rule_json`）。</summary>
    public bool SetRule(long id, string? ruleJson)
    {
        if (id <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE collections SET rule_json=$r, updated_at=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$r", (object?)ruleJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>删集合（连带成员挂接；⛔ 不动资产）。</summary>
    public bool Delete(long id)
    {
        if (id <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM collection_assets WHERE collection_id=$id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM collections WHERE id=$id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch { return false; }
    }

    /// <summary>把资产加进集合（追加到末尾）。返回新增数。智能集合 ⛔ 拒绝手工添加。</summary>
    public int AddAssets(long collectionId, IReadOnlyCollection<long> assetIds)
    {
        if (collectionId <= 0 || assetIds.Count == 0 || !IsAvailable) return 0;
        if (IsSmart(collectionId)) return 0;

        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var next = NextSortOrder(conn, tx, collectionId);
            var now = AssetService.Now();
            var n = 0;
            foreach (var aid in assetIds.Where(a => a > 0).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT OR IGNORE INTO collection_assets(collection_id, asset_id, sort_order, added_at)
                                    VALUES($c,$a,$o,$t);";
                cmd.Parameters.AddWithValue("$c", collectionId);
                cmd.Parameters.AddWithValue("$a", aid);
                cmd.Parameters.AddWithValue("$o", next);
                cmd.Parameters.AddWithValue("$t", now);
                if (cmd.ExecuteNonQuery() > 0) { n++; next++; }
            }
            tx.Commit();
            return n;
        }
        catch { return 0; }
    }

    /// <summary>把资产移出集合（⛔ 不删资产）。返回移出数。</summary>
    public int RemoveAssets(long collectionId, IReadOnlyCollection<long> assetIds)
    {
        if (collectionId <= 0 || assetIds.Count == 0 || !IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var n = 0;
            foreach (var aid in assetIds.Where(a => a > 0).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM collection_assets WHERE collection_id=$c AND asset_id=$a;";
                cmd.Parameters.AddWithValue("$c", collectionId);
                cmd.Parameters.AddWithValue("$a", aid);
                n += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return n;
        }
        catch { return 0; }
    }

    /// <summary>整组重设某资产的集合归属（详情卡保存）。智能集合 ⛔ 不参与手工设置。</summary>
    public bool SetAssetCollections(long assetId, IReadOnlyCollection<long> collectionIds)
    {
        if (assetId <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            var manual = new List<long>();
            foreach (var cid in collectionIds.Where(c => c > 0).Distinct())
                if (!IsSmart(cid)) manual.Add(cid);

            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                // 只清手动集合的挂接（智能集合的成员由规则算出来，本来就不在表里）
                cmd.CommandText = @"DELETE FROM collection_assets
                                    WHERE asset_id=$a
                                      AND collection_id IN (SELECT id FROM collections WHERE type='manual');";
                cmd.Parameters.AddWithValue("$a", assetId);
                cmd.ExecuteNonQuery();
            }
            var now = AssetService.Now();
            foreach (var cid in manual)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT OR IGNORE INTO collection_assets(collection_id, asset_id, sort_order, added_at)
                                    VALUES($c,$a,$o,$t);";
                cmd.Parameters.AddWithValue("$c", cid);
                cmd.Parameters.AddWithValue("$a", assetId);
                cmd.Parameters.AddWithValue("$o", NextSortOrder(conn, tx, cid));
                cmd.Parameters.AddWithValue("$t", now);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch { return false; }
    }

    /// <summary>按给定顺序重写 <c>sort_order</c>（成员列表拖拽排序落库）。</summary>
    public bool Reorder(long collectionId, IReadOnlyList<long> orderedAssetIds)
    {
        if (collectionId <= 0 || orderedAssetIds.Count == 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            for (var i = 0; i < orderedAssetIds.Count; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE collection_assets SET sort_order=$o WHERE collection_id=$c AND asset_id=$a;";
                cmd.Parameters.AddWithValue("$o", i);
                cmd.Parameters.AddWithValue("$c", collectionId);
                cmd.Parameters.AddWithValue("$a", orderedAssetIds[i]);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch { return false; }
    }

    // ==================== 内部 ====================

    /// <summary>是不是智能集合（成员由规则决定，⛔ 不允许手工增删）。</summary>
    public bool IsSmart(long collectionId)
    {
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT type FROM collections WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", collectionId);
            var v = cmd.ExecuteScalar() as string;
            return string.Equals(v, CollectionTypes.Smart, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static long NextSortOrder(Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx, long collectionId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT IFNULL(MAX(sort_order), -1) + 1 FROM collection_assets WHERE collection_id=$c;";
        cmd.Parameters.AddWithValue("$c", collectionId);
        var v = cmd.ExecuteScalar();
        return v == null || v is DBNull ? 0 : Convert.ToInt64(v);
    }
}
