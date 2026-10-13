using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NewToys.Core.Services;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频数据库（目录索引，与 <see cref="VideoMediaIndex"/> 的扫描缓存**不同**）：
/// 这是用户**主动沉淀**的目录清单，可写、可再读回，落同一个 <c>data\newtoys.db</c> 的 <c>video_catalog</c> 表。
/// <para>
/// 字段（需求 §6.4.3）：文件名 / 编码格式 / 时长 / 大小 / 码率 / 文件地址 / 分辨率。
/// 「💾 加入视频库」只写**列表已有**信息；没读取媒体信息的行对应字段留空（NULL），⛔ 不强行跑 ffprobe。
/// </para>
/// <para>读别的库文件（顶部「读取数据库文件」）：<see cref="ReadFile"/> 直接以只读方式打开选中的 <c>.db</c>。</para>
/// </summary>
public class VideoCatalogService
{
    private const string Table = "video_catalog";

    /// <summary>
    /// v3.21：标签库 —— 标签是**一等公民**，能单独保存（新建了还没用到任何视频的也留着），
    /// ⛔ 不再是"扫 tags 列反推出来的临时名单"（需求 §6.4.6）。
    /// </summary>
    private const string TagTable = "video_tags";

    private readonly SqliteDatabase _db;

    public VideoCatalogService(SqliteDatabase db)
    {
        _db = db;
        _db.EnsureTable(
            $"CREATE TABLE IF NOT EXISTS {TagTable} (" +
            "  name       TEXT PRIMARY KEY," +
            "  created_at INTEGER" +
            ");");
        _db.EnsureTable(
            $"CREATE TABLE IF NOT EXISTS {Table} (" +
            "  path      TEXT PRIMARY KEY," +
            "  name      TEXT NOT NULL," +
            "  size      INTEGER," +
            "  duration  REAL," +
            "  width     INTEGER," +
            "  height    INTEGER," +
            "  codec     TEXT," +
            "  bit_rate  INTEGER," +
            "  tags      TEXT" +
            ");");
        // v3.16：老库（v3.15 之前建的）可能没有 tags / modified 列 —— 补列，
        // ⛔ 不做数据回填，读的时候缺什么按默认处理（见 ReadFile 的列探测）。
        EnsureColumn("tags", "TEXT");
        EnsureColumn("modified", "INTEGER");
    }

