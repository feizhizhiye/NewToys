using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>
/// 阅读进度读写（<c>reading_progress</c> 表）：每本书记「读到哪一章、章内滚动到哪」。
/// <para>一本书可以有多行（每章一行），取最后更新的那条作为「继续阅读」的落点。</para>
/// </summary>
public sealed class ProgressStore
{
    private readonly ReadingDatabase _db;

    public ProgressStore(ReadingDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    /// <summary>记一次进度（同书同章覆盖）。</summary>
    public bool Save(string bookUrl, string chapterUrl, string chapterName, double offset)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(bookUrl) || string.IsNullOrWhiteSpace(chapterUrl)) return false;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO reading_progress(book_url,chapter_url,chapter_name,""offset"",updated_at)
VALUES($book,$chapter,$name,$offset,$at)
ON CONFLICT(book_url,chapter_url) DO UPDATE SET
  chapter_name = excluded.chapter_name,
  ""offset"" = excluded.""offset"",
  updated_at = excluded.updated_at;";
            cmd.Parameters.AddWithValue("$book", bookUrl);
            cmd.Parameters.AddWithValue("$chapter", chapterUrl);
            cmd.Parameters.AddWithValue("$name", chapterName ?? "");
            cmd.Parameters.AddWithValue("$offset", Math.Clamp(offset, 0, 1));
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>这本书最后读到的位置（没有 ⇒ <c>null</c>）。</summary>
    public ReadingProgress? Get(string bookUrl)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(bookUrl)) return null;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT book_url,chapter_url,chapter_name,""offset"",updated_at
FROM reading_progress WHERE book_url = $book
ORDER BY updated_at DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("$book", bookUrl);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
        catch { return null; }
    }

    /// <summary>全部书的最新进度（书架一次取完，⛔ 不逐书查库）。</summary>
    public IReadOnlyDictionary<string, ReadingProgress> LoadLatest()
    {
        var map = new Dictionary<string, ReadingProgress>(StringComparer.Ordinal);
        if (!_db.IsAvailable) return map;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT p.book_url,p.chapter_url,p.chapter_name,p.""offset"",p.updated_at
FROM reading_progress p
JOIN (SELECT book_url, MAX(updated_at) AS at FROM reading_progress GROUP BY book_url) latest
  ON latest.book_url = p.book_url AND latest.at = p.updated_at;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var progress = Read(reader);
                map[progress.BookUrl] = progress;
            }
        }
        catch { /* 库坏了按「没进度」处理 */ }

        return map;
    }

    /// <summary>移出书架时连带清进度。</summary>
    public int Remove(string bookUrl)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(bookUrl)) return 0;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM reading_progress WHERE book_url = $book;";
            cmd.Parameters.AddWithValue("$book", bookUrl);
            return cmd.ExecuteNonQuery();
        }
        catch { return 0; }
    }

    private static ReadingProgress Read(SqliteDataReader reader) => new()
    {
        BookUrl = reader.GetString(0),
        ChapterUrl = reader.GetString(1),
        ChapterName = reader.GetString(2),
        Offset = reader.GetDouble(3),
        UpdatedAt = DateTimeOffset.TryParse(reader.GetString(4), out var at) ? at : default,
    };
}
