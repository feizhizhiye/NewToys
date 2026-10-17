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

/// <summary>合并前的预览（源库规模 + 缺哪些表）。</summary>
public sealed class MergePreview
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public int Storages { get; init; }
    public int Assets { get; init; }
    public int Locations { get; init; }
    public int Tags { get; init; }
    public int Categories { get; init; }
    public int Collections { get; init; }
    public List<string> MissingTables { get; init; } = new();

    /// <summary>是不是**媒体资产库**（至少有 assets 表）。⛔ 没有 ⇒ 拒绝合并并明示
    /// （2026-10-16：「导入之前的 NewToys.db 崩溃」—— 旧版 NewToys 应用库没有媒体资产表）。</summary>
    public bool HasAssetTables => !MissingTables.Contains("assets");

    public string SummaryText =>
        $"资产 {Assets} 个 / 文件位置 {Locations} 个 / 标签 {Tags} / 分类 {Categories} / 集合 {Collections} / 存储源 {Storages}";
}

/// <summary>合并结果（回报计数）。</summary>
public sealed class MergeResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public int NewAssets { get; init; }
    public int NewLocations { get; init; }
    public int Skipped { get; init; }
    public int SkippedAssets { get; init; }
}

/// <summary>
/// 多库融合（NT-026）：把另一个 <c>newtoys.db</c> **合并进主库**（工作副本 = 主库，见 NT-026 未决 1 裁决）。
/// <para>
/// 🔴 实现要点：
/// ① <b>源库只读</b>——<c>ATTACH</c> 挂载后只 <c>SELECT</c>，⛔ 绝不写 src.*（源文件一个字节都不改）；
/// ② <b>ATTACH 必须在事务外</b>（SQLite 不允许事务中 ATTACH）→ 顺序 = ATTACH → BEGIN → 合并 → COMMIT → DETACH；
/// ③ <b>一个事务</b>：全部成功或全部不写（⛔ 不写一半）；
/// ④ 幂等：重复并入同一个库第二次不产生孤儿资产——零文件位置的重复资产（含其标签 / 分类 / 集合挂接 / 关联）在事务内一并清除；位置按 storage+relative_path / hash+size 去重。
/// </para>
/// </summary>
public class AssetMergeService
{
    /// <summary>合并涉及的表（缺表 → 跳过该表并记账；老库可能没有新表）。</summary>
    private static readonly string[] ExpectedTables =
    {
        "storages", "assets", "file_locations", "tags", "asset_tags",
        "categories", "asset_categories", "collections", "collection_assets", "relations"
    };

    private readonly AssetDatabase _db;
    private readonly AssetService _assets;

    public AssetMergeService(AssetDatabase db, AssetService assets)
    {
        _db = db;
        _assets = assets;
    }

    public bool IsAvailable => _db.IsAvailable;

    // ==================== 预览（只读打开源库数行数） ====================

