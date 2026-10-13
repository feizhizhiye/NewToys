using Avalonia.Media;

namespace NewToys.VideoTools.Models;

/// <summary>
/// 🏷 标签色片（v3.20，需求 §6.4.6）：列表「标签」列 / 详情卡 / 收藏页卡片 / 标签管理弹层共用。
/// <para>颜色来自 <see cref="Services.TagPalette"/>（数据色板，⛔ 不写死在这里）。</para>
/// </summary>
public sealed class TagChip
{
    /// <summary>标签名（「+N」= 溢出计数灰片）。</summary>
    public string Name { get; set; } = "";

    /// <summary>主色笔刷（绑 chip 文字 + 描边）。</summary>
    public SolidColorBrush Brush { get; set; } = null!;

    /// <summary>淡底笔刷（绑 chip 背景）。</summary>
    public SolidColorBrush SoftBrush { get; set; } = null!;

    /// <summary>建一个普通标签色片。</summary>
    public static TagChip Of(string name) => new()
    {
        Name = name,
        Brush = Services.TagPalette.BrushOf(name),
        SoftBrush = Services.TagPalette.SoftBrushOf(name)
    };

    /// <summary>建「+N」溢出灰片（标签太多，列宽放不下）。</summary>
    public static TagChip Overflow(int extra) => new()
    {
        Name = "+" + extra,
        Brush = Services.TagPalette.OverflowBrush,
        SoftBrush = Services.TagPalette.OverflowSoftBrush
    };
}

/// <summary>
/// 标签管理弹层的一行（名称 + 使用数 + 色点笔刷）。
/// <para>v3.20 从 <c>VideoListViewModel</c> 嵌套类提到 Models —— 收藏页弹层也要用。</para>
/// </summary>
public sealed class TagManagerItem
{
    public string Name { get; set; } = "";
    public int Count { get; set; }

    private Avalonia.Media.IBrush? _dotBrush;

    /// <summary>行首色点的颜色（与列表 / 详情卡 chip 同源同色）。</summary>
    public Avalonia.Media.IBrush DotBrush => _dotBrush ??= Services.TagPalette.BrushOf(Name);

    public string CountDisplay => Count > 0 ? $"{Count} 个视频" : "尚未使用";
}
