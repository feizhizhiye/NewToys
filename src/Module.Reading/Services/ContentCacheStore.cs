using System;

namespace NewToys.Reading.Services;

/// <summary>
/// 正文缓存读写（<c>content_cache</c> 表）：按章节地址存「最终正文」，开启「缓存正文」后重看同一章秒开。
/// <para>⛔ 只存清洗后的正文文本（⛔ 不存原始 HTML），键是 <c>chapter_url</c>（同一本书不同章地址唯一）。</para>
/// </summary>
public sealed class ContentCacheStore
{
    private readonly ReadingDatabase _db;

    public ContentCacheStore(ReadingDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    /// <summary>读某章的缓存正文（没有 ⇒ <c>null</c>）。</summary>
    public string? Get(string chapterUrl)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(chapterUrl)) return null;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT content FROM content_cache WHERE chapter_url = $url;";
            cmd.Parameters.AddWithValue("$url", chapterUrl);

            var result = cmd.ExecuteScalar();
            return result is string text && text.Length > 0 ? text : null;
        }
        catch { return null; }
    }

    /// <summary>清空全部正文缓存（改了替换规则 / 想让章节重新抓时用）。返回删掉的章数。</summary>
    public int Clear()
    {
        if (!_db.IsAvailable) return 0;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM content_cache;";
            return cmd.ExecuteNonQuery();
        }
        catch { return 0; }
    }

    /// <summary>写 / 覆盖某章的缓存正文。</summary>
    public bool Put(string chapterUrl, string content)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(chapterUrl) || string.IsNullOrWhiteSpace(content)) return false;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO content_cache(chapter_url,content,updated_at)
VALUES($url,$content,$at)
ON CONFLICT(chapter_url) DO UPDATE SET
  content = excluded.content,
  updated_at = excluded.updated_at;";
            cmd.Parameters.AddWithValue("$url", chapterUrl);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }
}
