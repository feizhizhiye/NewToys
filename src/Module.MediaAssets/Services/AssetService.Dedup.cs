using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 存储源重叠检测 + 重复资产合并。
/// <para>
/// 重复的来源：两条登记的根目录互相包含（父目录源 + 它的子目录源）时，同一物理文件会被两个源各扫一遍；
/// 扫描匹配只在**当前存储源的索引**内做（⛔ 跨源），于是同一文件落成两条资产、两条位置。
/// 本类只做两件补救：登记时拒掉重叠的源（<see cref="FindOverlappingStorage"/>）、
/// 把已经落成多条资产的同一路径并回一条（<see cref="MergeDuplicatePaths"/>）。
/// </para>
/// <para>⚠️ 合并只认**物理路径完全相同**（忽略大小写 / 分隔符）的位置；映射盘与 UNC 双写法指向同一份文件时路径不同，需手动移除其中一个源。</para>
/// </summary>
public partial class AssetService
{
    /// <summary>路径比较键：统一分隔符、去尾部反斜杠、忽略大小写（Windows 语义）。盘根 <c>C:\</c> 保留尾分隔符。</summary>
    public static string PathKey(string? path)
    {
        var p = (path ?? "").Trim().Replace('/', '\\');
        if (p.Length > 3) p = p.TrimEnd('\\');
        return p.ToLowerInvariant();
    }

