using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 标签服务（NT-018）：`tags` + `asset_tags` 的全部读写。
/// <para>🔴 标签是**用户手动数据**（R1 例外）：重建数据库会丢，靠「导出用户数据」兜底（NT-022）。</para>
/// <para>⛔ 库不可用时全部降级（空列表 / 返回 0），⛔ 不抛。</para>
/// </summary>
public class TagService
{
    private readonly AssetDatabase _db;

    public TagService(AssetDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    // ==================== 读 ====================

    /// <summary>全部标签 + 使用数（标签管理页主列表；按名称排序）。</summary>
    public List<TagRow> List()
    {
        var list = new List<TagRow>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT t.id, t.name, t.namespace, t.color, t.created_at,
                                       (SELECT COUNT(1) FROM asset_tags at WHERE at.tag_id = t.id) AS usage_count
                                FROM tags t
                                ORDER BY t.name COLLATE NOCASE;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new TagRow
                {
                    Id = r.GetInt64(0),
                    Name = r.GetString(1),
                    Namespace = r.IsDBNull(2) ? null : r.GetString(2),
                    Color = r.IsDBNull(3) ? null : r.GetString(3),
                    CreatedAt = r.GetString(4),
                    UsageCount = r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetInt64(5))
                });
            }
        }
        catch { /* 降级为空 */ }
        return list;
    }

    /// <summary>某个资产身上已经打了哪些标签（详情卡色片的初始选中态）。</summary>
    public List<long> ListTagIdsOfAsset(long assetId)
    {
        var list = new List<long>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT tag_id FROM asset_tags WHERE asset_id=$a;";
            cmd.Parameters.AddWithValue("$a", assetId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetInt64(0));
        }
        catch { /* 降级为空 */ }
        return list;
    }

    /// <summary>用了这个标签的资产（标签详情卡；点一下可跳过去）。</summary>
    public List<(long Id, string Name)> ListAssetsOfTag(long tagId, int limit = 200)
    {
        var list = new List<(long, string)>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT a.id, COALESCE(NULLIF(a.display_name,''), NULLIF(a.title,''), '资产 #' || a.id) AS nm
                                FROM assets a
                                JOIN asset_tags at ON at.asset_id = a.id
                                WHERE at.tag_id = $t AND a.status <> 'trashed'
                                ORDER BY nm COLLATE NOCASE
                                LIMIT $lim;";
            cmd.Parameters.AddWithValue("$t", tagId);
            cmd.Parameters.AddWithValue("$lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1)));
        }
        catch { /* 降级为空 */ }
        return list;
    }

    // ==================== 写 ====================

    /// <summary>新建标签（重名 + 同命名空间 → 返回已有的 id，⛔ 不报错）。返回 id（0 = 失败）。</summary>
    public long Create(string name, string? ns = null, string? color = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || !IsAvailable) return 0;

        var existing = FindId(name, ns);
        if (existing > 0) return existing;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            var now = AssetService.Now();
            cmd.CommandText = @"INSERT INTO tags(name, namespace, color, created_at, updated_at)
                                VALUES($n,$ns,$c,$t,$t); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$ns", (object?)NullIfEmpty(ns) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$c", (object?)NullIfEmpty(color) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", now);
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        }
        catch { return 0; }
    }

    /// <summary>按名字取 id，没有就建（批量加标签 / 快速新建用）。</summary>
    public long Ensure(string name, string? ns = null) => Create(name, ns);

    /// <summary>改名（同命名空间重名 → 返回 false，界面提示）。</summary>
    public bool Rename(long id, string newName)
    {
        newName = (newName ?? "").Trim();
        if (newName.Length == 0 || id <= 0 || !IsAvailable) return false;

        // 同命名空间内重名 → 拒绝（界面提示改名）；跨命名空间允许同名
        var dup = FindId(newName, null);
        if (dup > 0 && dup != id) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE tags SET name=$n, updated_at=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$n", newName);
            cmd.Parameters.AddWithValue("$t", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>改颜色（写库；⛔ 不改名字派生的默认色逻辑）。</summary>
    public bool SetColor(long id, string? color)
    {
        if (id <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE tags SET color=$c, updated_at=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$c", (object?)NullIfEmpty(color) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>把 <paramref name="sourceId"/> 合并进 <paramref name="targetId"/>：挂接关系后删掉源标签。</summary>
    public bool Merge(long sourceId, long targetId)
    {
        if (sourceId <= 0 || targetId <= 0 || sourceId == targetId || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var now = AssetService.Now();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT OR IGNORE INTO asset_tags(asset_id, tag_id, created_at)
                                    SELECT asset_id, $dst, $t FROM asset_tags WHERE tag_id = $src;";
                cmd.Parameters.AddWithValue("$dst", targetId);
                cmd.Parameters.AddWithValue("$src", sourceId);
                cmd.Parameters.AddWithValue("$t", now);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM asset_tags WHERE tag_id=$src; DELETE FROM tags WHERE id=$src;";
                cmd.Parameters.AddWithValue("$src", sourceId);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch { return false; }
    }

    /// <summary>删标签（连带删挂接关系；⛔ 不动资产 / 磁盘文件）。</summary>
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
                cmd.CommandText = "DELETE FROM asset_tags WHERE tag_id=$id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM tags WHERE id=$id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch { return false; }
    }

    /// <summary>清理「未使用」标签（使用数 = 0）。返回删掉几个。</summary>
    public int DeleteUnused()
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM asset_tags);";
            return cmd.ExecuteNonQuery();
        }
        catch { return 0; }
    }

    /// <summary>把某个资产的标签**整组重设**（详情卡点色片后保存：多的删、少的加）。</summary>
    public bool SetAssetTags(long assetId, IReadOnlyCollection<long> tagIds)
    {
        if (assetId <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var now = AssetService.Now();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM asset_tags WHERE asset_id=$a;";
                cmd.Parameters.AddWithValue("$a", assetId);
                cmd.ExecuteNonQuery();
            }
            foreach (var tid in tagIds.Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO asset_tags(asset_id, tag_id, created_at) VALUES($a,$t,$c);";
                cmd.Parameters.AddWithValue("$a", assetId);
                cmd.Parameters.AddWithValue("$t", tid);
                cmd.Parameters.AddWithValue("$c", now);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch { return false; }
    }

    /// <summary>批量：把同一个标签加到多个资产上（工具条「🏷 加标签」）。返回新增的挂接数。</summary>
    public int AddTagToAssets(IReadOnlyCollection<long> assetIds, long tagId)
    {
        if (tagId <= 0 || assetIds.Count == 0 || !IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var now = AssetService.Now();
            var n = 0;
            foreach (var aid in assetIds.Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO asset_tags(asset_id, tag_id, created_at) VALUES($a,$t,$c);";
                cmd.Parameters.AddWithValue("$a", aid);
                cmd.Parameters.AddWithValue("$t", tagId);
                cmd.Parameters.AddWithValue("$c", now);
                n += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return n;
        }
        catch { return 0; }
    }

    /// <summary>批量：把一个标签从多个资产上摘掉。返回摘掉几条。</summary>
    public int RemoveTagFromAssets(IReadOnlyCollection<long> assetIds, long tagId)
    {
        if (tagId <= 0 || assetIds.Count == 0 || !IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var n = 0;
            foreach (var aid in assetIds.Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM asset_tags WHERE asset_id=$a AND tag_id=$t;";
                cmd.Parameters.AddWithValue("$a", aid);
                cmd.Parameters.AddWithValue("$t", tagId);
                n += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return n;
        }
        catch { return 0; }
    }

    // ==================== 内部 ====================

    private long FindId(string name, string? ns)
    {
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM tags WHERE name=$n AND IFNULL(namespace,'')=IFNULL($ns,'') LIMIT 1;";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$ns", (object?)NullIfEmpty(ns) ?? DBNull.Value);
            var v = cmd.ExecuteScalar();
            return v == null || v is DBNull ? 0 : Convert.ToInt64(v);
        }
        catch { return 0; }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
}
