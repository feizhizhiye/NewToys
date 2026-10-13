using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 分类服务（NT-018）：`categories` + `asset_categories`。
/// <para>分类是**树**（`parent_id`）：分类页是左树 + 右成员列表，树由 VM 按 <c>Depth</c> 扁平化后交给 `ItemsControl` 自绘。</para>
/// <para>⛔ 库不可用时降级（空 / 0），⛔ 不抛。</para>
/// </summary>
public class CategoryService
{
    private readonly AssetDatabase _db;

    public CategoryService(AssetDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    // ==================== 读 ====================

    /// <summary>全部分类（未算 <c>Depth</c>，由 VM 扁平化时填）。</summary>
    public List<CategoryNode> List()
    {
        var list = new List<CategoryNode>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT c.id, c.name, c.parent_id, c.sort_order,
                                       (SELECT COUNT(1) FROM asset_categories ac WHERE ac.category_id = c.id) AS member_count
                                FROM categories c
                                ORDER BY c.sort_order, c.name COLLATE NOCASE;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new CategoryNode
                {
                    Id = r.GetInt64(0),
                    Name = r.GetString(1),
                    ParentId = r.IsDBNull(2) ? null : r.GetInt64(2),
                    SortOrder = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetInt64(3)),
                    MemberCount = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetInt64(4))
                });
            }
        }
        catch { /* 降级为空 */ }
        return list;
    }

    /// <summary>某个分类下的资产 id（右成员列表用；VM 再拿这些 id 去 `AssetService` 取行）。</summary>
    public List<long> ListMemberIds(long categoryId)
    {
        var list = new List<long>();
        if (categoryId <= 0 || !IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT asset_id FROM asset_categories WHERE category_id=$c;";
            cmd.Parameters.AddWithValue("$c", categoryId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetInt64(0));
        }
        catch { /* 降级为空 */ }
        return list;
    }

    /// <summary>某个资产属于哪些分类（详情卡多选框的初始态）。</summary>
    public List<long> ListCategoryIdsOfAsset(long assetId)
    {
        var list = new List<long>();
        if (assetId <= 0 || !IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT category_id FROM asset_categories WHERE asset_id=$a;";
            cmd.Parameters.AddWithValue("$a", assetId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetInt64(0));
        }
        catch { /* 降级为空 */ }
        return list;
    }

    // ==================== 写 ====================

    /// <summary>新建分类（<paramref name="parentId"/> = null 建根）。返回 id（0 = 失败）。</summary>
    public long Create(string name, long? parentId = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || !IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            var now = AssetService.Now();
            cmd.CommandText = @"INSERT INTO categories(name, parent_id, sort_order, created_at, updated_at)
                                VALUES($n,$p,0,$t,$t); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", now);
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
            cmd.CommandText = "UPDATE categories SET name=$n, updated_at=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$t", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>移动分类到新父节点。🔴 防环：⛔ 不许移到自己或自己的子孙下（否则树会自环、展开时死循环）。</summary>
    public bool Move(long id, long? newParentId)
    {
        if (id <= 0 || !IsAvailable) return false;
        if (newParentId == id) return false;
        if (newParentId.HasValue && SubtreeIds(id).Contains(newParentId.Value)) return false;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE categories SET parent_id=$p, updated_at=$t WHERE id=$id;";
            cmd.Parameters.AddWithValue("$p", (object?)newParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>删除分类**及其整棵子树**（连带 `asset_categories` 挂接）。返回删掉的分类数。</summary>
    public int DeleteSubtree(long id)
    {
        if (id <= 0 || !IsAvailable) return 0;
        var ids = SubtreeIds(id);
        if (ids.Count == 0) return 0;

        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var param = string.Join(",", ids.Select((_, i) => "$p" + i));

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM asset_categories WHERE category_id IN ({param});";
                for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue("$p" + i, ids[i]);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM categories WHERE id IN ({param});";
                for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue("$p" + i, ids[i]);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return ids.Count;
        }
        catch { return 0; }
    }

    /// <summary>某个分类（含子孙，<paramref name="includeChildren"/> = true 时）的 id 集合。</summary>
    public List<long> SubtreeIds(long id, bool includeChildren = true)
    {
        var all = List();
        var result = new List<long>();
        var stack = new Stack<long>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (result.Contains(cur)) continue;      // 防脏数据自环
            result.Add(cur);
            if (!includeChildren) continue;
            foreach (var c in all.Where(n => n.ParentId == cur)) stack.Push(c.Id);
        }
        return result;
    }

    /// <summary>整组重设某个资产的分类（详情卡保存）。</summary>
    public bool SetAssetCategories(long assetId, IReadOnlyCollection<long> categoryIds)
    {
        if (assetId <= 0 || !IsAvailable) return false;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM asset_categories WHERE asset_id=$a;";
                cmd.Parameters.AddWithValue("$a", assetId);
                cmd.ExecuteNonQuery();
            }
            var first = true;
            foreach (var cid in categoryIds.Where(c => c > 0))
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO asset_categories(asset_id, category_id, is_primary) VALUES($a,$c,$p);";
                cmd.Parameters.AddWithValue("$a", assetId);
                cmd.Parameters.AddWithValue("$c", cid);
                cmd.Parameters.AddWithValue("$p", first ? 1 : 0);   // 第一个 = 主分类
                cmd.ExecuteNonQuery();
                first = false;
            }
            tx.Commit();
            return true;
        }
        catch { return false; }
    }

    /// <summary>批量：把资产加进某个分类（右成员列表 / 后续拖拽用）。返回新增挂接数。</summary>
    public int AddAssets(long categoryId, IReadOnlyCollection<long> assetIds)
    {
        if (categoryId <= 0 || assetIds.Count == 0 || !IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var n = 0;
            foreach (var aid in assetIds.Where(a => a > 0).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO asset_categories(asset_id, category_id, is_primary) VALUES($a,$c,0);";
                cmd.Parameters.AddWithValue("$a", aid);
                cmd.Parameters.AddWithValue("$c", categoryId);
                n += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return n;
        }
        catch { return 0; }
    }

    /// <summary>批量摘出（⛔ 只删挂接，不删资产）。</summary>
    public int RemoveAssets(long categoryId, IReadOnlyCollection<long> assetIds)
    {
        if (categoryId <= 0 || assetIds.Count == 0 || !IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            var n = 0;
            foreach (var aid in assetIds.Where(a => a > 0).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM asset_categories WHERE category_id=$c AND asset_id=$a;";
                cmd.Parameters.AddWithValue("$c", categoryId);
                cmd.Parameters.AddWithValue("$a", aid);
                n += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return n;
        }
        catch { return 0; }
    }
}
