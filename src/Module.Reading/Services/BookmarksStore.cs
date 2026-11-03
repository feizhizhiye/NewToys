using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Reading.Controls;

namespace NewToys.Reading.Services;

/// <summary>
/// 阅读器书签持久化：每本索引书一个 <c>bookmarks.json</c>（落书目录，与 index.json 同级）。
/// <para>🔴 书签是附属产物：读写失败不影响阅读，静默返回空。</para>
/// </summary>
public sealed class BookmarksStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>读一本书的书签（按建立时间倒序）；没有 / 坏了返回空。</summary>
    public IReadOnlyList<ReaderBookmark> Load(string bookDir)
    {
        try
        {
            var path = Path.Combine(bookDir, "bookmarks.json");
            if (!File.Exists(path)) return Array.Empty<ReaderBookmark>();
            return JsonSerializer.Deserialize<List<ReaderBookmark>>(File.ReadAllText(path)) ?? [];
        }
        catch { return Array.Empty<ReaderBookmark>(); }
    }

    /// <summary>写一本书的书签（全量覆盖）。</summary>
    public void Save(string bookDir, IEnumerable<ReaderBookmark> bookmarks)
    {
        try
        {
            Directory.CreateDirectory(bookDir);
            File.WriteAllText(Path.Combine(bookDir, "bookmarks.json"),
                JsonSerializer.Serialize(bookmarks.OrderByDescending(b => b.CreatedAt).ToList(), JsonOpts));
        }
        catch { /* 附属产物 */ }
    }
}
