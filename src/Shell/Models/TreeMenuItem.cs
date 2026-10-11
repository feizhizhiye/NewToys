using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shell.Models;

/// <summary>
/// 左侧树状菜单节点（v3.1）。
/// <para>结构 = 「首页（叶子）+ 一级分组（<see cref="IsGroup"/>=true，可展开）+ 二级功能（叶子）」。</para>
/// <para>一级分组点击 <b>只展开/折叠、不打开标签页</b>；叶子点击打开/激活对应标签页。</para>
/// </summary>
public partial class TreeMenuItem : ObservableObject
{
    /// <summary>菜单 key（叶子）或分组 key（一级）。首页为 <c>home</c>。</summary>
    public string Key { get; init; } = "";

    public string Title { get; init; } = "";

    /// <summary>图标文本（emoji 回退；一级分组已改走 <see cref="VectorIcon"/>）。</summary>
    public string Icon { get; init; } = "";

    /// <summary>矢量图标（v3.2）：一级菜单/首页用；为 null 时回退显示 <see cref="Icon"/> 文本。</summary>
    public Geometry? VectorIcon { get; init; }

    public bool HasVectorIcon => VectorIcon != null;

    /// <summary>是否为一级分组（可展开）。</summary>
    public bool IsGroup { get; init; }

    [ObservableProperty] private bool _isExpanded = true;

    [ObservableProperty] private bool _isActive;

    public ObservableCollection<TreeMenuItem> Children { get; } = new();

    /// <summary>展开指示符：分组用 ▾ / ▸，叶子为空。</summary>
    public string Glyph => IsGroup ? (IsExpanded ? "▾" : "▸") : " ";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Glyph));
}
