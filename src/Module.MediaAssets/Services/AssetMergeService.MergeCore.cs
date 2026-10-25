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

    /// <summary>storages：按 root_path 去重，返回 src_id → main_id 映射。</summary>
    private Dictionary<long, long> MergeStorages(SqliteConnection conn, SqliteTransaction tx, HashSet<string> have,
                                                 IProgress<string>? progress)
    {
        var map = new Dictionary<long, long>();
        if (!have.Contains("storages")) return map;
        progress?.Report("正在合并存储源…");

        // 主库现有的（root_path 规范化后比较，与 AssetService.NormalizeRoot 同口径：去尾部分隔符）
        var mainByRoot = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, root_path FROM main.storages;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) mainByRoot[Normalize(r.GetString(1))] = r.GetInt64(0);
        }

        var srcRows = new List<(long Id, string Name, string Root, string Type, bool Online, string Created, string Updated)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id,name,root_path,type,online,created_at,updated_at FROM src.storages;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                srcRows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0,
                    r.IsDBNull(5) ? "" : r.GetString(5), r.IsDBNull(6) ? "" : r.GetString(6)));
        }

        foreach (var s in srcRows)
        {
            var key = Normalize(s.Root);
            if (mainByRoot.TryGetValue(key, out var existing))
            {
                map[s.Id] = existing;
                continue;
            }
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO main.storages(name,root_path,type,online,created_at,updated_at) " +
                "VALUES($n,$p,$t,$o,$c,$u); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$n", s.Name);
            cmd.Parameters.AddWithValue("$p", s.Root);
            cmd.Parameters.AddWithValue("$t", s.Type);
            cmd.Parameters.AddWithValue("$o", s.Online ? 1 : 0);
            cmd.Parameters.AddWithValue("$c", string.IsNullOrEmpty(s.Created) ? AssetService.Now() : s.Created);
            cmd.Parameters.AddWithValue("$u", AssetService.Now());
            var id = (long)cmd.ExecuteScalar()!;
            map[s.Id] = id;
            mainByRoot[key] = id;
        }
        return map;
    }

    /// <summary>assets：一律新建，返回 src_id → new_id 映射。</summary>
    private Dictionary<long, long> MergeAssets(SqliteConnection conn, SqliteTransaction tx, HashSet<string> have,
                                               IProgress<string>? progress, CancellationToken ct, out int newAssets)
    {
        var map = new Dictionary<long, long>();
        newAssets = 0;
        if (!have.Contains("assets")) return map;
        progress?.Report("正在合并资产…");

        var srcRows = new List<(long Id, string Type, string? Title, string? Display, string? Desc,
            string Status, int Rating, string Meta, string Created, string Updated)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT id,type,title,display_name,description,status,rating,metadata_json,created_at,updated_at FROM src.assets;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                srcRows.Add((r.GetInt64(0), r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5),
                    r.IsDBNull(6) ? 0 : r.GetInt32(6), r.IsDBNull(7) ? "{}" : r.GetString(7),
                    r.GetString(8), r.GetString(9)));
        }

        var done = 0;
        foreach (var a in srcRows)
        {
            ct.ThrowIfCancellationRequested();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO main.assets(type,title,display_name,description,status,rating,metadata_json,created_at,updated_at) " +
                "VALUES($t,$ti,$d,$de,$s,$r,$m,$c,$u); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$t", a.Type);
            cmd.Parameters.AddWithValue("$ti", (object?)a.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$d", (object?)a.Display ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$de", (object?)a.Desc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$s", a.Status);
            cmd.Parameters.AddWithValue("$r", a.Rating);
            cmd.Parameters.AddWithValue("$m", a.Meta);
            cmd.Parameters.AddWithValue("$c", string.IsNullOrEmpty(a.Created) ? AssetService.Now() : a.Created);
            cmd.Parameters.AddWithValue("$u", AssetService.Now());
            var id = (long)cmd.ExecuteScalar()!;
            map[a.Id] = id;
            newAssets++;
            if (++done % 500 == 0) progress?.Report($"正在合并资产… {done}/{srcRows.Count}");
        }
        return map;
    }

    /// <summary>file_locations：asset_id 重映射 + 去重（storage+relative_path 或 hash+size 已存在 → 跳过）。</summary>
    private (int NewLocations, int Skipped, HashSet<long> LocatedAssetIds) MergeLocations(SqliteConnection conn, SqliteTransaction tx, HashSet<string> have,
                                                           Dictionary<long, long> storageMap, Dictionary<long, long> assetMap,
                                                           IProgress<string>? progress, CancellationToken ct)
    {
        if (!have.Contains("file_locations")) return (0, 0, new HashSet<long>());
        progress?.Report("正在合并文件位置…");

        // 主库已有的去重键
        var byRel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byHash = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT IFNULL(storage_id,0), IFNULL(relative_path,''), IFNULL(hash,''), IFNULL(size,0) FROM main.file_locations;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                byRel.Add($"{r.GetInt64(0)}|{r.GetString(1)}");
                var hash = r.GetString(2);
                if (hash.Length > 0) byHash.Add($"{hash}|{r.GetInt64(3)}");
            }
        }

        var srcRows = new List<FileLocation>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT asset_id,storage_id,path,relative_path,file_name,ext,mime_type,size,mtime,hash,quick_hash," +
                "inode,device,is_primary,status,last_seen_at,created_at FROM src.file_locations;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                srcRows.Add(new FileLocation
                {
                    AssetId = r.GetInt64(0),
                    StorageId = r.GetInt64(1),
                    Path = r.GetString(2),
                    RelativePath = r.IsDBNull(3) ? null : r.GetString(3),
                    FileName = r.GetString(4),
                    Ext = r.IsDBNull(5) ? null : r.GetString(5),
                    MimeType = r.IsDBNull(6) ? null : r.GetString(6),
                    Size = r.IsDBNull(7) ? 0 : r.GetInt64(7),
                    Mtime = r.IsDBNull(8) ? null : r.GetString(8),
                    Hash = r.IsDBNull(9) ? null : r.GetString(9),
                    QuickHash = r.IsDBNull(10) ? null : r.GetString(10),
                    Inode = r.IsDBNull(11) ? null : r.GetString(11),
                    Device = r.IsDBNull(12) ? null : r.GetString(12),
                    IsPrimary = !r.IsDBNull(13) && r.GetInt64(13) != 0,
                    Status = r.IsDBNull(14) ? LocationStatuses.Active : r.GetString(14),
                    LastSeenAt = r.IsDBNull(15) ? null : r.GetString(15),
                    CreatedAt = r.IsDBNull(16) ? "" : r.GetString(16)
                });
        }

        var added = 0;
        var skipped = 0;
        var located = new HashSet<long>();   // 本次实际落到主库位置的资产 id（用于事务内清除孤儿资产，）
        foreach (var l in srcRows)
        {
            ct.ThrowIfCancellationRequested();

            // 资产没并进来（理论上不会——assets 先合并）→ 跳过
            if (!assetMap.TryGetValue(l.AssetId, out var newAssetId)) { skipped++; continue; }
            if (!storageMap.TryGetValue(l.StorageId, out var newStorageId)) { skipped++; continue; }

            var relKey = $"{newStorageId}|{l.RelativePath ?? ""}";
            if (byRel.Contains(relKey)) { skipped++; continue; }
            if (!string.IsNullOrEmpty(l.Hash) && byHash.Contains($"{l.Hash}|{l.Size}")) { skipped++; continue; }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO main.file_locations(asset_id,storage_id,path,relative_path,file_name,ext,mime_type,size,mtime," +
                "hash,quick_hash,inode,device,is_primary,status,last_seen_at,created_at,updated_at) " +
                "VALUES($aid,$sid,$path,$rel,$fn,$ext,$mime,$size,$mtime,$hash,$qhash,$inode,$dev,$prim,$st,$seen,$c,$u);";
            cmd.Parameters.AddWithValue("$aid", newAssetId);
            cmd.Parameters.AddWithValue("$sid", newStorageId);
            cmd.Parameters.AddWithValue("$path", l.Path);
            cmd.Parameters.AddWithValue("$rel", (object?)l.RelativePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$fn", l.FileName);
            cmd.Parameters.AddWithValue("$ext", (object?)l.Ext ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mime", (object?)l.MimeType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$size", l.Size);
            cmd.Parameters.AddWithValue("$mtime", (object?)l.Mtime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hash", (object?)l.Hash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qhash", (object?)l.QuickHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$inode", (object?)l.Inode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dev", (object?)l.Device ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$prim", l.IsPrimary ? 1 : 0);
            cmd.Parameters.AddWithValue("$st", l.Status);
            cmd.Parameters.AddWithValue("$seen", (object?)l.LastSeenAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$c", string.IsNullOrEmpty(l.CreatedAt) ? AssetService.Now() : l.CreatedAt);
            cmd.Parameters.AddWithValue("$u", AssetService.Now());
            cmd.ExecuteNonQuery();

            byRel.Add(relKey);
            if (!string.IsNullOrEmpty(l.Hash)) byHash.Add($"{l.Hash}|{l.Size}");
            located.Add(newAssetId);
            added++;
        }
        return (added, skipped, located);
    }

    /// <summary>
    /// 清除本次合并产生的孤儿资产（零文件位置）：删资产 + 其标签 / 分类 / 集合挂接 / 关联。
    /// <para>只删 <paramref name="assetMap"/> 中"本次新建却没落到任何位置"的资产 id（⛔ 不动合并前已存在的主库资产）。</para>
    /// </summary>
    private static void SweepOrphanAssets(SqliteConnection conn, SqliteTransaction tx,
                                          Dictionary<long, long> assetMap, HashSet<long> located,
                                          out int swept, IProgress<string>? progress)
    {
        swept = 0;
        if (assetMap.Count == 0) return;
        var orphans = assetMap.Values.Where(id => !located.Contains(id)).ToList();
        if (orphans.Count == 0) return;

        // id 全为 long（本次合并新建的主键），非任何外部输入 → IN 串拼接无注入风险
        var ids = string.Join(",", orphans);
        void Exec(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        Exec($"DELETE FROM main.asset_tags WHERE asset_id IN ({ids});");
        Exec($"DELETE FROM main.asset_categories WHERE asset_id IN ({ids});");
        Exec($"DELETE FROM main.collection_assets WHERE asset_id IN ({ids});");
        Exec($"DELETE FROM main.relations WHERE source_asset_id IN ({ids}) OR target_asset_id IN ({ids});");
        Exec($"DELETE FROM main.assets WHERE id IN ({ids});");

        swept = orphans.Count;
        progress?.Report($"清除重复并入产生的孤儿资产 {swept} 个。");
    }

}