    /// <summary>
    /// 给已有表补一列（幂等）：列已存在时 SQLite 报 <c>duplicate column name</c>，直接忽略。
    /// ⛔ 只加**可空**列（带 NOT NULL + 默认值会让老版本 SQLite 拒绝）。
    /// </summary>
    private void EnsureColumn(string column, string type)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {Table} ADD COLUMN {column} {type};";
            cmd.ExecuteNonQuery();
        }
        catch { /* 已存在 / 库只读：都不影响使用 */ }
    }

    /// <summary>数据库可用性（false = 视频库不可用，功能降级但页面照常）。</summary>
    public bool IsAvailable => _db.IsAvailable;

    /// <summary>数据库文件全路径（页面提示用）。</summary>
    public string DatabasePath => _db.DatabasePath;

    /// <summary>把一条视频写进视频库（同地址 <c>INSERT OR REPLACE</c>）。⛔ 只写已有信息，未读的字段留空。</summary>
    public void Upsert(VideoItem item)
    {
        if (!IsAvailable || item == null) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"INSERT OR REPLACE INTO {Table} (path, name, size, duration, width, height, codec, bit_rate, tags, modified) " +
                "VALUES ($path, $name, $size, $duration, $width, $height, $codec, $bitRate, $tags, $modified);";
            cmd.Parameters.AddWithValue("$path", item.FullPath ?? "");
            cmd.Parameters.AddWithValue("$name", item.Name ?? "");
            cmd.Parameters.AddWithValue("$size", item.SizeBytes);
            cmd.Parameters.AddWithValue("$duration", (object?)item.Duration ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$width", (object?)item.Width ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$height", (object?)item.Height ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$codec", item.Codec ?? "");
            cmd.Parameters.AddWithValue("$bitRate", (object?)item.BitRate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tags", item.Tags ?? "");
            // v3.16：修改时间也存下来 —— 以前读回一整库，列表里的"修改时间"全是导入那一刻，没意义。
            cmd.Parameters.AddWithValue("$modified", item.Modified == default ? (object?)DBNull.Value : item.Modified.Ticks);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // 落库失败不影响界面（下次还能再写）
        }
    }

    /// <summary>v3.15：该路径是否已在视频库里（标签 / 收藏都要求"先入视频库"，见需求 §6.4.6 / §6.4.9）。</summary>
    public bool HasPath(string path)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {Table} WHERE path = $path LIMIT 1;";
            cmd.Parameters.AddWithValue("$path", path);
            return cmd.ExecuteScalar() != null;
        }
        catch { return false; }
    }

    /// <summary>v3.15：取某条记录的标签（收藏页卡片直接读它；不在库里 / 出错返回空串）。</summary>
    public string GetTags(string path)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT tags FROM {Table} WHERE path = $path LIMIT 1;";
            cmd.Parameters.AddWithValue("$path", path);
            var v = cmd.ExecuteScalar();
            return v is null or DBNull ? "" : (v as string ?? "");
        }
        catch { return ""; }
    }

    /// <summary>v3.15：更新某条记录的标签（逗号分隔）。路径不存在时静默忽略。</summary>
    public void UpdateTags(string path, string tags)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path)) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE {Table} SET tags = $tags WHERE path = $path;";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.Parameters.AddWithValue("$tags", (tags ?? "").Trim());
            cmd.ExecuteNonQuery();
        }
        catch { /* 标签更新失败不影响界面 */ }
    }

    // ==================== v3.19 / NT-011：标签管理（弹层卡；v3.21 加标签库表） ====================

    /// <summary>
    /// v3.21：取全部标签 + 出现次数（**标签库 ∪ 已使用标签**，按次数降序、名称升序）。
    /// 标签库里新建但还没用到的也会出现（次数 0，界面显示「尚未使用」）。
    /// </summary>
    public List<(string Tag, int Count)> GetAllTags()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!IsAvailable) return new();

        // ① 标签库（含新建未用的）
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT name FROM {TagTable};";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var n = r.IsDBNull(0) ? "" : r.GetString(0);
                if (n.Length == 0) continue;
                if (!counts.ContainsKey(n)) counts[n] = 0;
            }
        }
        catch { }

        // ② 各条目 tags 列里实际用到的（次数以它为准）
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT tags FROM {Table} WHERE tags IS NOT NULL AND tags <> '';";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var raw = r.IsDBNull(0) ? "" : r.GetString(0);
                foreach (var t in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            }
        }
        catch { }

        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                     .Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>v3.21：新建一个标签（**真入库**，未用到任何视频也留着）。返回 false = 已存在 / 库不可用。</summary>
    public bool CreateTag(string name)
    {
        name = (name ?? "").Trim();
        if (!IsAvailable || name.Length == 0) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT OR IGNORE INTO {TagTable} (name, created_at) VALUES ($name, $at);";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$at", DateTime.Now.Ticks);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>v3.21：从标签库里删掉一个名字（⛔ 只删库里的名字，不动各行已打的标签 —— 那由 <see cref="DeleteTag"/> 负责）。</summary>
    public void DeleteTagIndex(string name)
    {
        name = (name ?? "").Trim();
        if (!IsAvailable || name.Length == 0) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {TagTable} WHERE name = $name;";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    /// <summary>
    /// 把所有条目里的 <paramref name="oldTag"/> 改写为 <paramref name="newTag"/>（与已有重名则合并去重）。返回受影响行数。
    /// v3.21：顺带把**标签库里的名字**也改掉（新名已存在 → 删掉旧的那条，避免重名）。
    /// </summary>
    public int RenameTag(string oldTag, string newTag)
    {
        var n = RewriteTags(tags =>
        {
            var i = tags.FindIndex(t => string.Equals(t, oldTag, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            tags[i] = newTag;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var j = tags.Count - 1; j >= 0; j--) if (!seen.Add(tags[j])) tags.RemoveAt(j);
            return true;
        });

        var nn = (newTag ?? "").Trim();
        if (nn.Length > 0 && IsAvailable)
        {
            try
            {
                using var conn = _db.Open();
                using var cmd = conn.CreateCommand();
                // 新名已在库里 → 旧名那条直接删（主键冲突，IGNORE 掉）；否则改名为新名
                cmd.CommandText = $"UPDATE OR IGNORE {TagTable} SET name = $new WHERE name = $old;";
                cmd.Parameters.AddWithValue("$old", (oldTag ?? "").Trim());
                cmd.Parameters.AddWithValue("$new", nn);
                cmd.ExecuteNonQuery();
                DeleteTagIndex(oldTag);
            }
            catch { }
        }
        return n;
    }

    /// <summary>
    /// 把所有条目里的 <paramref name="tag"/> 删掉，并把**标签库里的名字**一并删掉（v3.21）。返回受影响行数。
    /// </summary>
    public int DeleteTag(string tag)
    {
        var n = RewriteTags(tags =>
            tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) > 0);
        DeleteTagIndex(tag);
        return n;
    }

    /// <summary>批量改写标签：读出全部 (path, tags)、对每个标签表应用 <paramref name="transform"/>、变了就 UPDATE。</summary>
    private int RewriteTags(Func<List<string>, bool> transform)
    {
        if (!IsAvailable) return 0;
        var affected = 0;
        try
        {
            using var conn = _db.Open();
            var rows = new List<(string Path, List<string> Tags)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT path, tags FROM {Table};";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var p = r.IsDBNull(0) ? "" : r.GetString(0);
                    var raw = r.IsDBNull(1) ? "" : r.GetString(1);
                    rows.Add((p, raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()));
                }
            }
            foreach (var (p, tags) in rows)
            {
                var before = string.Join(",", tags);
                if (!transform(tags)) continue;
                var after = string.Join(",", tags);
                if (after == before) continue;
                using var upd = conn.CreateCommand();
                upd.CommandText = $"UPDATE {Table} SET tags = $t WHERE path = $p;";
                upd.Parameters.AddWithValue("$t", after);
                upd.Parameters.AddWithValue("$p", p);
                affected += upd.ExecuteNonQuery();
            }
        }
        catch { }
        return affected;
    }

    /// <summary>v3.15：详情卡内改名后，按旧路径把视频库里的 path / name 同步成新的（不重扫）。</summary>
    public void MovePath(string oldPath, string newPath, string newName)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath)) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE {Table} SET path = $new, name = $name WHERE path = $old;";
            cmd.Parameters.AddWithValue("$old", oldPath);
            cmd.Parameters.AddWithValue("$new", newPath);
            cmd.Parameters.AddWithValue("$name", newName ?? Path.GetFileName(newPath));
            cmd.ExecuteNonQuery();
        }
        catch { /* 同步失败不影响界面 */ }
    }

    /// <summary>
    /// 读另一个 <c>.db</c> 文件的 <c>video_catalog</c> 表，返回视频项 + 失败原因（文件可能已不存在，照常显示）。
    /// <para>
    /// v3.16：以前读不到就**静默返回空列表**（异常被 <c>catch</c> 吞掉），界面上只表现为"列表没反应"，
    /// 查不出是"文件挑错了 / 表里没数据 / 老库缺列"。现在按下面三条给**可诊断的原因**：
    /// ① 文件打不开；② 没有 <c>video_catalog</c> 表；③ 查询报错（含 SQLite 原文）。
    /// </para>
    /// <para>
    /// **兼容旧库**：先用 <c>PRAGMA table_info</c> 拿到实际列名，再按列取值 —— 缺 <c>tags</c> / <c>modified</c>
    /// 这类后加的列时按默认值读，⛔ 不整表读不出来。
    /// </para>
    /// </summary>
    public CatalogReadResult ReadFile(string dbPath)
    {
        var result = new CatalogReadResult();
        if (!File.Exists(dbPath))
        {
            result.Error = "文件不存在：" + dbPath;
            return result;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly };
            using var conn = new SqliteConnection(builder.ToString());
            conn.Open();

            if (!TableExists(conn))
            {
                // 挑错文件是最常见的原因 —— 直接说清它里面有哪些表，比干说"不是视频库"有用。
                result.Error = $"「{Path.GetFileName(dbPath)}」里没有 {Table} 表（不是本程序导出的视频库）；"
                             + "该文件里的表：" + string.Join(" / ", ListTables(conn));
                return result;
            }

            var cols = new HashSet<string>(ListColumns(conn), StringComparer.OrdinalIgnoreCase);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {string.Join(", ", ColNames.Select(c => cols.Contains(c) ? c : "NULL AS " + c))} FROM {Table};";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = NullOrString(reader, 0);
                var item = new VideoItem
                {
                    FullPath = path,
                    Name = NullOrString(reader, 1) is { Length: > 0 } n
                        ? n
                        : (string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path)),
                    SizeBytes = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    Extension = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).ToLowerInvariant()
                };
                if (!reader.IsDBNull(3)) item.Duration = reader.GetDouble(3);
                if (!reader.IsDBNull(4)) item.Width = reader.GetInt32(4);
                if (!reader.IsDBNull(5)) item.Height = reader.GetInt32(5);
                item.Codec = NullOrString(reader, 6);
                if (!reader.IsDBNull(7)) item.BitRate = reader.GetInt64(7);
                item.Tags = NullOrString(reader, 8);

                // 修改时间：库里存了就用库里的；没有（老库）就看文件还在不在；都没有才退到"现在"
                if (!reader.IsDBNull(9)) item.Modified = new DateTime(reader.GetInt64(9));
                else item.Modified = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;

                if (item.Width is > 0 || item.Duration is > 0 || !string.IsNullOrWhiteSpace(item.Codec))
                    item.MediaStatus = $"{item.ResolutionDisplay} · {item.CodecDisplay} · {item.BitRateDisplay} · {item.DurationDisplay}";

                result.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            result.Error = "读不了这个 .db：" + ex.Message;
            result.Items.Clear();
        }
        return result;
    }

    /// <summary>读取时按固定顺序取的列（缺的列用 <c>NULL AS xxx</c> 顶上，保证列序稳定）。</summary>
    private static readonly string[] ColNames =
        { "path", "name", "size", "duration", "width", "height", "codec", "bit_rate", "tags", "modified" };

    private static string NullOrString(Microsoft.Data.Sqlite.SqliteDataReader reader, int i)
        => reader.IsDBNull(i) ? "" : reader.GetString(i);

    private bool TableExists(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{Table}' LIMIT 1;";
        return cmd.ExecuteScalar() != null;
    }

    private static List<string> ListTables(SqliteConnection conn)
    {
        var names = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.IsDBNull(0) ? "?" : r.GetString(0));
        return names.Count == 0 ? new List<string> { "（空）" } : names;
    }

    private static List<string> ListColumns(SqliteConnection conn)
    {
        var names = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({Table});";
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.IsDBNull(1) ? "" : r.GetString(1));   // 第 2 列 = 列名
        return names;
    }
}

/// <summary>读外部视频库的结果：<see cref="CatalogReadResult.Items"/> = 记录，<see cref="CatalogReadResult.Error"/> = 失败原因（正常时为 <c>null</c>）。</summary>
public sealed class CatalogReadResult
{
    /// <summary>读到的记录（可能为空：库是空的）。</summary>
    public List<VideoItem> Items { get; } = new();

    /// <summary>读不出来时的原因（⛔ 不许静默失败：界面必须把它显示出来）。</summary>
    public string? Error { get; set; }

    /// <summary>是不是"读成功了，但库里一条记录都没有"。</summary>
    public bool IsEmptyLibrary => Error == null && Items.Count == 0;
}

