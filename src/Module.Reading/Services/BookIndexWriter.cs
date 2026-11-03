using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.Reading.Controls;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>
/// 「索引书」落盘：把一本书导成阅读页可直接读的目录 + 逐章正文 ——
/// <c>store/reading/books/&lt;bookId&gt;/index.json</c>（书目 + 章节清单）+ <c>chapters/NNNN.txt</c>（清洗后正文，UTF-8）；
/// 本地 TXT / MD 走 <see cref="ExportLocalText"/> 落到 <c>store/reading/local/</c>。
/// <para>
/// 正文来源 = 正文缓存（<see cref="ContentCacheStore"/>）：抓过的章直接落盘，⛔ 不联网补抓 ——
/// 缺章由阅读页翻到时补写（<see cref="WriteChapter"/>）或整本下载后再导一次。
/// 阅读页只认这两个东西 ⇒ 天然离线、重启续读。
/// </para>
/// </summary>
public sealed class BookIndexWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ContentCacheStore _cache;
    private readonly ConfigStoreService _cfg;

    public BookIndexWriter(ContentCacheStore cache, ConfigStoreService cfg)
    {
        _cache = cache;
        _cfg = cfg;
    }

    /// <summary>bookId = book_url 的 SHA256 前 8 字节（16 个 hex 字符）：同一本书换封面 / 改名不换目录。</summary>
    public static string BookIdOf(string bookUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(bookUrl ?? ""));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>索引书目录（<c>store/reading/books/&lt;bookId&gt;/</c>）。</summary>
    public string BookDirOf(string bookUrl)
        => Path.Combine(_cfg.StoreDir, "reading", "books", BookIdOf(bookUrl));

    /// <summary>
    /// 本地 TXT / MD 落盘成索引书（<c>store/reading/local/&lt;hash&gt;/</c>）：
    /// hash = 文件路径 + 大小 + 修改时间 ⇒ 文件变了自动换目录重导，没变则直接复用已有落盘。
    /// </summary>
    public string? ExportLocalText(string filePath, string text, IReadOnlyList<ReaderChapter> chapters, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(filePath) || chapters.Count == 0)
        {
            error = "没有可导入的内容。";
            return null;
        }

        try
        {
            var info = new FileInfo(filePath);
            var key = $"{filePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var dir = Path.Combine(_cfg.StoreDir, "reading", "local", BookIdOf(key));
            var indexPath = Path.Combine(dir, "index.json");

            if (File.Exists(indexPath)) return indexPath;   // 同一文件已导过 ⇒ 直接复用

            Directory.CreateDirectory(Path.Combine(dir, "chapters"));

            var dto = new IndexBookDto
            {
                BookId = BookIdOf(key),
                Title = Path.GetFileNameWithoutExtension(filePath),
                Author = "",
                SourceUrl = filePath,
            };
            foreach (var chapter in chapters)
            {
                var index = dto.Chapters.Count;
                dto.Chapters.Add(new IndexChapterDto
                {
                    Index = index,
                    Title = chapter.Title ?? "",
                    Url = "",
                    File = $"{index + 1:D4}.txt",
                });
            }

            File.WriteAllText(indexPath, JsonSerializer.Serialize(dto, JsonOpts), new UTF8Encoding(false));

            for (var i = 0; i < chapters.Count; i++)
            {
                var slice = text.Substring(chapters[i].Start, Math.Min(chapters[i].Length, text.Length - chapters[i].Start));
                WriteChapterFile(Path.Combine(dir, "chapters", $"{i + 1:D4}.txt"), slice);
            }

            return indexPath;
        }
        catch (Exception ex)
        {
            error = "导入本地文件失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>写 index.json + 把已缓存章落盘（不联网）；返回索引文件绝对路径，失败返回 null。</summary>
    public string? ExportBook(string bookUrl, string title, string author, string sourceUrl,
        IReadOnlyList<Chapter> chapters, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(bookUrl) || chapters.Count == 0)
        {
            error = "还没有可导出的目录。";
            return null;
        }

        try
        {
            var dir = Path.Combine(_cfg.StoreDir, "reading", "books", BookIdOf(bookUrl));
            Directory.CreateDirectory(Path.Combine(dir, "chapters"));

            var dto = new IndexBookDto
            {
                BookId = BookIdOf(bookUrl),
                Title = title ?? "",
                Author = author ?? "",
                SourceUrl = sourceUrl ?? "",
            };
            foreach (var chapter in chapters)
            {
                var index = dto.Chapters.Count;
                dto.Chapters.Add(new IndexChapterDto
                {
                    Index = index,
                    Title = chapter.Name ?? "",
                    Url = chapter.Url ?? "",
                    File = $"{index + 1:D4}.txt",
                });
            }

            var indexPath = Path.Combine(dir, "index.json");
            File.WriteAllText(indexPath, JsonSerializer.Serialize(dto, JsonOpts), new UTF8Encoding(false));

            var written = 0;
            for (var i = 0; i < chapters.Count; i++)
            {
                var file = Path.Combine(dir, "chapters", $"{i + 1:D4}.txt");
                if (File.Exists(file)) continue;

                var cached = _cache.Get(chapters[i].Url);
                if (string.IsNullOrWhiteSpace(cached)) continue;
                if (WriteChapterFile(file, cached)) written++;
            }

            return indexPath;
        }
        catch (Exception ex)
        {
            error = "生成书目索引失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>读完一章顺手落盘（换规则重抓后也走这里 ⇒ 文件始终是最新清洗结果）。</summary>
    public void WriteChapter(string bookUrl, int chapterIndex, string content)
    {
        if (string.IsNullOrWhiteSpace(bookUrl) || string.IsNullOrWhiteSpace(content) || chapterIndex < 0) return;

        try
        {
            var file = Path.Combine(_cfg.StoreDir, "reading", "books", BookIdOf(bookUrl),
                "chapters", $"{chapterIndex + 1:D4}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            WriteChapterFile(file, content);
        }
        catch { /* 落盘是附属产物，失败不碍阅读 */ }
    }

    private static bool WriteChapterFile(string file, string content)
    {
        try
        {
            File.WriteAllText(file, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
            return true;
        }
        catch { return false; }
    }

    private sealed class IndexBookDto
    {
        public string BookId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public List<IndexChapterDto> Chapters { get; set; } = new();
    }

    private sealed class IndexChapterDto
    {
        public int Index { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string File { get; set; } = "";
    }
}
