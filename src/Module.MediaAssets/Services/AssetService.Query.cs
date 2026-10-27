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
    /// 最近一次查询失败的原因（查询成功即清空）。界面把它拼进状态栏：
    /// <c>QueryPage</c> 这类方法会把异常吞掉并返回空集合，没有这条提示时「列表恒空」只能靠猜。
    /// </summary>
    public string LastQueryError { get; private set; } = "";

    /// <summary>
    /// 一次查询拼出列表页的行（**资产 + 主位置 + 存储源名**），再把标签 / 分类 / 集合**批量**补齐。
    /// <para>
    /// ⛔ 为什么不在 VM 里对每个资产再查位置：那是 N+1（500 行 = 500 次查询）。这里主位置用
    /// <c>LEFT JOIN (子查询取 is_primary 优先的第一条)</c> 一次带出；三类组织信息各用**一条**查询
    /// 拉回全部映射再拼，总查询数恒定 = 4。
    /// </para>
    /// </summary>
    public List<AssetRow> QueryRows(string? type = null, bool includeTrashed = false, int limit = 500)
    {
        var rows = new List<AssetRow>();
        if (!IsAvailable) return rows;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            var sql =
                "SELECT a.id, a.type, a.title, a.display_name, a.status, a.rating, a.description, " +
                "       IFNULL(l.file_name,''), IFNULL(l.path,''), IFNULL(l.size,0), l.mtime, " +
                "       IFNULL(l.status,'active'), IFNULL(l.storage_id,0), IFNULL(s.name,''), a.updated_at, a.metadata_json, " +
                "       IFNULL(a.is_favorite,0) " +
                "FROM assets a " +
                "LEFT JOIN file_locations l ON l.id = (SELECT f.id FROM file_locations f " +
                "    WHERE f.asset_id = a.id ORDER BY f.is_primary DESC, f.id LIMIT 1) " +
                "LEFT JOIN storages s ON s.id = l.storage_id " +
                "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(type)) { sql += " AND a.type=$t"; cmd.Parameters.AddWithValue("$t", type); }
            if (!includeTrashed) sql += " AND a.status <> 'trashed'";
            sql += " ORDER BY a.updated_at DESC LIMIT $lim;";
            cmd.Parameters.AddWithValue("$lim", limit);
            cmd.CommandText = sql;

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var meta = r.IsDBNull(15) ? "{}" : r.GetString(15);
                var row = new AssetRow
                {
                    Id = r.GetInt64(0),
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
                    StorageId = r.GetInt64(12),
                    StorageName = r.GetString(13),
                    UpdatedAt = r.IsDBNull(14) ? null : r.GetString(14),
                    IsFavorite = !r.IsDBNull(16) && r.GetInt32(16) != 0,
                    Name = !r.IsDBNull(3) && r.GetString(3).Length > 0 ? r.GetString(3)
                        : !r.IsDBNull(2) && r.GetString(2).Length > 0 ? r.GetString(2)
                        : "资产 #" + r.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                ApplyVideoMeta(row, meta);   // 逐行独立解析（损坏的 metadata_json 不影响整批）
                rows.Add(row);
            }
        }
        catch (Exception ex) { LastQueryError = ex.Message; return rows; }

        // 三类组织信息：各一条查询拉全量映射（行数有限，⛔ 不 per-row 查）
        var tags = LoadNameMap("asset_tags", "tags");
        var cats = LoadNameMap("asset_categories", "categories");
        var cols = LoadNameMap("collection_assets", "collections");
        foreach (var row in rows)
        {
            if (tags.TryGetValue(row.Id, out var t)) row.TagsText = t;
            if (cats.TryGetValue(row.Id, out var c)) row.CategoriesText = c;
            if (cols.TryGetValue(row.Id, out var k)) row.CollectionsText = k;
        }
        return rows;
    }

    // ==================== 分页查询（列表与计数共用同一套 WHERE） ====================

    /// <summary>
    /// 分页查询条件（列表 <see cref="QueryPage"/> 与计数 <see cref="CountAssets"/> 传同一个对象）。
    /// <para>🔴 WHERE 只准在 <see cref="BuildWhere"/> 里写一份 —— 两份漂移 = 总数与列表对不上。</para>
    /// </summary>
    /// <param name="Search">关键字（名称 / 路径 / 标签 包含，忽略大小写）。</param>
    /// <param name="Type">类型原始值（video / music / …）。</param>
    /// <param name="Status">状态原始值（active / archived / missing / trashed）。</param>
    /// <param name="StorageId">存储源 id 文本。</param>
    /// <param name="TagName">标签名（TagsText 逗号串里含这一个）。</param>
    /// <param name="Clauses">表头右键「多项筛选」的生效条件（AND）。</param>
    /// <param name="SortColumn">排序列键（与内存版口径一致；空 = 按名称）。</param>
    /// <param name="SortDescending">是否降序。</param>
    /// <param name="Offset">偏移行数（= (页码-1)×页大小）。</param>
    /// <param name="Limit">页大小。</param>
    public sealed record AssetQuery(
        string? Search = null,
        string? Type = null,
        string? Status = null,
        string? StorageId = null,
        string? TagName = null,
        IReadOnlyList<FilterClause>? Clauses = null,
        string SortColumn = "",
        bool SortDescending = false,
        int Offset = 0,
        int Limit = DefaultPageSize);

    /// <summary>SQL 侧共用的列表达式（🔴 筛选 / 排序 / 关键字共用同一份定义，⛔ 不各写一遍）。</summary>
    /// <remarks>
    /// 三级兜底必须用 <c>COALESCE</c>：SQLite 的 <c>IFNULL</c> **只收两个参数**，
    /// 写成 <c>IFNULL(a,b,c)</c> 会让整条 SELECT 抛 "wrong number of arguments"，
    /// 而 <c>QueryPage</c> 的 catch 吞掉异常 ⇒ 页码/总数正常但列表恒空。
    /// </remarks>
    private const string NameExpr = "COALESCE(NULLIF(a.display_name,''), NULLIF(a.title,''), '资产 #' || a.id)";
    // 🔴 分组聚合的三列必须走「内层排序 + 外层 group_concat」，而外层只能引用**内层的输出列名**：
    //    内层 `SELECT n.name` 的输出列叫 `name`，外层写 `group_concat(n.name, …)` 会让 SQLite 抛
    //    "no such column: n.name"（整条查询失败，被 catch 吞成 0 行 —— 表现就是「搜索随便填都搜不到」）。
    //    内层必须显式 `AS name`，外层一律用 `name`。
    private const string TagsExpr = "(SELECT group_concat(name, ', ') FROM (SELECT n.name AS name FROM asset_tags at " +
        "JOIN tags n ON n.id=at.tag_id WHERE at.asset_id=a.id ORDER BY n.name))";
    private const string CategoriesExpr = "(SELECT group_concat(name, ', ') FROM (SELECT n.name AS name FROM asset_categories ac " +
        "JOIN categories n ON n.id=ac.category_id WHERE ac.asset_id=a.id ORDER BY n.name))";
    private const string CollectionsExpr = "(SELECT group_concat(name, ', ') FROM (SELECT n.name AS name FROM collection_assets ca " +
        "JOIN collections n ON n.id=ca.collection_id WHERE ca.asset_id=a.id ORDER BY n.name))";
    // 🔴 json_extract 遇损坏 JSON 会**炸整条查询**（内存路径是逐行容错）⇒ 一律 json_valid 守卫
    private const string DurationExpr = "IFNULL(CASE WHEN json_valid(a.metadata_json) THEN " +
        "json_extract(a.metadata_json,'$.duration_seconds') END,0)";
    private const string ResolutionExpr = "CASE WHEN json_valid(a.metadata_json) AND " +
        "json_extract(a.metadata_json,'$.width') IS NOT NULL AND " +
        "json_extract(a.metadata_json,'$.height') IS NOT NULL " +
        "THEN json_extract(a.metadata_json,'$.width') || '×' || json_extract(a.metadata_json,'$.height') ELSE '' END";
    private const string CodecExpr = "IFNULL(CASE WHEN json_valid(a.metadata_json) THEN " +
        "json_extract(a.metadata_json,'$.video_codec') END,'')";

    private const int DefaultPageSize = 50;

    private const string QueryJoins =
        "LEFT JOIN file_locations l ON l.id = (SELECT f.id FROM file_locations f " +
        "    WHERE f.asset_id = a.id ORDER BY f.is_primary DESC, f.id LIMIT 1) " +
        "LEFT JOIN storages s ON s.id = l.storage_id ";

    /// <summary>
    /// 符合条件的总条数（独立 COUNT(*)，⛔ 不用「当前页条数」充当总数）。
    /// 库不可用 / 查询失败 → 0（调用方按空页处理并提示）。
    /// </summary>
    public int CountAssets(AssetQuery q)
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM assets a " + QueryJoins + BuildWhere(q, cmd) + ";";
            var v = cmd.ExecuteScalar();
            LastQueryError = "";
            return v is null || v is DBNull ? 0 : Convert.ToInt32(v);
        }
        catch (Exception ex) { LastQueryError = ex.Message; return 0; }
    }

    /// <summary>
    /// 取一页资产（ORDER BY 下推 SQL + LIMIT/OFFSET 按需取页，⛔ 不再整表入内存）。
    /// 组织信息（标签 / 分类 / 集合）只查本页 id 的映射（⛔ 不拉全表映射 —— 那是分页最贵的隐藏 N²）。
    /// </summary>
    public List<AssetRow> QueryPage(AssetQuery q)
    {
        var rows = new List<AssetRow>();
        if (!IsAvailable || q.Limit <= 0) return rows;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT a.id, a.type, a.title, a.display_name, a.status, a.rating, a.description, " +
                "       IFNULL(l.file_name,''), IFNULL(l.path,''), IFNULL(l.size,0), l.mtime, " +
                "       IFNULL(l.status,'active'), IFNULL(l.storage_id,0), IFNULL(s.name,''), a.updated_at, a.metadata_json, " +
                "       IFNULL(a.is_favorite,0) " +
                "FROM assets a " + QueryJoins + BuildWhere(q, cmd) + BuildOrderBy(q) +
                " LIMIT $lim OFFSET $off;";
            cmd.Parameters.AddWithValue("$lim", q.Limit);
            cmd.Parameters.AddWithValue("$off", System.Math.Max(0, q.Offset));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var meta = r.IsDBNull(15) ? "{}" : r.GetString(15);
                var row = new AssetRow
                {
                    Id = r.GetInt64(0),
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
                    StorageId = r.GetInt64(12),
                    StorageName = r.GetString(13),
                    UpdatedAt = r.IsDBNull(14) ? null : r.GetString(14),
                    IsFavorite = !r.IsDBNull(16) && r.GetInt32(16) != 0,
                    Name = !r.IsDBNull(3) && r.GetString(3).Length > 0 ? r.GetString(3)
                        : !r.IsDBNull(2) && r.GetString(2).Length > 0 ? r.GetString(2)
                        : "资产 #" + r.GetInt64(0).ToString(CultureInfo.InvariantCulture)
                };
                ApplyVideoMeta(row, meta);   // 逐行独立解析（损坏的 metadata_json 不影响整批）
                rows.Add(row);
            }
            LastQueryError = "";
        }
        catch (Exception ex) { LastQueryError = ex.Message; return rows; }

        // 三类组织信息：只查本页 id 的映射（行数有限，⛔ 不 per-row 查、⛔ 不拉全表）
        var ids = rows.Select(x => x.Id).ToArray();
        var tags = LoadNameMap("asset_tags", "tags", ids);
        var cats = LoadNameMap("asset_categories", "categories", ids);
        var cols = LoadNameMap("collection_assets", "collections", ids);
        foreach (var row in rows)
        {
            if (tags.TryGetValue(row.Id, out var t)) row.TagsText = t;
            if (cats.TryGetValue(row.Id, out var c)) row.CategoriesText = c;
            if (cols.TryGetValue(row.Id, out var k)) row.CollectionsText = k;
        }
        return rows;
    }

    /// <summary>
    /// WHERE 子句构造（唯一一份；列表与计数共用）。
    /// 页面级条件（类型 / 状态 / 存储源 / 标签 / 关键字）+ 表头右键的多项筛选（<see cref="FilterClause"/>）。
    /// </summary>
    private static string BuildWhere(AssetQuery q, SqliteCommand cmd)
    {
        var sql = new StringBuilder("WHERE 1=1");

        // 状态筛选优先：显式选了状态时只按该状态过滤（含「回收站」），
        // ⛔ 不能再叠加默认的「非回收站」条件 —— 两个条件同时存在时选回收站恒为 0 行。
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            sql.Append(" AND a.status=$s");
            cmd.Parameters.AddWithValue("$s", q.Status);
        }
        else
        {
            sql.Append(" AND a.status <> 'trashed'");
        }

        if (!string.IsNullOrWhiteSpace(q.Type))
        {
            sql.Append(" AND a.type=$t");
            cmd.Parameters.AddWithValue("$t", q.Type);
        }

        if (!string.IsNullOrWhiteSpace(q.StorageId))
        {
            if (long.TryParse(q.StorageId, out var sid))
            {
                sql.Append(" AND IFNULL(l.storage_id,0)=$sid");
                cmd.Parameters.AddWithValue("$sid", sid);
            }
        }

        if (!string.IsNullOrWhiteSpace(q.TagName))
        {
            sql.Append(" AND EXISTS (SELECT 1 FROM asset_tags at JOIN tags tg ON tg.id=at.tag_id " +
                       "WHERE at.asset_id=a.id AND tg.name=$tag)");
            cmd.Parameters.AddWithValue("$tag", q.TagName);
        }

        var search = (q.Search ?? "").Trim();
        if (search.Length > 0)
        {
            // 🔴 分词 AND：关键字按空白/文件名分隔符拆成多个词，每个词都要在「名称 / 路径 / 标签」任一字段里出现
            //    ⇒ 「熊出 01」也能搜到 `熊出没_01.mp4`（整串连续匹配时中间隔字就搜不到，看起来像搜索失效）。
            var tokens = SplitSearchTokens(search);
            for (var i = 0; i < tokens.Count; i++)
            {
                var p = "$qs" + i.ToString(CultureInfo.InvariantCulture);
                sql.Append(" AND (" + NameExpr + " LIKE " + p + " ESCAPE '\\' OR IFNULL(l.path,'') LIKE " + p +
                           " ESCAPE '\\' OR " + TagsExpr + " LIKE " + p + " ESCAPE '\\')");
                cmd.Parameters.AddWithValue(p, "%" + EscapeLike(tokens[i]) + "%");
            }
        }

        if (q.Clauses != null)
        {
            var n = 0;
            foreach (var c in q.Clauses)
            {
                if (!c.IsActive) continue;
                var frag = ClauseToSql(c, cmd, "c" + n.ToString(CultureInfo.InvariantCulture));
                if (frag != null) { sql.Append(" AND ").Append(frag); n++; }
            }
        }

        return sql.ToString();
    }

    /// <summary>
    /// ORDER BY 下推（⛔ 排序不准留在内存做 —— 分页后页内排序 = 每页各自排，排错序）。
    /// 文本列一律 <c>COLLATE NOCASE</c>（贴合原内存排序的大小写不敏感口径）；
    /// 恒定加 <c>a.id</c> 次级键（同值行翻页不串位）。
    /// </summary>
    private static string BuildOrderBy(AssetQuery q)
    {
        var expr = q.SortColumn switch
        {
            "Type" => "a.type COLLATE NOCASE",
            "Tags" => TagsExpr + " COLLATE NOCASE",
            "Categories" => CategoriesExpr + " COLLATE NOCASE",
            "Collections" => CollectionsExpr + " COLLATE NOCASE",
            "Size" => "IFNULL(l.size,0)",
            "Modified" => "IFNULL(l.mtime,'')",
            "Storage" => "IFNULL(s.name,'') COLLATE NOCASE",
            "Duration" => DurationExpr,
            "Resolution" => ResolutionExpr + " COLLATE NOCASE",
            "Codec" => CodecExpr + " COLLATE NOCASE",
            _ => NameExpr + " COLLATE NOCASE"   // 空 / "Name" = 按名称（与原默认一致）
        };
        return " ORDER BY " + expr + (q.SortDescending ? " DESC" : " ASC") + ", a.id ASC";
    }

    /// <summary>列键 → SQL 表达式 + 是否数值列（与 <see cref="AssetRow"/> 的展示口径对齐）。</summary>
    private static (string Expr, bool Numeric)? ColumnExpr(string key) => key switch
    {
        "Name" => (NameExpr, false),
        "Type" => ("a.type", false),
        "Status" => ("a.status", false),
        "Tags" => (TagsExpr, false),
        "Categories" => (CategoriesExpr, false),
        "Collections" => (CollectionsExpr, false),
        "Size" => ("IFNULL(l.size,0)", true),
        "Modified" => ("IFNULL(l.mtime,'')", false),
        "Storage" => ("IFNULL(s.name,'')", false),
        "Duration" => (DurationExpr, true),
        "Resolution" => (ResolutionExpr, false),
        "Codec" => (CodecExpr, false),
        _ => null   // 取不到值的列 ⇒ 条件不生效（与内存版「列键对不上 ⇒ 跳过」同口径）
    };

    /// <summary>一条 <see cref="FilterClause"/> → SQL 片段（⛔ 不再内存里补滤 —— 那会让计数与列表对不上）。</summary>
    private static string? ClauseToSql(FilterClause c, SqliteCommand cmd, string p)
    {
        var col = ColumnExpr(c.ColumnKey);
        if (col == null) return null;
        var (expr, numeric) = col.Value;
        var input = (c.Value ?? "").Trim();

        // 🔴 先给初值：`numeric && TryParse(out num)` 短路时 TryParse 不跑 ⇒ num 未赋值（CS0165）
        var num = 0.0;
        var numOk = numeric && double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out num);

        switch (c.Op)
        {
            case FilterOperators.Contains:
                cmd.Parameters.AddWithValue("$" + p, "%" + EscapeLike(input) + "%");
                return expr + " LIKE $" + p + " ESCAPE '\\'";
            case FilterOperators.NotContains:
                cmd.Parameters.AddWithValue("$" + p, "%" + EscapeLike(input) + "%");
                return "IFNULL(" + expr + ",'') NOT LIKE $" + p + " ESCAPE '\\'";
            case FilterOperators.Equal:
                if (numOk)
                {
                    cmd.Parameters.AddWithValue("$" + p, num);
                    return expr + "=$" + p;
                }
                cmd.Parameters.AddWithValue("$" + p, input);
                return expr + "=$" + p + " COLLATE NOCASE";
            case FilterOperators.NotEqual:
                if (numOk)
                {
                    cmd.Parameters.AddWithValue("$" + p, num);
                    return expr + "<>$" + p;
                }
                cmd.Parameters.AddWithValue("$" + p, input);
                return expr + "<>$" + p + " COLLATE NOCASE";
            case FilterOperators.Greater:
                if (numOk)
                {
                    cmd.Parameters.AddWithValue("$" + p, num);
                    return expr + ">$" + p;
                }
                cmd.Parameters.AddWithValue("$" + p, input);
                return expr + ">$" + p;
            case FilterOperators.Less:
                if (numOk)
                {
                    cmd.Parameters.AddWithValue("$" + p, num);
                    return expr + "<$" + p;
                }
                cmd.Parameters.AddWithValue("$" + p, input);
                return expr + "<$" + p;
            case FilterOperators.Empty:
                return "TRIM(IFNULL(" + expr + ",''))=''";
            case FilterOperators.NotEmpty:
                return "TRIM(IFNULL(" + expr + ",''))<>''";
            default:
                return null;
        }
    }

    /// <summary>LIKE 通配符转义（% _ \ 三个字符；⛔ 用户输入不当 wildcard 用）。</summary>
    private static string EscapeLike(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>关键字分词分隔符：半角/全角空白 + 常见文件名里的分隔符（`_` `-` `.` `,` `;` 及全角版）。</summary>
    private static readonly char[] SearchSeparators =
        { ' ', '\t', '\u3000', ',', '，', ';', '；', '_', '-', '.', '、' };

    /// <summary>关键字最多拆成几个词（⛔ 不设上限的话一长串空格能拼出上百个 AND 条件）。</summary>
    private const int MaxSearchTokens = 8;

    /// <summary>
    /// 关键字 → 词列表（去空、忽略大小写去重、封顶 <see cref="MaxSearchTokens"/>）。
    /// <para>🔴 SQL 与内存两条路径必须用**同一份**分词口径，否则草稿态与库态的搜索结果对不上。</para>
    /// </summary>
    public static IReadOnlyList<string> SplitSearchTokens(string? search)
    {
        var text = (search ?? "").Trim();
        if (text.Length == 0) return Array.Empty<string>();

        var list = new List<string>(MaxSearchTokens);
        foreach (var raw in text.Split(SearchSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) continue;
            if (list.Contains(raw, StringComparer.OrdinalIgnoreCase)) continue;
            list.Add(raw);
            if (list.Count >= MaxSearchTokens) break;
        }
        return list;
    }

    /// <summary>
    /// 内存版关键字匹配（草稿态 / 扫描预览用）：每个词都要在名称、路径、标签串任一里出现。
    /// <para>与 SQL 侧 <c>BuildWhere</c> 同口径（名称 / 路径 / 标签，忽略大小写）。</para>
    /// </summary>
    public static bool MatchesKeyword(AssetRow row, string? search)
    {
        var tokens = SplitSearchTokens(search);
        if (tokens.Count == 0) return true;

        foreach (var t in tokens)
        {
            if (!Hit(row.Name, t) && !Hit(row.FilePath, t) && !Hit(row.TagsText, t)) return false;
        }
        return true;

        static bool Hit(string? value, string token)
            => !string.IsNullOrEmpty(value) && value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把 <c>metadata_json</c> 里的视频元数据填进 <see cref="AssetRow"/> 的视频专属字段。
    /// <para>逐行独立解析、吞掉一切异常（JSON 损坏 / 缺键 / 类型不符都不影响列表其余行）。
    /// 键名沿用 <see cref="MetadataService"/> 的写入：<c>duration_seconds</c> / <c>width</c> / <c>height</c> / <c>video_codec</c>。</para>
    /// </summary>
    private static void ApplyVideoMeta(AssetRow row, string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("duration_seconds", out var d) && d.ValueKind == JsonValueKind.Number)
                row.DurationSeconds = d.GetDouble();
            if (root.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number
                && root.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number)
                row.ResolutionText = $"{w.GetInt32()}×{h.GetInt32()}";
            if (root.TryGetProperty("video_codec", out var c) && c.ValueKind == JsonValueKind.String)
                row.CodecText = c.GetString() ?? "";
        }
        catch { /* 元数据损坏：本行视频列留空，不拖垮整批 */ }
    }

}
