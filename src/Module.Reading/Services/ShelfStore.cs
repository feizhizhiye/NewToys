using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>
/// 书架读写（<c>shelf</c> 表）：把搜索结果里感兴趣的书存下来，下次从「书架」直接进目录。
/// <para>以 <c>book_url</c> 为主键 ⇒ 同一本重复加入只更新，⛔ 不产生重复行。</para>
/// </summary>
public sealed class ShelfStore
{
    private const string Columns = "book_url,source_url,name,author,cover_url,group_name,added_at,last_chapter,chapter_count,new_count,checked_at,update_note";

    private readonly ReadingDatabase _db;

    public ShelfStore(ReadingDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    /// <summary>全部藏书（新加的在前）。</summary>
    public IReadOnlyList<ShelfBook> LoadAll()
    {
        var list = new List<ShelfBook>();
        if (!_db.IsAvailable) return list;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM shelf ORDER BY added_at DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ShelfBook
                {
                    BookUrl = reader.GetString(0),
                    SourceUrl = reader.GetString(1),
                    Name = reader.GetString(2),
                    Author = reader.GetString(3),
                    CoverUrl = reader.GetString(4),
                    Group = reader.GetString(5),
                    AddedAt = DateTimeOffset.TryParse(reader.GetString(6), out var at) ? at : default,
                    LatestChapter = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    ChapterCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    NewCount = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    UpdateNote = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    CheckedAt = !reader.IsDBNull(10) && DateTimeOffset.TryParse(reader.GetString(10), out var checkedAt)
                        ? checkedAt
                        : null,
                });
            }
        }
        catch { /* 库坏了返回已读到的部分 */ }

        return list;
    }

    /// <summary>这本是否已在书架里。</summary>
    public bool Contains(string bookUrl)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(bookUrl)) return false;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM shelf WHERE book_url = $url LIMIT 1;";
            cmd.Parameters.AddWithValue("$url", bookUrl);
            return cmd.ExecuteScalar() is not null;
        }
        catch { return false; }
    }

    /// <summary>加入书架（已在则刷新书名 / 作者 / 封面，保留原加入时间）。</summary>
    public bool Add(ShelfBook book)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(book.BookUrl)) return false;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO shelf(book_url,source_url,name,author,cover_url,group_name,added_at)
VALUES($url,$source,$name,$author,$cover,$group,$at)
ON CONFLICT(book_url) DO UPDATE SET
  source_url = excluded.source_url,
  name = excluded.name,
  author = excluded.author,
  cover_url = excluded.cover_url;";
            cmd.Parameters.AddWithValue("$url", book.BookUrl);
            cmd.Parameters.AddWithValue("$source", book.SourceUrl ?? "");
            cmd.Parameters.AddWithValue("$name", book.Name ?? "");
            cmd.Parameters.AddWithValue("$author", book.Author ?? "");
            cmd.Parameters.AddWithValue("$cover", book.CoverUrl ?? "");
            cmd.Parameters.AddWithValue("$group", book.Group ?? "");
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>改分组（<paramref name="group"/> 空白 ⇒ 归「未分组」）。</summary>
    public int SetGroup(IEnumerable<string> bookUrls, string? group)
    {
        if (!_db.IsAvailable) return 0;

        var changed = 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            foreach (var url in bookUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE shelf SET group_name = $group WHERE book_url = $url;";
                cmd.Parameters.AddWithValue("$group", (group ?? "").Trim());
                cmd.Parameters.AddWithValue("$url", url);
                changed += cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { return changed; }

        return changed;
    }

    /// <summary>
    /// 写一次「批量更新」结论。
    /// <para><paramref name="note"/> 非空 = 这次检查失败（只更新时间和原因，<b>保留上次的章数基线</b>，
    /// 否则失败一次就把「有新章节」的对比基准冲掉了）。</para>
    /// </summary>
    public void SetUpdate(string bookUrl, string? latestChapter, int chapterCount, int newCount,
        string? note, DateTimeOffset at)
    {
        if (!_db.IsAvailable || string.IsNullOrWhiteSpace(bookUrl)) return;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            var failed = !string.IsNullOrWhiteSpace(note);

            cmd.CommandText = failed
                ? "UPDATE shelf SET checked_at = $at, update_note = $note WHERE book_url = $url;"
                : @"UPDATE shelf
SET last_chapter = $latest, chapter_count = $count, new_count = $new, checked_at = $at, update_note = ''
WHERE book_url = $url;";

            cmd.Parameters.AddWithValue("$url", bookUrl);
            cmd.Parameters.AddWithValue("$at", at.ToString("O"));
            cmd.Parameters.AddWithValue("$note", note ?? "");
            if (!failed)
            {
                cmd.Parameters.AddWithValue("$latest", latestChapter ?? "");
                cmd.Parameters.AddWithValue("$count", chapterCount);
                cmd.Parameters.AddWithValue("$new", newCount);
            }

            cmd.ExecuteNonQuery();
        }
        catch { /* 更新结论写不进去不影响本次检查 */ }
    }

    /// <summary>移出书架（不动源文件，只是不再列出）。</summary>
    public int Remove(IEnumerable<string> bookUrls)
    {
        if (!_db.IsAvailable) return 0;

        var removed = 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();
            foreach (var url in bookUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM shelf WHERE book_url = $url;";
                cmd.Parameters.AddWithValue("$url", url);
                removed += cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { return removed; }

        return removed;
    }
}
