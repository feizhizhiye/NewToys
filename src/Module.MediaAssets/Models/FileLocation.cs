namespace NewToys.MediaAssets.Models;

/// <summary>
/// 文件位置一行（`file_locations`）：一个资产可有多份拷贝 / 多个路径。
/// <para>
/// 匹配（需求 §6.6 核心流程）按 ① <see cref="StorageId"/>+<see cref="RelativePath"/> →
/// ② <see cref="Hash"/>+<see cref="Size"/> → ③ <see cref="Inode"/>+<see cref="Device"/>（仅本地）依次进行。
/// </para>
/// </summary>
public class FileLocation
{
    public long Id { get; set; }
    public long AssetId { get; set; }
    public long StorageId { get; set; }

    /// <summary>绝对路径（落库用 <see cref="System.IO.Path.GetFullPath"/> 规范化后的结果）。</summary>
    public string Path { get; set; } = "";

    /// <summary>相对所属存储源根目录的路径（**同存储源内匹配的主键之一**）。</summary>
    public string? RelativePath { get; set; }

    public string FileName { get; set; } = "";
    public string? Ext { get; set; }
    public string? MimeType { get; set; }

    /// <summary>字节数。</summary>
    public long Size { get; set; }

    /// <summary>最后修改时间（ISO 8601 文本）。</summary>
    public string? Mtime { get; set; }

    /// <summary>完整内容哈希（无匹配时才计算；前缀 <c>sha1:</c>）。</summary>
    public string? Hash { get; set; }

    /// <summary>快速哈希 = 前 N MB + size + mtime（扫描主力，避免全量读盘）。</summary>
    public string? QuickHash { get; set; }

    /// <summary>文件系统 inode（仅本地存储源，用于改名 / 移动识别）。</summary>
    public string? Inode { get; set; }

    /// <summary>所在设备号（与 <see cref="Inode"/> 配对防跨卷误判）。</summary>
    public string? Device { get; set; }

    /// <summary>是否该资产的主位置（详情卡 / 打开文件优先用它）。</summary>
    public bool IsPrimary { get; set; }

    /// <summary>位置状态，取 <see cref="LocationStatuses"/>。</summary>
    public string Status { get; set; } = LocationStatuses.Active;

    /// <summary>最后一次扫描见到它的时间（判 missing 的依据）。</summary>
    public string? LastSeenAt { get; set; }

    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
