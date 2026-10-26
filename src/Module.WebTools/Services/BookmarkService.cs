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
using NewToys.Core.Services.DataMigrations;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>
/// 书签持久化与导入：数据落**本模块库 <c>web_docs</c> 的 <c>bookmarks</c> 文档**（旧
/// <c>config\bookmarks.json</c> 由 <see cref="WebToolsSchemaContributor"/> 一次性搬入并改名 <c>.legacy</c>）。
/// <para>导入浏览器书签：用 <see cref="AngleSharp"/> 解析 Netscape 书签 HTML（<c>&lt;A HREF&gt;</c> + 文件夹 <c>&lt;H3&gt;</c>）。</para>
/// <para>联通检测：发一个带超时的 HTTP 请求，反馈状态码 / 超时 / 异常。</para>
/// </summary>
public class BookmarkService
{
    private readonly ConfigStoreService _cfg;
    private readonly JsonDocStore _docs;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public BookmarkService(ConfigStoreService cfg, WebDb db)
    {
        _cfg = cfg;
        _docs = new JsonDocStore(db, "web");
    }

    /// <summary>旧文件（迁移源；搬完是 <c>bookmarks.json.legacy</c>）。只在 <see cref="Load"/> 里做兜底搬运。</summary>
    private string LegacyFile => Path.Combine(_cfg.ConfigDir, WebToolsSchemaContributor.DocBookmarks + ".json");

    public IReadOnlyList<BookmarkItem> Load()
    {
        try
        {
            JsonDocMigration.ImportOnce(_docs, WebToolsSchemaContributor.DocBookmarks, LegacyFile);
            return _docs.Load<List<BookmarkItem>>(WebToolsSchemaContributor.DocBookmarks) ?? new List<BookmarkItem>();
        }
        catch { return new List<BookmarkItem>(); }
    }

    public void Save(IEnumerable<BookmarkItem> items)
    {
        try
        {
            var list = items.ToList();
            // 空列表即删（沿用 「懒创建」语义：没有书签就不留空文档）
            if (list.Count == 0) { _docs.Delete(WebToolsSchemaContributor.DocBookmarks); return; }
            _docs.Save(WebToolsSchemaContributor.DocBookmarks, list);
        }
        catch { /* 保存失败不打断 UI */ }
    }

    /// <summary>解析浏览器导出的书签 HTML，返回新增条目（已按 URL 去重，跳过 JS/空链接）。</summary>
    /// <remarks>
    /// 文件夹用**全路径**记到 <see cref="BookmarkItem.Folder"/>（<c>/</c> 分隔），页面据此派生树。
    /// 🔴 ⛔ 按「遍历到 H3 就改当前文件夹」的写法：浏览器导出里 <c>H3</c> 可能在紧邻的 <c>&lt;DT&gt;</c>，
    /// 也可能与 <c>&lt;DL&gt;</c> 同在一个 <c>&lt;DT&gt;</c> 里，顺序不可靠 ⇒ 只认 <c>&lt;A&gt;</c> 的祖先链。
    /// </remarks>
    public BookmarkImportResult ImportHtml(string html, IReadOnlyList<BookmarkItem> existing)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        var known = new HashSet<string>(existing.Select(e => e.Url), StringComparer.OrdinalIgnoreCase);
        var added = new List<BookmarkItem>();
        var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var el in doc.QuerySelectorAll("a"))
        {
            var href = (el.GetAttribute("href") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(href)
                || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("place:", StringComparison.OrdinalIgnoreCase)) continue;
            if (known.Contains(href) || added.Any(x => x.Url.Equals(href, StringComparison.OrdinalIgnoreCase))) continue;

            var title = (el.TextContent ?? "").Trim();
            if (title.Length == 0) title = href;

            var folder = FolderPathOf(el);
            if (folder.Length > 0)
            {
                // 路径本身 + 全部祖先前缀都算「导入到的文件夹」（状态栏报的文件夹数 = 路径数）
                var prefix = "";
                foreach (var part in folder.Split('/'))
                {
                    prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                    folderSet.Add(prefix);
                }
            }

            added.Add(new BookmarkItem { Title = title, Url = href, Folder = folder });
            known.Add(href);
        }

        return new BookmarkImportResult { Added = added.Count, Folders = folderSet.Count, Items = added };
    }

    /// <summary>由书签的祖先链反推文件夹全路径（<c>/</c> 分隔，根级书签 = 空串）。</summary>
    private static string FolderPathOf(IElement anchor)
    {
        var names = new List<string>();
        for (var n = anchor.ParentElement; n != null; n = n.ParentElement)
        {
            if (n.LocalName != "dl") continue;
            var name = FolderNameOf(n);
            if (name.Length > 0) names.Insert(0, name);
        }
        return string.Join('/', names);
    }

    /// <summary><c>&lt;DL&gt;</c> 对应的文件夹名：同 <c>&lt;DT&gt;</c> 内的 <c>H3</c> 优先，否则回溯前面的兄弟节点。</summary>
    private static string FolderNameOf(IElement dl)
    {
        if (dl.ParentElement is { LocalName: "dt" } dt)
        {
            foreach (var c in dt.Children)
                if (c.LocalName == "h3") return (c.TextContent ?? "").Trim();
        }

        for (var prev = dl.PreviousElementSibling; prev != null; prev = prev.PreviousElementSibling)
        {
            if (prev.LocalName == "h3") return (prev.TextContent ?? "").Trim();
            foreach (var c in prev.Children)
                if (c.LocalName == "h3") return (c.TextContent ?? "").Trim();
            if (prev.LocalName is "dl" or "a") return "";   // 已越过上一个文件夹 ⇒ 本级无名字（根 DL）
        }
        return "";
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
