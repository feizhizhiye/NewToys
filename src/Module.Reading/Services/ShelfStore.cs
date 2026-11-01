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
    private const string Columns = "book_url,source_url,name,author,cover_url,added_at";

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
                    AddedAt = DateTimeOffset.TryParse(reader.GetString(5), out var at) ? at : default,
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
INSERT INTO shelf(book_url,source_url,name,author,cover_url,added_at)
VALUES($url,$source,$name,$author,$cover,$at)
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
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
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
