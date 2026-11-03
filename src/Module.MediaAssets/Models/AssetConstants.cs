using System;

namespace NewToys.MediaAssets.Models;

/// <summary>资产类型（`assets.type`）：视频 / 音乐 / 书籍 / 图片 / 文档 / 其它。</summary>
public static class AssetTypes
{
    public const string Video = "video";
    public const string Music = "music";
    public const string Book = "book";
    public const string Image = "image";
    public const string Document = "document";
    public const string Other = "other";
}

/// <summary>资产状态（`assets.status`）。</summary>
public static class AssetStatuses
{
    public const string Active = "active";
    public const string Trashed = "trashed";
    public const string Missing = "missing";
    public const string Archived = "archived";
}

/// <summary>文件位置状态（`file_locations.status`）。</summary>
public static class LocationStatuses
{
    public const string Active = "active";
    public const string Missing = "missing";
    public const string Deleted = "deleted";
    public const string Moved = "moved";
    public const string Error = "error";
}

/// <summary>操作日志状态（`operation_logs.status`）：**物理操作先写 pending 再动磁盘**。</summary>
public static class OperationStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Success = "success";
    public const string Failed = "failed";
    public const string RolledBack = "rolled_back";
    public const string Cancelled = "cancelled";
}

/// <summary>扫描任务状态（`scan_jobs.status`）。</summary>
public static class ScanStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Success = "success";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>存储源类型（`storages.type`）。</summary>
public static class StorageTypes
{
    public const string Local = "local";
    public const string Removable = "removable";
    public const string Nas = "nas";
}

/// <summary>关联类型（`relations.relation_type`，用）。</summary>
public static class RelationTypes
{
    public const string SubtitleOf = "subtitle_of";
    public const string CoverOf = "cover_of";
    public const string SameWork = "same_work";
    public const string VersionOf = "version_of";
    public const string FormatOf = "format_of";
    public const string Reference = "reference";
    public const string RelatedTo = "related_to";
}

/// <summary>
/// 标签的**适用类型**（存 <c>tags.namespace</c> 列，取代原来的自由文本「命名空间」）。
/// <para>
/// 口径统一：标签只分「通用」+ 五种资产类型。详情卡按当前资产的类型只显示「通用 + 本类型」的标签，
/// 避免「视频标签」和「音乐标签」挤在一排互相干扰；标签管理页按类型排序分组。
/// </para>
/// <para>⚠️ 库里的历史值可能是任意文本（老的命名空间）⇒ 读出来原样显示，编辑时收敛到这 6 个值。</para>
/// </summary>
public static class TagTypes
{
    /// <summary>不限类型（默认）。</summary>
    public const string General = "通用";

    /// <summary>可选值（下拉顺序 = 这里的顺序）。</summary>
    public static readonly string[] Choices = { General, "视频", "音乐", "图片", "书籍", "文档" };

    /// <summary>资产类型 key → 标签类型文案。</summary>
    public static string OfAssetType(string? typeKey) => typeKey switch
    {
        AssetTypes.Video => "视频",
        AssetTypes.Music => "音乐",
        AssetTypes.Image => "图片",
        AssetTypes.Book => "书籍",
        AssetTypes.Document => "文档",
        _ => General
    };

    /// <summary>该标签是否适用于这个资产类型（通用恒适用）。</summary>
    public static bool AppliesTo(string? tagNamespace, string? assetTypeKey)
    {
        if (string.IsNullOrWhiteSpace(tagNamespace)) return true;
        var wanted = OfAssetType(assetTypeKey);
        return string.Equals(tagNamespace, General, StringComparison.OrdinalIgnoreCase)
               || string.Equals(tagNamespace, wanted, StringComparison.OrdinalIgnoreCase);
    }
}