    /// <summary>预览源库：规模 + 缺表。⛔ 不写任何东西（单独只读连接打开）。</summary>
    public MergePreview Preview(string dbPath)
    {
        if (!IsAvailable) return new MergePreview { Ok = false, Message = "主资产库不可用（数据库打不开）。" };
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            return new MergePreview { Ok = false, Message = "文件不存在。" };

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly
            };
            using var conn = new SqliteConnection(builder.ToString());
            conn.Open();

            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                using var r = cmd.ExecuteReader();
                while (r.Read()) have.Add(r.GetString(0));
            }

            var missing = ExpectedTables.Where(t => !have.Contains(t)).ToList();
            int Count(string table) => have.Contains(table) ? Scalar(conn, $"SELECT COUNT(*) FROM {table};") : 0;

            return new MergePreview
            {
                Ok = true,
                Storages = Count("storages"),
                Assets = Count("assets"),
                Locations = Count("file_locations"),
                Tags = Count("tags"),
                Categories = Count("categories"),
                Collections = Count("collections"),
                MissingTables = missing
            };
        }
        catch (Exception ex)
        {
            return new MergePreview { Ok = false, Message = "打不开这个库：" + ex.Message };
        }
    }

    // ==================== 只读导出（导入预览：⛔ 不写任何库） ====================

    /// <summary>
    /// 把另一个 newtoys.db 的资产**读成预览行**（⛔ 不写主库、⛔ 不改源库）——
    /// 2026-10-16 用户定稿：「📥 导入」= 把 db/json 加载到列表显示（草稿态），
    /// 写入与否由用户点「💾 加入库」决定。独立只读连接打开（⛔ 不 ATTACH 主库连接）。
    /// </summary>
    public (bool Ok, string Message, List<AssetRow> Rows) ReadAssets(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            return (false, "文件不存在。", new List<AssetRow>());

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly
            };
            using var conn = new SqliteConnection(builder.ToString());
            conn.Open();

            var have = TableSet(conn, "main");   // 独立连接（⛔ 无 ATTACH）⇒ 主库 schema = main
            if (!have.Contains("assets"))
                return (false, $"「{System.IO.Path.GetFileName(dbPath)}」不是媒体资产库（找不到 assets 表）。", new List<AssetRow>());

            var rows = new List<AssetRow>();
            using (var cmd = conn.CreateCommand())
            {
                // 与库态 QueryPage 同构：主文件位置 + 存储源名字（缺表 ⇒ 整段跳过）
                cmd.CommandText =
                    "SELECT a.id, a.type, a.title, a.display_name, a.status, a.rating, a.description, " +
                    "       IFNULL(l.file_name,''), IFNULL(l.path,''), IFNULL(l.size,0), l.mtime, " +
                    "       IFNULL(l.status,'active'), IFNULL(s.name,''), a.updated_at, IFNULL(a.is_favorite,0) " +
                    "FROM assets a " +
                    "LEFT JOIN file_locations l ON l.id = (SELECT f.id FROM file_locations f " +
                    "    WHERE f.asset_id = a.id ORDER BY f.is_primary DESC, f.id LIMIT 1) " +
                    "LEFT JOIN storages s ON s.id = l.storage_id " +
                    "WHERE a.status <> 'trashed' ORDER BY a.id;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                var row = new AssetRow
                {
                    Id = r.GetInt64(0),   // 暂存源库 id（组织名字映射用；出方法前统一清零 = 草稿态）
                        Type = r.IsDBNull(1) ? AssetTypes.Other : r.GetString(1),
                        Title = r.IsDBNull(2) ? null : r.GetString(2),
                        DisplayName = r.IsDBNull(3) ? null : r.GetString(3),
                        Status = r.IsDBNull(4) ? AssetStatuses.Active : r.GetString(4),
                        Rating = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                        Description = r.IsDBNull(6) ? null : r.GetString(6),
                        FileName = r.GetString(7),
                        FilePath = r.GetString(8),
                        Size = r.GetInt64(9),
                        Mtime = r.IsDBNull(10) ? null : r.GetString(10),
                        LocationStatus = r.IsDBNull(11) ? LocationStatuses.Active : r.GetString(11),
                        StorageName = r.GetString(12),
                        UpdatedAt = r.IsDBNull(13) ? null : r.GetString(13),
                        IsFavorite = !r.IsDBNull(14) && r.GetInt32(14) != 0,
                        Name = !r.IsDBNull(3) && r.GetString(3).Length > 0 ? r.GetString(3)
                            : !r.IsDBNull(2) && r.GetString(2).Length > 0 ? r.GetString(2)
                            : "资产 #" + r.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    };
                    rows.Add(row);
                }
            }

            // 组织信息：按原名拼（逗号 + 空格，与库态 TagsText 同格式）
            FillOrgText(conn, have, rows, "asset_tags", "tags", "tag_id", (r, v) => r.TagsText = v);
            FillOrgText(conn, have, rows, "asset_categories", "categories", "category_id", (r, v) => r.CategoriesText = v);
            FillOrgText(conn, have, rows, "collection_assets", "collections", "collection_id", (r, v) => r.CollectionsText = v);

            foreach (var row in rows) row.Id = 0;   // 草稿态：清零源库 id（入库时统一分配）

            if (rows.Count == 0)
                return (true, "这个库里没有资产（或全是回收站条目）。", rows);
            return (true, $"已读出 {rows.Count} 条。", rows);
        }
        catch (Exception ex)
        {
            return (false, "读不了这个库：" + ex.Message, new List<AssetRow>());
        }
    }

    /// <summary>按链接表拼组织名字到行上（⛔ 表 / 列名全是固定映射，⛔ 不接受外部输入）。行的 Id 此刻仍是源库 id。</summary>
    private static void FillOrgText(SqliteConnection conn, HashSet<string> have, List<AssetRow> rows,
        string linkTable, string nameTable, string fk, Action<AssetRow, string> set)
    {
        if (rows.Count == 0 || !have.Contains(linkTable) || !have.Contains(nameTable)) return;

        // 源库资产 id → 名字串（行的 Id 暂存着源库 id）
        var names = new Dictionary<long, string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT l.asset_id, n.name FROM {linkTable} l JOIN {nameTable} n ON n.id = l.{fk} ORDER BY n.name;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                var name = r.GetString(1);
                names[id] = names.TryGetValue(id, out var old) ? old + ", " + name : name;
            }
        }

        foreach (var row in rows)
            if (names.TryGetValue(row.Id, out var text)) set(row, text);
    }

    // ==================== 合并 ====================

    /// <summary>
    /// 把源库合并进主库（一个事务；源库只读）。
    /// ⚠️ 必须在没有活动事务的连接上先 ATTACH —— 本方法自开连接，⛔ 别在事务里调。
    /// </summary>
    public MergeResult Merge(string dbPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!IsAvailable) return new MergeResult { Ok = false, Message = "主资产库不可用（数据库打不开）。" };
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            return new MergeResult { Ok = false, Message = "文件不存在。" };
        if (string.Equals(Path.GetFullPath(dbPath), Path.GetFullPath(_assets.DatabasePath), StringComparison.OrdinalIgnoreCase))
            return new MergeResult { Ok = false, Message = "这就是当前主库自己——选另一个 .db。" };

        var jobId = 0L;
        SqliteConnection? conn = null;
        try
        {
            jobId = _assets.CreateScanJob(null, "merge");
            conn = _db.Db.Open();

            // ① ATTACH（⛔ 事务外）
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "ATTACH DATABASE $p AS src;";
                cmd.Parameters.AddWithValue("$p", Path.GetFullPath(dbPath));
                cmd.ExecuteNonQuery();
            }

            var have = TableSet(conn, "src");
            var missing = ExpectedTables.Where(t => !have.Contains(t)).ToList();
            if (missing.Count > 0)
                progress?.Report($"源库缺 {missing.Count} 张表（{string.Join("、", missing)}）——按老库兼容处理，缺的跳过。");

            // ② 事务
            using var tx = conn.BeginTransaction();

            // storages：按 root_path 去重（已存在 → 用现有的 id）
            var storageMap = MergeStorages(conn, tx, have, progress);

            // assets：一律新建 + 建 src_id → new_id 映射
            var assetMap = MergeAssets(conn, tx, have, progress, ct, out var newAssets);

            // file_locations：asset_id 重映射 + 去重（storage+relative_path / hash+size）
            var (newLocations, skipped, locatedAssetIds) = MergeLocations(conn, tx, have, storageMap, assetMap, progress, ct);

            // tags / categories / collections：按名字去重
            var tagMap = MergeByName(conn, tx, have, "tags", "namespace", "name", progress);
            var catMap = MergeByName(conn, tx, have, "categories", null, "name", progress);
            var colMap = MergeByName(conn, tx, have, "collections", null, "name", progress);
            FixCategoryParents(conn, tx, have, catMap, progress);

            // 链接表：两端 id 都换新的，主键冲突忽略
            var linked = MergeLinkTable(conn, tx, have, "asset_tags", "tag_id", tagMap, assetMap, progress)
                       + MergeLinkTable(conn, tx, have, "asset_categories", "category_id", catMap, assetMap, progress)
                       + MergeLinkTable(conn, tx, have, "collection_assets", "collection_id", colMap, assetMap, progress);

            // relations：两端重映射（同向同类型已存在 → 跳过）
            var rels = MergeRelations(conn, tx, have, assetMap, progress);

            // ② 修正（NT-033）：本次并入、却没有任何文件位置落到主库（重复并入同一库 / 与现有库共享文件）的资产是孤儿，
            //    连同其标签 / 分类 / 集合挂接 / 关联一并清除，避免主库堆积零位置资产、计数虚高。
            SweepOrphanAssets(conn, tx, assetMap, locatedAssetIds, out var sweptAssets, progress);
            newAssets = locatedAssetIds.Count;

            tx.Commit();

            // ③ DETACH（⛔ 事务外）。用 TryDetach：分离失败不该把"已提交"报成失败。
            TryDetach(conn);

            var job = new ScanJob
            {
                Id = jobId,
                StorageId = null,
                Mode = "merge",
                Status = ScanStatuses.Success,
                TotalFiles = newAssets,
                AddedFiles = newAssets,
                UpdatedFiles = newLocations,
                MissingFiles = skipped,
                StartedAt = AssetService.Now(),
                FinishedAt = AssetService.Now()
            };
            _assets.UpdateScanJob(job);

            progress?.Report($"完成：新增资产 {newAssets} / 新增位置 {newLocations} / 跳过 {skipped} / 清除孤儿 {sweptAssets} / 组织挂接 {linked} / 关联 {rels}");
            return new MergeResult
            {
                Ok = true,
                Message = $"合并完成：新增资产 {newAssets} / 新增位置 {newLocations} / 跳过重复位置 {skipped} / 清除孤儿资产 {sweptAssets} / 组织挂接 {linked} / 关联 {rels}（源库未改动）。",
                NewAssets = newAssets,
                NewLocations = newLocations,
                Skipped = skipped,
                SkippedAssets = sweptAssets
            };
        }
        catch (Exception ex)
        {
            // 事务没提交 → 自动回滚；DETACH 失败也无所谓（连接随即释放）
            TryDetach(conn);
            if (jobId > 0)
                _assets.UpdateScanJob(new ScanJob
                {
                    Id = jobId, Mode = "merge", Status = ScanStatuses.Failed,
                    TotalFiles = 0, ErrorCount = 1, StartedAt = AssetService.Now(), FinishedAt = AssetService.Now()
                });
            return new MergeResult { Ok = false, Message = "合并失败（已回滚，主库未被写坏）：" + ex.Message };
        }
        finally
        {
            conn?.Dispose();
        }
    }

    /// <summary>💿 另存库文件（另存为）：把主库**在线备份**成一个新 .db（VACUUM INTO = 一致快照，⛔ 不锁库）。</summary>
    public (bool Ok, string Message) SaveAs(string targetPath)
    {
        if (!IsAvailable) return (false, "主资产库不可用（数据库打不开）。");
        if (string.IsNullOrWhiteSpace(targetPath)) return (false, "未指定目标文件。");

        try
        {
            var full = Path.GetFullPath(targetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            // VACUUM INTO：SQLite 官方的"在线另存"——一致、紧凑、⛔ 不需要事务
            cmd.CommandText = "VACUUM INTO $p;";
            cmd.Parameters.AddWithValue("$p", full);
            cmd.ExecuteNonQuery();

            var size = new FileInfo(full).Length;
            return (true, $"已保存到：{full}（{AssetRow.FormatSize(size)}）");
        }
        catch (Exception ex)
        {
            return (false, "保存失败：" + ex.Message + "（目标已存在？换个文件名或确认覆盖）");
        }
    }

    // ==================== 内部：各表合并 ====================

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
        var located = new HashSet<long>();   // 本次实际落到主库位置的资产 id（用于事务内清除孤儿资产，NT-033）
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
