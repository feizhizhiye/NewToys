using System;
using System.Collections.Generic;
using System.Linq;

namespace NewToys.Shared.Services;

/// <summary>
/// 一个强调色预设。<see cref="Color"/> / <see cref="HoverColor"/> 均为 <c>#RRGGBB</c> 文本，
/// 界面侧自行转成画刷（本文件属 Core/Shared，**不引用 Avalonia**）。
/// </summary>
public sealed record AccentPreset(string Id, string Name, string Color, string HoverColor);

/// <summary>
/// 强调色预设表 —— **Shell 的资源注入与主题页的色卡列表共用这一份**，不要各写一套：
/// - Shell：<c>App.ApplyTheme()</c> 用它填 <c>AccentBrush</c> / <c>AccentHoverBrush</c>，并派生选中态色；
/// - 主题页：通用设置「主题外观」节的 <c>GeneralViewModel</c>（NT-051 合并后）直接把它当作色卡数据源。
/// </summary>
public static class AccentPalette
{
    /// <summary>默认预设（首次启动 / 配置损坏时用）。</summary>
    public static AccentPreset Default => All[0];

    public static IReadOnlyList<AccentPreset> All { get; } = new List<AccentPreset>
    {
        new("wechat", "微信绿", "#07C160", "#06AD56"),
        new("blue",   "天青蓝", "#1E88E5", "#1976D2"),
        new("teal",   "青碧",   "#00ACC1", "#00838F"),
        new("purple", "紫罗兰", "#7C4DFF", "#651FFF"),
        new("orange", "落日橙", "#FF8F00", "#F57C00"),
        new("red",    "告警红", "#E53935", "#C62828"),
    };

    /// <summary>按 id 找预设，找不到（含 null / 空 / 旧配置里的废弃 id）一律回退默认。</summary>
    public static AccentPreset Find(string? id)
        => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
