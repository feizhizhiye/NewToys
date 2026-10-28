using System.Collections.Generic;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 「资产浏览」左树的节点 = 磁盘上的一个**绝对目录**（资源管理器形态：盘符 → 逐层目录）。
/// <para>
/// 🔴 树是**自绘**（<c>ListBox</c> + 按钮行，按 <see cref="Depth"/> 扁平化）：⛔ 不用 <c>TreeView</c>，
/// 与分类页同一套路。
/// </para>
/// <para>
/// 🔴 **子目录懒加载**：节点首次展开才枚举磁盘（<see cref="IsLoaded"/>），⛔ 不在进页面时递归扫全盘。
/// <see cref="InLibrary"/> 只看路径是否落在某个已登记存储源之内 —— 未入库目录照样可展开浏览，只是标灰。
/// </para>
/// </summary>
public partial class AssetDirNode : ObservableObject
{
    /// <summary>绝对目录路径（建树 / 分桶 / 持久化的唯一键，比较时忽略大小写）。</summary>
    public string AbsPath { get; init; } = "";

    /// <summary>节点显示名（最后一段目录名；盘符根 = <c>D:\</c>）。</summary>
    public string Name { get; init; } = "";

    /// <summary>悬浮提示（完整绝对路径）。</summary>
    public string ToolTipText { get; init; } = "";

    /// <summary>路径是否落在某个已登记存储源之内（false ⇒ 节点标灰；仍可正常浏览 / 展开）。</summary>
    public bool InLibrary { get; set; } = true;

    /// <summary>是否成功枚举过子目录（枚举抛权限 / IO 异常 ⇒ true 且 <see cref="Children"/> 为空）。</summary>
    public bool IsLoaded { get; set; }

    /// <summary>本目录可读（false ⇒ 显示「（无法访问）」，⛔ 不让用户白点）。</summary>
    public bool IsAccessible { get; set; } = true;

    /// <summary>本目录 + 所有子孙目录里**已入库**的文件数（0 ⇒ 不显示计数）。</summary>
    public int TotalCount { get; set; }

    /// <summary>子节点（懒加载后填充；未加载时为空集合）。</summary>
    public List<AssetDirNode> Children { get; } = new();

    /// <summary>树深度（0 = 盘符 / 共享根）；界面按它换算左缩进。</summary>
    public int Depth { get; set; }

    [ObservableProperty] private bool _isSelected;

    /// <summary>是否展开（默认折叠；展开会触发子目录枚举）。</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>还有没展开的东西：已加载看子节点数，未加载看可读性（未知 ⇒ 先给箭头）。</summary>
    public bool HasChildren => Children.Count > 0 || (!IsLoaded && IsAccessible);

    /// <summary>展开箭头（自绘）。</summary>
    public string Arrow => !HasChildren ? "　" : (IsExpanded ? "▾" : "▸");

    /// <summary>缩进（每层 14px，与分类页一致）。</summary>
    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    /// <summary>计数文案（没有内容时不显示）。</summary>
    public string CountText => TotalCount <= 0 ? "" : TotalCount.ToString();

    /// <summary>不可读目录的后缀提示（可读 = 空串）。</summary>
    public string HintText => IsAccessible ? "" : "（无法访问）";

    /// <summary>未入库目录整行标灰（仍可点 —— 「置灰但能用」是用户明确要的语义）。</summary>
    public double RowOpacity => InLibrary ? 1.0 : 0.55;

    /// <summary>子目录枚举完成 / 展开态变化后，把派生属性一次性通知出去。</summary>
    public void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasChildren));
        OnPropertyChanged(nameof(Arrow));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(RowOpacity));
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Arrow));
}