    /// <summary><paramref name="key"/> 是否严格在 <paramref name="root"/> 之下（两侧都须是 <see cref="PathKey"/> 的结果）。</summary>
    private static bool IsUnderKey(string key, string root)
    {
        if (root.Length == 0 || key.Length <= root.Length) return false;
        var prefix = root.EndsWith("\\", StringComparison.Ordinal) ? root : root + "\\";
        return key.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>原始路径版的「是否在其之下」（严格子路径，不含相等）。</summary>
    public static bool IsUnderPath(string path, string root) => IsUnderKey(PathKey(path), PathKey(root));

    /// <summary>一条路径与已登记存储源的重叠关系。<paramref name="NewIsInside"/> = true：新路径在对方根目录**之下**。</summary>
    public sealed record StorageOverlap(StorageSource Other, bool NewIsInside);

    /// <summary>
    /// 查新路径是否与已有存储源互为父子（重叠 ⇒ 同一批文件会被重复入库）。
    /// <paramref name="excludeId"/> 用于改名自己时跳过自身。
    /// </summary>
    public StorageOverlap? FindOverlappingStorage(string rootPath, long excludeId = 0)
    {
        var key = PathKey(rootPath);
        if (key.Length == 0) return null;

        foreach (var s in ListStorages())
        {
            if (s.Id == excludeId) continue;
            var other = PathKey(s.RootPath);
            if (other.Length == 0 || other == key) continue;      // 完全相同的根由 FindStorageByRoot 负责
            if (IsUnderKey(key, other)) return new StorageOverlap(s, true);
            if (IsUnderKey(other, key)) return new StorageOverlap(s, false);
        }
        return null;
    }

    /// <summary>
    /// 在给定清单里找**覆盖**该路径的存储源（根目录是它的祖先，取最长匹配 = 最贴近的那个）；没有 → null。
    /// <para>登记新目录前先问它：落在已有源里面就复用那个源，⛔ 别再为子目录单独建一条源。</para>
    /// </summary>
    public static StorageSource? FindCoveringStorage(IEnumerable<StorageSource> storages, string path)
    {
        var key = PathKey(path);
        if (key.Length == 0) return null;

        StorageSource? best = null;
        var bestLen = -1;
        foreach (var s in storages)
        {
            var root = PathKey(s.RootPath);
            if (root.Length == 0 || root.Length <= bestLen) continue;
            if (key != root && !IsUnderKey(key, root)) continue;
            best = s;
            bestLen = root.Length;
        }
        return best;
    }

    /// <summary>同上（自己读一次存储源清单）。</summary>
    public StorageSource? FindCoveringStorage(string path) => FindCoveringStorage(ListStorages(), path);

    /// <summary>合并结果（页面状态栏用）。</summary>
    public sealed record DuplicateMergeResult(int Groups, int RemovedAssets, int MovedLocations, int PrunedLocations = 0)
    {
        public bool IsEmpty => Groups == 0 && RemovedAssets == 0 && PrunedLocations == 0;
    }

    /// <summary>有多少组「同一物理路径挂在多条资产下」（合并前的确认提示用）。</summary>
    public int CountDuplicatePaths()
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM (" +
                "  SELECT lower(path) FROM file_locations GROUP BY lower(path) HAVING COUNT(DISTINCT asset_id) > 1" +
                ");";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    /// <summary>
    /// 合并「同一物理路径挂在多条资产下」的重复：每组保留一条资产（active 优先、id 最小），
    /// 其余资产的文件位置与标签 / 分类 / 集合 / 关联全部重挂过去后删除，🔴 **磁盘文件一个都不动**。
    /// <para>回收站里的资产不参与合并（否则会把回收站内容并回在用资产）。</para>
    /// </summary>
    public DuplicateMergeResult MergeDuplicatePaths()
    {
        if (!IsAvailable) return new DuplicateMergeResult(0, 0, 0);
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();

            var groups = LoadDuplicateGroups(conn, tx);
            var merged = 0;
            var removed = 0;
            var moved = 0;

            foreach (var group in groups)
            {
                var keep = group[0];
                merged++;
                for (var i = 1; i < group.Count; i++)
                {
                    removed++;
                    moved += MergeOne(conn, tx, keep, group[i]);
                }
            }

            var pruned = PruneDuplicateLocations(conn, tx);
            tx.Commit();
            return new DuplicateMergeResult(merged, removed, moved, pruned);
        }
        catch { return new DuplicateMergeResult(0, 0, 0); }
    }

    /// <summary>
    /// 删掉「同一资产 + 同一路径」的冗余位置（合并后常见：一个资产挂着两条指向同一文件的记录）。
    /// 每个 (asset, path) 只留一条：主位置优先、其次 id 最小。返回删除条数。
    /// </summary>
    private static int PruneDuplicateLocations(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "DELETE FROM file_locations WHERE id NOT IN (" +
            "  SELECT id FROM (" +
            "    SELECT id, ROW_NUMBER() OVER (PARTITION BY asset_id, lower(path) ORDER BY is_primary DESC, id) AS rn " +
            "    FROM file_locations" +
            "  ) WHERE rn = 1);";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>读出「同一路径对应多条资产」的组，每组内按「active 优先、id 最小」排序（首元素 = 保留者）。</summary>
    private static List<List<long>> LoadDuplicateGroups(SqliteConnection conn, SqliteTransaction tx)
    {
        var byPath = new Dictionary<string, List<(long Id, string Status)>>(StringComparer.Ordinal);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT lower(l.path), l.asset_id, a.status " +
                "FROM file_locations l JOIN assets a ON a.id = l.asset_id " +
                "WHERE lower(l.path) IN (" +
                "  SELECT lower(path) FROM file_locations GROUP BY lower(path) HAVING COUNT(DISTINCT asset_id) > 1" +
                ") ORDER BY lower(l.path), l.asset_id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                var id = r.GetInt64(1);
                var status = r.IsDBNull(2) ? "" : r.GetString(2);
                if (!byPath.TryGetValue(key, out var list)) byPath[key] = list = new();
                if (list.All(x => x.Id != id)) list.Add((id, status));
            }
        }

        var groups = new List<List<long>>();
        foreach (var list in byPath.Values)
        {
            if (list.Count < 2) continue;
            if (list.Any(x => string.Equals(x.Status, AssetStatuses.Trashed, StringComparison.Ordinal))) continue;
            groups.Add(list
                .OrderBy(x => string.Equals(x.Status, AssetStatuses.Active, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToList());
        }
        return groups;
    }

    /// <summary>把 <paramref name="oldAsset"/> 的挂接与位置全部并进 <paramref name="keep"/>，再删掉旧资产。返回重挂的位置数。</summary>
    private static int MergeOne(SqliteConnection conn, SqliteTransaction tx, long keep, long oldAsset)
    {
        // 带主键的挂接表：先 UPDATE OR IGNORE（重复的直接留着），再把残留的旧行删掉
        Exec(conn, tx, "UPDATE OR IGNORE asset_tags SET asset_id=$k WHERE asset_id=$o;", ("$k", keep), ("$o", oldAsset));
        Exec(conn, tx, "DELETE FROM asset_tags WHERE asset_id=$o;", ("$o", oldAsset));
        Exec(conn, tx, "UPDATE OR IGNORE asset_categories SET asset_id=$k WHERE asset_id=$o;", ("$k", keep), ("$o", oldAsset));
        Exec(conn, tx, "DELETE FROM asset_categories WHERE asset_id=$o;", ("$o", oldAsset));
        Exec(conn, tx, "UPDATE OR IGNORE collection_assets SET asset_id=$k WHERE asset_id=$o;", ("$k", keep), ("$o", oldAsset));
        Exec(conn, tx, "DELETE FROM collection_assets WHERE asset_id=$o;", ("$o", oldAsset));

        // 关联表没有唯一约束 → 直接改指；改完两端的自环（keep↔keep）删掉
        Exec(conn, tx, "UPDATE relations SET source_asset_id=$k WHERE source_asset_id=$o;", ("$k", keep), ("$o", oldAsset));
        Exec(conn, tx, "UPDATE relations SET target_asset_id=$k WHERE target_asset_id=$o;", ("$k", keep), ("$o", oldAsset));
        Exec(conn, tx, "DELETE FROM relations WHERE source_asset_id=$k AND target_asset_id=$k;", ("$k", keep));

        // 位置重挂：⛔ 不抢主位置（is_primary 归零），"主位置"仍由保留资产原来那条担任
        var moved = Exec(conn, tx,
            "UPDATE file_locations SET asset_id=$k, is_primary=0, updated_at=$u WHERE asset_id=$o;",
            ("$k", keep), ("$o", oldAsset), ("$u", Now()));

        Exec(conn, tx, "DELETE FROM assets WHERE id=$o;", ("$o", oldAsset));
        return moved;
    }

    private static int Exec(SqliteConnection conn, SqliteTransaction tx, string sql,
                            params (string Name, object Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteNonQuery();
    }

    // ==================== 扫描用：重叠源的既有位置 ====================

    /// <summary>
    /// 把**登记在当前存储源之下**的其它源（子目录源）的位置读成「完整路径 → 位置」表：
    /// 父源扫描时先认领这些位置，⛔ 不再为同一个物理文件新建资产。
    /// <para>没有此类源时返回空表 —— 常规库零额外开销（不会多读一行）。</para>
    /// <para>⛔ 只取子源、不取父源：父源临时离线时，子源扫描不该把它下面的位置抢走。</para>
    /// </summary>
    public Dictionary<string, FileLocation> LoadOverlappingLocations(StorageSource storage)
    {
        var map = new Dictionary<string, FileLocation>(StringComparer.Ordinal);
        if (!IsAvailable) return map;

        var root = PathKey(storage.RootPath);
        if (root.Length == 0) return map;

        var childIds = new List<long>();
        foreach (var s in ListStorages())
        {
            if (s.Id == storage.Id) continue;
            var other = PathKey(s.RootPath);
            if (IsUnderKey(other, root)) childIds.Add(s.Id);
        }
        if (childIds.Count == 0) return map;

        foreach (var sid in childIds)
            foreach (var l in ListStorageLocations(sid))
                map[PathKey(l.Path)] = l;

        return map;
    }
}
