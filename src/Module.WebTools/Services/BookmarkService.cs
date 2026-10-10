using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NewToys.Core.Services;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>
/// 书签持久化与导入：数据落在 <c>config\bookmarks.json</c>（可被数据管理备份）。
/// <para>导入浏览器书签：用 <see cref="AngleSharp"/> 解析 Netscape 书签 HTML（<c>&lt;A HREF&gt;</c> + 文件夹 <c>&lt;H3&gt;</c>）。</para>
/// <para>联通检测：发一个带超时的 HTTP 请求，反馈状态码 / 超时 / 异常。</para>
/// </summary>
public class BookmarkService
{
    private readonly ConfigStoreService _cfg;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public BookmarkService(ConfigStoreService cfg) => _cfg = cfg;

    private string FilePath => Path.Combine(_cfg.ConfigDir, "bookmarks.json");

    public IReadOnlyList<BookmarkItem> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<BookmarkItem>();
            var list = JsonSerializer.Deserialize<List<BookmarkItem>>(File.ReadAllText(FilePath));
            return list ?? new List<BookmarkItem>();
        }
        catch { return new List<BookmarkItem>(); }
    }

    public void Save(IEnumerable<BookmarkItem> items)
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败不打断 UI */ }
    }

    /// <summary>解析浏览器导出的书签 HTML，返回新增条目（已按 URL 去重，跳过 JS/空链接）。</summary>
    public BookmarkImportResult ImportHtml(string html, IReadOnlyList<BookmarkItem> existing)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        var known = new HashSet<string>(existing.Select(e => e.Url), StringComparer.OrdinalIgnoreCase);
        var added = new List<BookmarkItem>();
        var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 递归遍历：遇到 H3 更新当前文件夹，遇到 A 收一条；文件夹随层级向下传递，正确处理嵌套分组。
        void Walk(INode node, string folder)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is not IElement el) continue;
                if (el.LocalName == "h3") { folder = (el.TextContent ?? "").Trim(); continue; }
                if (el.LocalName == "a")
                {
                    var href = (el.GetAttribute("href") ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(href)
                        || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                        || href.StartsWith("place:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (known.Contains(href) || added.Any(x => x.Url.Equals(href, StringComparison.OrdinalIgnoreCase))) continue;

                    var title = (el.TextContent ?? "").Trim();
                    if (title.Length == 0) title = href;
                    var item = new BookmarkItem { Title = title, Url = href, Folder = folder };
                    if (!string.IsNullOrWhiteSpace(folder)) folderSet.Add(folder);
                    added.Add(item);
                    known.Add(href);
                    continue;
                }
                Walk(el, folder);
            }
        }

        var root = (INode?)doc.DocumentElement ?? (INode?)doc.Body ?? doc;
        Walk(root, "");
        return new BookmarkImportResult { Added = added.Count, Folders = folderSet.Count, Items = added };
    }

    /// <summary>联通检测：发 HTTP 请求，返回 (是否成功, 状态文本)。</summary>
    public async Task<(bool ok, string msg)> CheckUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (false, "空网址");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return (false, "网址格式不正确");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, uri);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; NewToys/1.0)");
            var resp = await Http.SendAsync(req).ConfigureAwait(false);
            return (resp.IsSuccessStatusCode || (int)resp.StatusCode < 500,
                $"{(int)resp.StatusCode} {resp.StatusCode}");
        }
        catch (TaskCanceledException) { return (false, "超时"); }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
