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

/// <summary>关联类型（`relations.relation_type`，NT-018 用）。</summary>
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
