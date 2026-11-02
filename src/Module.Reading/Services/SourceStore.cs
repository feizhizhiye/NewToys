using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>
/// 书源库读写（<c>book_sources</c> 表）。
/// <para>
/// 🔴 <c>url</c> 是主键 ⇒ 重复导入按 url <b>更新</b>，⛔ 不产生副本。
/// </para>
/// <para>库不可用时全部方法安静返回（空列表 / 0 条），⛔ 不抛 —— 页面按「库里没有书源」渲染。</para>
/// </summary>
public sealed class SourceStore
{
    private const string Columns = "url,name,group_name,source_type,enabled,rules_json,last_used_at,check_state,check_note,checked_at";

    private readonly ReadingDatabase _db;

    public SourceStore(ReadingDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    public string DatabasePath => _db.DatabasePath;

    /// <summary>读全部书源（按名称排序；能力档在此重算，⛔ 不落库）。</summary>
    public IReadOnlyList<BookSource> LoadAll()
    {
        var list = new List<BookSource>();
        if (!_db.IsAvailable) return list;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM book_sources ORDER BY name COLLATE NOCASE;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(ReadRow(reader));
        }
        catch { /* 库坏了也照常返回已读到的部分 */ }

        return list;
    }

    /// <summary>按 url 取一条（找不到 ⇒ null）。</summary>
    public BookSource? Find(string url)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM book_sources WHERE url = $url LIMIT 1;";
            cmd.Parameters.AddWithValue("$url", url);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadRow(reader) : null;
        }
        catch { return null; }
    }

    /// <summary>幂等写入（按 url 覆盖）。返回成功写入的条数。</summary>
    public int Upsert(IEnumerable<BookSource> sources)
    {
        if (!_db.IsAvailable) return 0;

        var written = 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();

            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source.Url)) continue;

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO book_sources(url,name,group_name,source_type,enabled,rules_json,last_used_at)
VALUES($url,$name,$group,$type,$enabled,$rules,NULL)
ON CONFLICT(url) DO UPDATE SET
  name = excluded.name,
  group_name = excluded.group_name,
  source_type = excluded.source_type,
  enabled = excluded.enabled,
  rules_json = excluded.rules_json;";
                cmd.Parameters.AddWithValue("$url", source.Url);
                cmd.Parameters.AddWithValue("$name", source.Name ?? "");
                cmd.Parameters.AddWithValue("$group", source.Group ?? "");
                cmd.Parameters.AddWithValue("$type", source.Type);
                cmd.Parameters.AddWithValue("$enabled", source.Enabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$rules", string.IsNullOrWhiteSpace(source.RawJson)
                    ? JsonSerializer.Serialize(source)
                    : source.RawJson);
                written += cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { return written; }

        return written;
    }

    /// <summary>删除若干书源。</summary>
    public int Delete(IEnumerable<string> urls)
    {
        if (!_db.IsAvailable) return 0;

        var deleted = 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM book_sources WHERE url = $url;";
                cmd.Parameters.AddWithValue("$url", url);
                deleted += cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { return deleted; }

        return deleted;
    }

    /// <summary>批量启用 / 停用。</summary>
    public int SetEnabled(IEnumerable<string> urls, bool enabled)
    {
        if (!_db.IsAvailable) return 0;

        var changed = 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE book_sources SET enabled = $enabled WHERE url = $url;";
                cmd.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$url", url);
                changed += cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { return changed; }

        return changed;
    }

    /// <summary>
    /// 批量改分组（<c>""</c> = 未分组）。
    /// <para>🔴 必须同时回写 <c>rules_json</c> 里的 <c>bookSourceGroup</c>：导出走的是原始 JSON，
    /// 只改 <c>group_name</c> 的话导出再导入分组就回去了。</para>
    /// </summary>
    public int SetGroup(IEnumerable<string> urls, string? group)
    {
        if (!_db.IsAvailable) return 0;

        var name = (group ?? "").Trim();
        var changed = 0;

        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();

            foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE book_sources SET group_name = $group, rules_json = $rules WHERE url = $url;";
                cmd.Parameters.AddWithValue("$group", name);
                cmd.Parameters.AddWithValue("$rules", PatchGroup(ReadRaw(conn, tx, url), name));
                cmd.Parameters.AddWithValue("$url", url);
                changed += cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { return changed; }

        return changed;
    }

    /// <summary>写一次校验结论（通过 / 无结果 / 失败）。</summary>
    public void SetCheck(string url, SourceCheckState state, string note, DateTimeOffset at)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE book_sources
SET check_state = $state, check_note = $note, checked_at = $at
WHERE url = $url;";
            cmd.Parameters.AddWithValue("$state", (int)state);
            cmd.Parameters.AddWithValue("$note", note ?? "");
            cmd.Parameters.AddWithValue("$at", at.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$url", url);
            cmd.ExecuteNonQuery();
        }
        catch { /* 校验结论写不进去不影响本次校验 */ }
    }

    private static string ReadRaw(SqliteConnection conn, SqliteTransaction tx, string url)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT rules_json FROM book_sources WHERE url = $url LIMIT 1;";
            cmd.Parameters.AddWithValue("$url", url);
            return cmd.ExecuteScalar() as string ?? "";
        }
        catch { return ""; }
    }

    /// <summary>把原始 JSON 里的 <c>bookSourceGroup</c> 改成目标分组（原文坏 / 空 ⇒ 原样返回，只改库列）。</summary>
    private static string PatchGroup(string rawJson, string group)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return rawJson;

        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(rawJson) is not System.Text.Json.Nodes.JsonObject obj) return rawJson;

            if (group.Length == 0) obj.Remove("bookSourceGroup");
            else obj["bookSourceGroup"] = group;
            return obj.ToJsonString();
        }
        catch { return rawJson; }
    }

    /// <summary>记一次「用过」（详情卡显示上次使用时间）。</summary>
    public void TouchUsed(string url)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE book_sources SET last_used_at = $at WHERE url = $url;";
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$url", url);
            cmd.ExecuteNonQuery();
        }
        catch { /* 记录使用时间失败不影响抓取 */ }
    }

    private static BookSource ReadRow(SqliteDataReader reader)
    {
        var json = reader.IsDBNull(5) ? "" : reader.GetString(5);

        BookSource? source = null;
        if (json.Length > 0)
        {
            try { source = JsonSerializer.Deserialize<BookSource>(json); }
            catch { source = null; }
        }

        source ??= new BookSource();
        source.Url = reader.GetString(0);
        source.Name = reader.GetString(1);
        source.Group = reader.IsDBNull(2) ? "" : reader.GetString(2);
        source.Type = reader.GetInt32(3);
        source.Enabled = reader.GetInt32(4) != 0;
        source.RawJson = json;

        if (!reader.IsDBNull(6) && DateTimeOffset.TryParse(reader.GetString(6), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var at))
            source.LastUsedAt = at;

        source.CheckState = (SourceCheckState)reader.GetInt32(7);
        source.CheckNote = reader.IsDBNull(8) ? "" : reader.GetString(8);
        if (!reader.IsDBNull(9) && DateTimeOffset.TryParse(reader.GetString(9), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var checkedAt))
            source.CheckedAt = checkedAt;

        var (capability, reason) = SourceAnalyzer.Analyze(source);
        source.Capability = capability;
        source.CapabilityReason = reason;
        return source;
    }
}
