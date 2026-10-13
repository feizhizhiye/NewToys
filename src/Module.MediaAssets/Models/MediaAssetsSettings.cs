using System;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 媒体资产模块设置（NT-021）：落 <c>&lt;应用根&gt;\config\media-assets.json</c>。
/// <para>
/// ⚠️ 模型放在**本模块**而不是 Shared：读者（扫描 / 元数据 / 缩略图服务）与写者（本模块设置页）
/// 都在 <c>Module.MediaAssets</c> 内，没有跨模块共用 → 按规范 §3.4「被两模块共用时才放 Shared」。
/// </para>
/// </summary>
public class MediaAssetsSettings
{
    // ==================== 扫描 ====================

    /// <summary>忽略隐藏文件 / 目录（文件属性 Hidden）。</summary>
    public bool IgnoreHiddenFiles { get; set; } = true;

    /// <summary>忽略临时文件（<c>.tmp</c> / <c>.part</c> / <c>~$*</c> 等 Office 临时副本）。</summary>
    public bool IgnoreTempFiles { get; set; } = true;

    /// <summary>忽略的扩展名（逗号 / 分号分隔；扫描时跳过）。</summary>
    public string IgnoreExtensions { get; set; } = ".tmp, .part, .crdownload, .partial, .lnk, .ini, .db, .db-wal, .db-shm";

    /// <summary>忽略的目录名（逗号 / 分号分隔；按目录名匹配，⛔ 不是路径片段）。</summary>
    public string IgnoreDirectories { get; set; } = ".git, .svn, .hg, node_modules, $RECYCLE.BIN, System Volume Information, .cache, AppData, .thumbnails";

    /// <summary>quick_hash 采样大小（MB）：越大越准、扫描越慢。</summary>
    public int QuickHashSampleMb { get; set; } = 1;

    // ==================== 缩略图 ====================

    /// <summary>视频抽帧位置模式：<c>percent</c>（按时长百分比）/ <c>seconds</c>（固定秒数）。</summary>
    public string ThumbFrameMode { get; set; } = "percent";

    /// <summary>抽帧位置（百分比，0~100；<see cref="ThumbFrameMode"/> = percent 时用）。</summary>
    public double ThumbFramePercent { get; set; } = 25;

    /// <summary>抽帧位置（秒；<see cref="ThumbFrameMode"/> = seconds 时用）。</summary>
    public double ThumbFrameSeconds { get; set; } = 5;

    /// <summary>图片 / 抽帧的缩略图尺寸（长边像素；只缩不放）。</summary>
    public int ImageThumbSize { get; set; } = 320;

    /// <summary>缩略图目录（空 = <c>&lt;应用根&gt;\data\thumbs</c>）。</summary>
    public string ThumbsDirectory { get; set; } = "";

    // ==================== 命名模板 + 冲突策略（命名功能落地时启用） ====================

    /// <summary>命名模板占位符：<c>{title}</c> / <c>{name}</c> / <c>{year}</c> / <c>{ext}</c>。</summary>
    public string NameTemplateVideo { get; set; } = "{title}";

    public string NameTemplateMusic { get; set; } = "{title}";
    public string NameTemplateBook { get; set; } = "{title}";
    public string NameTemplateImage { get; set; } = "{title}";

    /// <summary>重命名冲突策略：<c>append</c>（追加序号）/ <c>hash</c>（短哈希）/ <c>skip</c>（跳过）/ <c>overwrite</c>（覆盖，需确认）。</summary>
    public string ConflictPolicy { get; set; } = "append";

    // ==================== 回收站 ====================

    /// <summary>回收站保留天数（超过即「过期」）。</summary>
    public int TrashRetentionDays { get; set; } = 30;

    /// <summary>到期自动清理（开启后：启动时 + 手动点「🧹 立即清理」都会把过期资产永久删除）。默认关（破坏性操作要用户显式开）。</summary>
    public bool AutoCleanupTrash { get; set; }

    // ==================== 物理操作 ====================

    /// <summary>删除默认送系统回收站（关 = 直接永久删除，⛔ 危险）。</summary>
    public bool DeleteToRecycleBin { get; set; } = true;

    /// <summary>破坏性物理操作前二次确认（强制开；设置页置灰）。</summary>
    public bool ConfirmBeforeOperation { get; set; } = true;

    // ==================== 元数据 / 缩略图 ====================

    /// <summary>扫描到新文件时自动提取元数据（视频走 ffprobe；图片读尺寸）。</summary>
    public bool AutoExtractMetadata { get; set; } = true;

    /// <summary>扫描到新文件时自动生成缩略图（视频抽帧 / 图片缩放）。</summary>
    public bool AutoGenerateThumbnails { get; set; } = true;

    // ==================== 工具方法（⛔ 不落盘） ====================

    /// <summary>把逗号 / 分号分隔的列表解析成 HashSet（去空 / 去首尾空白 / 忽略大小写）。</summary>
    public static HashSet<string> ParseList(string? text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return set;
        foreach (var part in text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var v = part.Trim();
            if (v.Length > 0) set.Add(v);
        }
        return set;
    }

    /// <summary>扩展名归一化：确保带前导点（<c>tmp</c> → <c>.tmp</c>）。</summary>
    public static string NormalizeExtension(string ext)
    {
        var v = (ext ?? "").Trim();
        if (v.Length == 0) return "";
        return v.StartsWith(".", StringComparison.Ordinal) ? v : "." + v;
    }
}
