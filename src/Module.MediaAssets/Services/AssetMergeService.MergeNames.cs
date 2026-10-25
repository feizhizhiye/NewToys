using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

public partial class AssetMergeService
{

    /// <summary>tags / categories / collections：按名字去重（tags 用 namespace+name），返回 src_id → main_id 映射。</summary>
    private Dictionary<long, long> MergeByName(SqliteConnection conn, SqliteTransaction tx, HashSet<string> have,
                                               string table, string? nsColumn, string nameColumn,
                                               IProgress<string>? progress)
    {
        var map = new Dictionary<long, long>();
        if (!have.Contains(table)) return map;
        progress?.Report($"正在合并{table}…");

        var nsSelect = nsColumn == null ? "''" : $"IFNULL({nsColumn},'')";
        var mainByKey = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT id, {nsSelect}, {nameColumn} FROM main.{table};";
            using var r = cmd.ExecuteReader();
            while (r.Read()) mainByKey[$"{r.GetString(1)}|{r.GetString(2)}"] = r.GetInt64(0);
        }

            var srcRows = new List<(long Id, string Ns, string Name, string Extra1, string Extra2, string Extra3, long? ParentId)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                if (table == "tags")
                    cmd.CommandText = $"SELECT id, {nsSelect}, name, IFNULL(color,''), created_at, updated_at FROM src.{table};";
                else if (table == "categories")
                    cmd.CommandText = $"SELECT id, {nsSelect}, name, '', sort_order, created_at, parent_id FROM src.{table};";
                else
                    cmd.CommandText = $"SELECT id, {nsSelect}, name, IFNULL(type,'manual'), IFNULL(rule_json,''), IFNULL(description,'') FROM src.{table};";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    // ⚠️ categories 的第 5 列是 sort_order（INTEGER）→ GetString 会抛；按表分别读
                    var extra2 = table == "categories"
                        ? (r.IsDBNull(4) ? "0" : r.GetInt64(4).ToString(CultureInfo.InvariantCulture))
                        : r.GetString(4);
                    srcRows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), extra2, r.GetString(5),
                        table == "categories" && !r.IsDBNull(6) ? r.GetInt64(6) : (long?)null));
                }
            }

        foreach (var s in srcRows)
        {
            var key = $"{s.Ns}|{s.Name}";
            if (mainByKey.TryGetValue(key, out var existing)) { map[s.Id] = existing; continue; }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            if (table == "tags")
            {
                cmd.CommandText =
                    "INSERT INTO main.tags(name,namespace,color,created_at,updated_at) VALUES($n,$ns,$c,$cr,$u); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$n", s.Name);
                cmd.Parameters.AddWithValue("$ns", s.Ns.Length == 0 ? DBNull.Value : s.Ns);
                cmd.Parameters.AddWithValue("$c", s.Extra1.Length == 0 ? DBNull.Value : s.Extra1);
                cmd.Parameters.AddWithValue("$cr", s.Extra2.Length == 0 ? AssetService.Now() : s.Extra2);
                cmd.Parameters.AddWithValue("$u", AssetService.Now());
            }
            else if (table == "categories")
            {
                // parent_id 先不映射（父分类可能排在后面）→ 挂顶级；⛔ 不建错父子
                cmd.CommandText =
                    "INSERT INTO main.categories(name,parent_id,sort_order,created_at,updated_at) VALUES($n,NULL,$so,$cr,$u); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$n", s.Name);
                cmd.Parameters.AddWithValue("$so", int.TryParse(s.Extra2, out var so) ? so : 0);
                cmd.Parameters.AddWithValue("$cr", s.Extra3.Length == 0 ? AssetService.Now() : s.Extra3);
                cmd.Parameters.AddWithValue("$u", AssetService.Now());
            }
            else
            {
                cmd.CommandText =
                    "INSERT INTO main.collections(name,description,type,rule_json,created_at,updated_at) " +
                    "VALUES($n,$d,$t,$r,$cr,$u); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$n", s.Name);
                cmd.Parameters.AddWithValue("$d", s.Extra3.Length == 0 ? DBNull.Value : s.Extra3);
                cmd.Parameters.AddWithValue("$t", s.Extra1.Length == 0 ? CollectionTypes.Manual : s.Extra1);
                cmd.Parameters.AddWithValue("$r", s.Extra2.Length == 0 ? DBNull.Value : s.Extra2);
                cmd.Parameters.AddWithValue("$cr", AssetService.Now());
                cmd.Parameters.AddWithValue("$u", AssetService.Now());
            }
            var id = (long)cmd.ExecuteScalar()!;
            map[s.Id] = id;
            mainByKey[key] = id;
        }
        return map;
    }

    /// <summary>
    /// 分类父子修补：<see cref="MergeByName"/> 建分类时先挂顶级（父可能排在后面），
    /// 这里按 <paramref name="catMap"/> 把 src 的 parent_id 重映射回写（父没并进来 → 保持顶级）。
    /// </summary>
    private void FixCategoryParents(SqliteConnection conn, SqliteTransaction tx, HashSet<string> have,
                                    Dictionary<long, long> catMap, IProgress<string>? progress)
    {
        if (!have.Contains("categories") || catMap.Count == 0) return;

        var pairs = new List<(long SrcChild, long? SrcParent)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, parent_id FROM src.categories WHERE parent_id IS NOT NULL;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) pairs.Add((r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1)));
        }

        var fixedCount = 0;
        foreach (var (child, parent) in pairs)
        {
            if (parent == null) continue;
            if (!catMap.TryGetValue(child, out var newChild)) continue;
            if (!catMap.TryGetValue(parent.Value, out var newParent)) continue;   // 父没并进来 → 顶级
            if (newChild == newParent) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE main.categories SET parent_id=$p, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$p", newParent);
            cmd.Parameters.AddWithValue("$u", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", newChild);
            if (cmd.ExecuteNonQuery() > 0) fixedCount++;
        }
        if (fixedCount > 0) progress?.Report($"分类父子关系已修补 {fixedCount} 条。");
    }

    /// <summary>链接表：两端 id 重映射后 <c>INSERT OR IGNORE</c>（主键冲突 = 已挂过 → 跳过）。返回挂接数。</summary>
    private int MergeLinkTable(SqliteConnection conn, SqliteTransaction tx, HashSet<string> have, string table,
                               string fkColumn, Dictionary<long, long> fkMap, Dictionary<long, long> assetMap,
                               IProgress<string>? progress)
    {
        if (!have.Contains(table)) return 0;
        progress?.Report($"正在合并{table}…");

        // ⚠️ 三张链接表的时间列名不同（collection_assets 叫 added_at，⛔ 不是 created_at）
        var stamp = table == "collection_assets" ? "added_at" : "created_at";

        var rows = new List<(long Asset, long Fk)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT asset_id, {fkColumn} FROM src.{table};";
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add((r.GetInt64(0), r.GetInt64(1)));
        }

        var linked = 0;
        foreach (var (assetId, fkId) in rows)
        {
            if (!assetMap.TryGetValue(assetId, out var newAsset)) continue;
            if (!fkMap.TryGetValue(fkId, out var newFk)) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT OR IGNORE INTO main.{table}(asset_id, {fkColumn}, {stamp}) VALUES($a,$f,$c);";
            cmd.Parameters.AddWithValue("$a", newAsset);
            cmd.Parameters.AddWithValue("$f", newFk);
            cmd.Parameters.AddWithValue("$c", AssetService.Now());
            if (cmd.ExecuteNonQuery() > 0) linked++;
        }
        return linked;
    }

    /// <summary>relations：两端重映射；同向同类型已存在 → 跳过。返回新增数。</summary>
    private int MergeRelations(SqliteConnection conn, SqliteTransaction tx, HashSet<string> have,
                               Dictionary<long, long> assetMap, IProgress<string>? progress)
    {
        if (!have.Contains("relations")) return 0;
        progress?.Report("正在合并关联…");

        var rows = new List<(long Src, long Dst, string Type)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT source_asset_id, target_asset_id, relation_type FROM src.relations;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add((r.GetInt64(0), r.GetInt64(1), r.GetString(2)));
        }

        var added = 0;
        foreach (var (s, t, type) in rows)
        {
            if (!assetMap.TryGetValue(s, out var ns) || !assetMap.TryGetValue(t, out var nt)) continue;
            if (ns == nt) continue;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT COUNT(1) FROM main.relations WHERE source_asset_id=$s AND target_asset_id=$t AND relation_type=$r;";
                cmd.Parameters.AddWithValue("$s", ns);
                cmd.Parameters.AddWithValue("$t", nt);
                cmd.Parameters.AddWithValue("$r", type);
                if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) continue;
            }
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO main.relations(source_asset_id,target_asset_id,relation_type,metadata_json,created_at) " +
                "VALUES($s,$t,$r,'{}',$c);";
            ins.Parameters.AddWithValue("$s", ns);
            ins.Parameters.AddWithValue("$t", nt);
            ins.Parameters.AddWithValue("$r", type);
            ins.Parameters.AddWithValue("$c", AssetService.Now());
            ins.ExecuteNonQuery();
            added++;
        }
        return added;
    }

    // ==================== 内部：小工具 ====================

    private static HashSet<string> TableSet(SqliteConnection conn, string schema)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM {schema}.sqlite_master WHERE type='table';";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    private static int Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>根路径规范化（去尾部分隔符；与 <see cref="AssetService"/> 私有版同口径）。</summary>
    private static string Normalize(string rootPath)
    {
        var p = (rootPath ?? "").Trim();
        if (p.Length > 3) p = p.TrimEnd('\\', '/');
        return p;
    }

    private static void TryDetach(SqliteConnection? conn)
    {
        if (conn == null) return;
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DETACH DATABASE src;";
            cmd.ExecuteNonQuery();
        }
        catch { /* 连接马上释放，DETACH 失败无所谓 */ }
    }

}
