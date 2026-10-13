using System;
using Avalonia.Media;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 集合管理页的一行（NT-018）：`collections` + 成员数。
/// <para>手动集合（<c>manual</c>）成员靠人和拖拽排序；智能集合（<c>smart</c>）成员由 <c>rule_json</c> 规则筛出来。</para>
/// </summary>
public partial class CollectionRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary><c>manual</c> / <c>smart</c>。</summary>
    public string Type { get; set; } = CollectionTypes.Manual;

    public string? RuleJson { get; set; }
    public int MemberCount { get; set; }
    public string CreatedAt { get; set; } = "";

    // ===== 界面派生（⛔ 不落库） =====

    public bool IsSmart => string.Equals(Type, CollectionTypes.Smart, StringComparison.Ordinal);

    /// <summary>图标：智能集合与手动集合要一眼能分开（需求 §6.6）。</summary>
    public string TypeIcon => IsSmart ? "⚡" : "📦";

    public string TypeText => IsSmart ? "智能" : "手动";

    public string MemberText => MemberCount == 0 ? "（空）" : $"{MemberCount} 个";

    /// <summary>左侧列表的色条（数据色板，按名字稳定取；⛔ 全名限定 `Avalonia.Media.Color` 防遮蔽）。</summary>
    public IBrush AccentBrush => new SolidColorBrush(Avalonia.Media.Color.Parse(AssetPalette.For(Name)));

    public string CreatedText
        => DateTime.TryParse(CreatedAt, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : CreatedAt;

    /// <summary>当前选中（左列表高亮；`Classes.selected` 绑它）。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isSelected;
}

/// <summary>集合类型（`collections.type`）。</summary>
public static class CollectionTypes
{
    /// <summary>手动：成员由用户增删、可拖拽排序（写 `collection_assets.sort_order`）。</summary>
    public const string Manual = "manual";

    /// <summary>智能：成员由 `rule_json` 规则筛出来（只读，⛔ 不允许手工增删）。</summary>
    public const string Smart = "smart";
}
