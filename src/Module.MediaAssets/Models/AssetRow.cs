using System;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 「资产列表」的一行 = **资产 + 它的主文件位置 + 组织信息**（标签 / 分类 / 集合的显示串）。
/// <para>
/// 为什么要单独一行模型：`assets` 表里**没有**文件名 / 大小 / 路径（一个资产可有多份位置，
/// 见 <see cref="FileLocation"/>），列表页却要显示它们 —— 由数据层一次查询拼好，
/// ⛔ 别在 VM 里对每个资产再查一次位置（N+1，500 行就是 500 次查询）。
/// </para>
/// <para>
/// 🔴 **列键 ≠ 属性名的列，必须在 <c>ViewModel</c> 的 <c>FieldText</c> 里显式映射**（v3.15 教训：
/// 基类靠**反射取同名属性**，取不到就返回 null → 该条筛选**静默失效**、不报错）：
/// <c>Tags</c>→<see cref="TagsText"/>、<c>Categories</c>→<see cref="CategoriesText"/>、
/// <c>Collections</c>→<see cref="CollectionsText"/>、<c>Modified</c>→<see cref="ModifiedText"/>、
/// <c>Storage</c>→<see cref="StorageName"/>。
/// </para>
/// </summary>
public partial class AssetRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>
    /// v3.34 / NT-018：**勾选列**的状态（「🏷 加标签」「🗂 加集合」按勾选批量操作）。
    /// ⚠️ 只是界面态，⛔ 不落库、⛔ 不参与筛选 / 排序 / 导入导出。
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isChecked;

    // ===== 资产本体 =====
    public long Id { get; set; }

    /// <summary>显示名：优先 <c>display_name</c> → <c>title</c> → 「资产 #id」（与 <see cref="Asset.EffectiveName"/> 同源）。</summary>
    public string Name { get; set; } = "";

    /// <summary>类型（<see cref="AssetTypes"/> 的原始值，如 <c>video</c>）—— 排序用它，显示用 <see cref="TypeText"/>。</summary>
    public string Type { get; set; } = AssetTypes.Other;

    /// <summary>状态（<see cref="AssetStatuses"/>）。</summary>
    public string Status { get; set; } = AssetStatuses.Active;

    public int Rating { get; set; }

    /// <summary>用户在库里改的显示名（<c>display_name</c>；可能为空 → 与 <see cref="Title"/> 分开存，详情卡要能改它）。</summary>
    public string? DisplayName { get; set; }

    /// <summary>扫描时由文件名推出的标题。</summary>
    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>最后更新时间（ISO 文本，来自 <c>assets.updated_at</c>）；回收站里 ≈ 软删除时刻。</summary>
    public string? UpdatedAt { get; set; }

    // ===== 视频元数据（来自 metadata_json；非视频 / 未扫描 → 默认空，列表列空而不崩） =====
    /// <summary>视频时长（秒，来自 <c>metadata_json.duration_seconds</c>）；排序用原始秒数。</summary>
    public double DurationSeconds { get; set; }

    /// <summary>分辨率文本（"1920×1080"，来自 <c>metadata_json.width/height</c>）；无则空串。</summary>
    public string ResolutionText { get; set; } = "";

    /// <summary>视频编码（"h264"，来自 <c>metadata_json.video_codec</c>）；无则空串。</summary>
    public string CodecText { get; set; } = "";

    // ===== 主文件位置 =====
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long Size { get; set; }

    /// <summary>文件最后修改时间（ISO 文本，来自 <see cref="FileLocation.Mtime"/>）。</summary>
    public string? Mtime { get; set; }

    /// <summary>位置状态（<see cref="LocationStatuses"/>）：文件被外部删掉 = <c>missing</c>。</summary>
    public string LocationStatus { get; set; } = LocationStatuses.Active;

    public long StorageId { get; set; }
    public string StorageName { get; set; } = "";

    // ===== 组织信息（逗号分隔的显示串） =====
    public string TagsText { get; set; } = "";
    public string CategoriesText { get; set; } = "";
    public string CollectionsText { get; set; } = "";

    // ===== 界面派生显示（⛔ 不落库） =====

    /// <summary>类型的中文显示名。</summary>
    public string TypeText => AssetTypeTexts.Of(Type);

    public string StatusText => Status switch
    {
        AssetStatuses.Active => "正常",
        AssetStatuses.Trashed => "回收站",
        AssetStatuses.Missing => "已丢失",
        AssetStatuses.Archived => "已归档",
        _ => Status
    };

    /// <summary>大小的可读串（1.2 GB / 3.4 MB …）。</summary>
    public string SizeText => FormatSize(Size);

    /// <summary>修改时间的显示串（拿不到就空串）。</summary>
    public string ModifiedText
    {
        get
        {
            if (string.IsNullOrEmpty(Mtime)) return "";
            return DateTime.TryParse(Mtime, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : Mtime;
        }
    }

    /// <summary>删除时间（≈软删除时刻）的显示串，拿不到就空串。</summary>
    public string UpdatedAtText
    {
        get
        {
            if (string.IsNullOrEmpty(UpdatedAt)) return "";
            return DateTime.TryParse(UpdatedAt, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : UpdatedAt;
        }
    }

    /// <summary>时长的人类可读串（HH:MM:SS / MM:SS；拿不到就空串）。</summary>
    public string DurationText => FormatDuration(DurationSeconds);

    /// <summary>行是否"文件已不在磁盘上"（列表里给它一个提示色）。</summary>
    public bool IsMissing => LocationStatus == LocationStatuses.Missing || Status == AssetStatuses.Missing;

    /// <summary>字节数 → 可读串（1024 进制，与资源管理器一致）。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "";
        if (bytes < 1024) return $"{bytes} B";
        string[] units = { "KB", "MB", "GB", "TB" };
        var v = bytes / 1024.0;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }

    /// <summary>秒数 → HH:MM:SS（≥1h，如 02:14:33）或 MM:SS（&lt;1h，如 14:33）；≤0 返回空串。</summary>
    public static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "";
        var ts = TimeSpan.FromSeconds(seconds);
        var h = (int)ts.TotalHours;
        return h >= 1
            ? $"{h:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}

/// <summary>资产类型 ↔ 中文显示名（列表 / 详情 / 筛选下拉共用；⛔ 别在各处各写一份）。</summary>
public static class AssetTypeTexts
{
    /// <summary>筛选下拉第一项（v3.40：文案由「全部」改为「全部类型」—— 与其他三个筛选框的「全部状态 / 全部存储源 / 全部标签」对齐）。</summary>
    public const string All = "全部类型";

    public static string Of(string type) => type switch
    {
        AssetTypes.Video => "视频",
        AssetTypes.Music => "音乐",
        AssetTypes.Book => "书籍",
        AssetTypes.Image => "图片",
        AssetTypes.Document => "文档",
        _ => "其它"
    };

    /// <summary>筛选下拉的候选项：全部 + 六种类型（值取 <see cref="AssetTypes"/>）。</summary>
    public static (string Label, string Value)[] Choices { get; } =
    {
        (All, ""),
        ("视频", AssetTypes.Video),
        ("音乐", AssetTypes.Music),
        ("书籍", AssetTypes.Book),
        ("图片", AssetTypes.Image),
        ("文档", AssetTypes.Document),
        ("其它", AssetTypes.Other)
    };
}
