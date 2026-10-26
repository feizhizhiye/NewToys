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
/// 多库融合：把另一个资产库 <c>*.db</c> **合并进当前库**（工作副本 = 当前库；⛔ 不改源库）。
/// <para>
/// 🔴 实现要点：
/// ① <b>源库只读</b>——<c>ATTACH</c> 挂载后只 <c>SELECT</c>，⛔ 绝不写 src.*（源文件一个字节都不改）；
/// ② <b>ATTACH 必须在事务外</b>（SQLite 不允许事务中 ATTACH）→ 顺序 = ATTACH → BEGIN → 合并 → COMMIT → DETACH；
/// ③ <b>一个事务</b>：全部成功或全部不写（⛔ 不写一半）；
/// ④ 幂等：重复并入同一个库第二次不产生孤儿资产——零文件位置的重复资产（含其标签 / 分类 / 集合挂接 / 关联）在事务内一并清除；位置按 storage+relative_path / hash+size 去重。
/// </para>
/// </summary>
public partial class AssetMergeService
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
    /// 把另一个资产库的资产**读成预览行**（⛔ 不写当前库、⛔ 不改源库）——
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

}
