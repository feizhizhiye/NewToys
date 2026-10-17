using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 集合页<b>左栏</b>的一行（NT-068 §三.7）：集合本身 + 成员数等派生显示值。
/// </summary>
/// <remarks>
/// ⚠️ 名字叫 <c>CollectionRow</c> 而不是 <c>ScriptCollection</c> —— 后者是落盘模型（<c>script.json</c>），
/// 这个是界面行，带 <see cref="MemberCount"/> / <see cref="Pinned"/> 这类展示状态。
/// </remarks>
public partial class CollectionRow : ObservableObject
{
    public CollectionRow(ScriptCollection c)
    {
        Id = c.Id;
        _name = c.Name;
        _kind = ScriptCollectionKind.Normalize(c.Kind);
        _icon = c.Icon;
        _description = c.Description;
        _pinned = c.Pinned;
        _archived = c.Archived;
        Builtin = c.Builtin;
        _lastOpenedAt = c.LastOpenedAt ?? "";
        Tags = c.Tags ?? new List<string>();
    }

    /// <summary>集合 id（<c>script.json</c> 里的稳定标识）。</summary>
    public string Id { get; }

    [ObservableProperty] private string _name = "";

    [ObservableProperty] private string _kind = ScriptCollectionKind.Info;

    [ObservableProperty] private string _icon = "";

    [ObservableProperty] private string _description = "";

    [ObservableProperty] private bool _pinned;

    [ObservableProperty] private bool _archived;

    /// <summary>内置集合：可改名、⛔ 不可删。</summary>
    public bool Builtin { get; }

    [ObservableProperty] private string _lastOpenedAt = "";

    public List<string> Tags { get; }

    /// <summary>成员数（打开集合时由页面算一次）。</summary>
    [ObservableProperty] private int _memberCount;

    /// <summary>kind 的中文名（信息面板 / 脚本库 / 命令库 / 软件包）。</summary>
    public string KindText => ScriptCollectionKind.Label(Kind);

    /// <summary>置顶标记（★ / 空）。</summary>
    public string PinMark => Pinned ? "★" : "";

    /// <summary>左栏副标题：`kind · 3 个成员`。</summary>
    public string SubText => $"{KindText} · {MemberCount}";

    /// <summary>把界面上的改动写回落盘模型（改名 / 图标 / 描述 / 置顶 / 归档 / 排序）。</summary>
    public void ApplyTo(ScriptCollection c)
    {
        c.Name = Name;
        c.Kind = ScriptCollectionKind.Normalize(Kind);
        c.Icon = Icon;
        c.Description = Description;
        c.Pinned = Pinned;
        c.Archived = Archived;
        c.Tags = Tags;
    }

    partial void OnPinnedChanged(bool value) => OnPropertyChanged(nameof(PinMark));
    partial void OnKindChanged(string value) => OnPropertyChanged(nameof(KindText));
    partial void OnMemberCountChanged(int value) => OnPropertyChanged(nameof(SubText));
}

/// <summary>
/// 集合页<b>右栏</b>的一行 = 全库条目 + 「是否属于当前集合」的勾选（NT-068 §三.7）。
/// </summary>
/// <remarks>
/// 布局是「全库清单 + 勾选」而不是「先挑条目再加」：勾 = 在集合内，取消勾 = 移出，
/// 即时生效、无保存按钮（卡 §三.7）。
/// </remarks>
public partial class MemberRow : ObservableObject
{
    public MemberRow(ScriptItem item, bool isMember)
    {
        ItemId = item.Id;
        _title = item.Title;
        Role = ScriptItemRole.Normalize(item.Role);
        Target = item.Target ?? "";
        Collectable = item.Collectable;
        Reason = item.IneligibleReason ?? "";
        _isMember = isMember;
    }

    public string ItemId { get; }

    [ObservableProperty] private string _title = "";

    /// <summary>条目角色（script / command / collector / package）。</summary>
    public string Role { get; }

    public string Target { get; }

    /// <summary>可采集（不可采集的行在「显示不可采集」打开后灰显不可勾）。</summary>
    public bool Collectable { get; }

    public string Reason { get; }

    [ObservableProperty] private bool _isMember;

    /// <summary>角色中文名。</summary>
    public string RoleText => Role switch
    {
        ScriptItemRole.Command => "命令",
        ScriptItemRole.Collector => "采集器",
        ScriptItemRole.Package => "软件包",
        _ => "脚本",
    };

    /// <summary>勾选框是否可操作：不可采集的行只能在「显示不可采集」下看到，但⛔ 勾不上。</summary>
    public bool CanToggle => Collectable;
}

/// <summary>筛选下拉的一个选项（左栏的「分组 / 类型 / 标签」共用）。</summary>
public class CollectionFilterOption
{
    public string Label { get; set; } = "";

    /// <summary>筛选值（空串 = 不限）。</summary>
    public string Value { get; set; } = "";
}
