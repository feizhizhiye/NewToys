using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 「节详情卡 → 显示列」里的一个 chip（NT-069）：点一下切这一列在当前视图里的显隐。
/// <para>由页面 VM 按节当前投影重建（<c>Title</c> 已是视图覆盖后的标题）。</para>
/// </summary>
public sealed partial class SectionColumnChip : ObservableObject
{
    /// <summary>列 key（= 输出属性名）。</summary>
    public string Key { get; init; } = "";

    /// <summary>chip 文案（视图覆盖后的列标题；详情卡「表头名称」改的就是它）。</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>当前是否显示。</summary>
    [ObservableProperty] private bool _visible;
}

/// <summary>
/// 信息面板顶部「集合 ▾」下拉里的一项（NT-068 §三.8）。
/// <para>下拉文案 = <c>名称 · 成员数</c>；排序 = ★置顶 → 最近使用 → 名称。</para>
/// </summary>
public sealed class InfoCollectionOption
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Icon { get; init; } = "";

    public int MemberCount { get; init; }

    public bool Pinned { get; init; }

    /// <summary>最近打开时间（ISO 8601；空 = 从未）。</summary>
    public string LastOpenedAt { get; init; } = "";

    public string Kind { get; init; } = ScriptCollectionKind.Info;

    /// <summary>下拉显示文案（<c>磁盘体检 · 3</c>）。</summary>
    public string Display => MemberCount > 0 ? $"{Name} · {MemberCount}" : Name;

    /// <summary>悬浮提示。</summary>
    public string Tip => $"{ScriptCollectionKind.Label(Kind)} · {MemberCount} 个成员"
                         + (Pinned ? " · ★ 置顶" : "");

    /// <summary>「最近使用」排序键（空时间排最后）。</summary>
    public DateTime LastOpenedUtc
        => DateTime.TryParse(LastOpenedAt, out var t) ? t : DateTime.MinValue;
}

/// <summary>信息面板「视图 ▾」下拉里的一项（同一集合的不同呈现）。</summary>
public sealed class InfoViewOption
{
    public InfoViewOption() { }

    public InfoViewOption(ScriptView view)
    {
        Id = view.Id;
        Name = string.IsNullOrWhiteSpace(view.Name) ? "默认" : view.Name;
        Layout = ScriptViewLayout.Normalize(view.Layout);
    }

    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Layout { get; init; } = ScriptViewLayout.Sectioned;

    public string Display => Layout == ScriptViewLayout.Merged ? Name + "（合并表）" : Name;
}

/// <summary>「⚙ 节设置」里的下拉项（解析器 / 保留策略 / 刷新方式）。</summary>
public sealed class InfoOption
{
    public InfoOption() { }

    public InfoOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    public string Value { get; init; } = "";

    public string Label { get; init; } = "";

    public override string ToString() => Label;
}
