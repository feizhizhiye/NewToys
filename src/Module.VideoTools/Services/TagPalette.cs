using System;
using Avalonia.Media;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 🏷 标签色板（v3.20，需求 §6.4.6「每个标签预设一种颜色」）。
/// <para>
/// 每个标签按**名字稳定哈希**映射到固定 8 色里的一种 —— 同一个标签在任何页面
/// （列表列 / 详情卡 / 收藏页卡片 / 标签管理弹层）颜色一致；换名字 = 换颜色。
/// </para>
/// <para>
/// ⚠️ 这是**数据色板**，不是主题色：与 <c>Shared/Services/AccentPalette.cs</c> 同属
/// 「代码里的单一色源」（色值只准出现在本文件，⛔ 不准在 XAML / code-behind 散落写死）。
/// 8 色都取中等饱和度，明暗主题下作文字 / 描边都可辨；底色统一给 20% 透明版。
/// </para>
/// </summary>
public static class TagPalette
{
    /// <summary>8 个预设色（玫瑰 / 绿 / 蓝 / 琥珀 / 紫 / 珊瑚 / 青 / 灰）。</summary>
    private static readonly string[] Hex =
    {
        "#E06C9F", "#5FB878", "#5B9BE8", "#E8A33D",
        "#9B7FE0", "#E0705F", "#3FB6B2", "#98A2B3"
    };

    /// <summary>标签主色（文字 / 描边用）。</summary>
    public static Color ColorOf(string? tag)
    {
        var idx = string.IsNullOrEmpty(tag) ? Hex.Length - 1 : StableIndex(tag!);
        return Color.Parse(Hex[idx]);
    }

    /// <summary>标签主色笔刷。</summary>
    public static SolidColorBrush BrushOf(string? tag) => new(ColorOf(tag));

    /// <summary>chip 淡底（主色 20% 透明）。</summary>
    public static SolidColorBrush SoftBrushOf(string? tag)
    {
        var c = ColorOf(tag);
        return new SolidColorBrush(new Color(0x33, c.R, c.G, c.B));
    }

    /// <summary>「+N」溢出灰片的主色。</summary>
    public static SolidColorBrush OverflowBrush => new(Color.Parse(Hex[^1]));

    /// <summary>「+N」溢出灰片的淡底。</summary>
    public static SolidColorBrush OverflowSoftBrush
    {
        get
        {
            var c = Color.Parse(Hex[^1]);
            return new SolidColorBrush(new Color(0x33, c.R, c.G, c.B));
        }
    }

    /// <summary>FNV-1a 稳定哈希（⛔ 不用 string.GetHashCode：跨进程不稳定）。</summary>
    private static int StableIndex(string tag)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var ch in tag)
            {
                h ^= (uint)ch;
                h *= 16777619;
            }
            return (int)(h % (uint)Hex.Length);
        }
    }
}
