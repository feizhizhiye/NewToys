using System;
using System.Collections.Generic;

namespace NewToys.WebTools.Models;

/// <summary>一条书签：标题 / 网址 / 分组 / 备注，外加「批量打开」开关与最近一次联通检测结果。</summary>
public class BookmarkItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>分组（对应浏览器收藏夹的文件夹；空 = 未分组）。</summary>
    public string Folder { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>是否加入「批量打开」（右键或在详情勾选 → 一次打开全部）。</summary>
    public bool IsBatchOpen { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CheckedAt { get; set; }
    /// <summary>最近一次联通检测结果（"200" / "404" / "超时" / 异常信息）；未检测为 null。</summary>
    public string? CheckStatus { get; set; }
    /// <summary>检测是否成功（仅用于界面着色）。</summary>
    public bool CheckOk { get; set; }
}

/// <summary>书签导入结果（供状态栏显示）。</summary>
public class BookmarkImportResult
{
    public int Added { get; init; }
    public int Folders { get; init; }
    public List<BookmarkItem> Items { get; init; } = new();
}
