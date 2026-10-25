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
    /// 取「资产 id → 逗号分隔的名字串」（标签 / 分类 / 集合通用）。
    /// <paramref name="linkTable"/> 必须有 <c>asset_id</c> 与 <c>{tag|category|collection}_id</c> 两列。
    /// <paramref name="ids">分页：只查这些资产的映射（⛔ 不拉全表 —— 全量映射是分页最贵的隐藏消耗）；
    /// <c>null</c> = 全量（非分页调用方用）。</paramref>
    /// </summary>
    private Dictionary<long, string> LoadNameMap(string linkTable, string nameTable, IReadOnlyCollection<long>? ids = null)
    {
        var map = new Dictionary<long, string>();
        if (!IsAvailable) return map;
        if (ids != null && ids.Count == 0) return map;   // 空页 ⇒ 空映射，⛔ 不发无意义查询

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
            var sql = $"SELECT l.asset_id, n.name FROM {linkTable} l JOIN {nameTable} n ON n.id = l.{fk}";
            if (ids != null)
            {
                // id 是 long 值（⛔ 非用户文本）→ 直接拼 IN 列表安全；单页 ≤ 页大小，参数个数可控
                sql += " WHERE l.asset_id IN (" + string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")";
            }
            sql += " ORDER BY n.name;";
            cmd.CommandText = sql;
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

    /// <summary>按**绝对路径**找资产 id（导入去重 / 用户数据恢复按路径重建映射用；找不到 → null）。</summary>
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
    /// 批量取「资产 id → 组织名**列表**」（标签 / 分类 / 集合；导出用，保真名字不丢逗号）。
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

    /// <summary>筛选下拉用：全部标签（id + 名字）。建好标签后才有内容，之前为空列表。</summary>
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

}
