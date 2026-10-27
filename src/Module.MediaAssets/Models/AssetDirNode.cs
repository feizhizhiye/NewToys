using System.Collections.Generic;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 「资产浏览」左树的节点：一级 = 存储源，往下 = 该源里的目录层级（数据来自 <c>file_locations.relative_path</c> 聚合）。
/// <para>
/// 🔴 树是**自绘**（<c>ItemsControl</c> + 按钮行，按 <see cref="Depth"/> 扁平化）：⛔ 不用 <c>TreeView</c>，
/// 与分类页同一套路。
/// </para>
/// <para>
/// 存储源节点（<see cref="IsStorageRoot"/>）只是**分组抬头**：点它只展开 / 折叠，不改右侧内容；
/// 直接落在源根下的文件挂在子节点「（根目录）」（<see cref="DirPath"/> = 空串）。
/// </para>
/// </summary>
public partial class AssetDirNode : ObservableObject
{
    public long StorageId { get; init; }

    /// <summary>节点显示名（存储源名 / 目录名 /「（根目录）」）。</summary>
    public string Name { get; init; } = "";

    /// <summary>相对所属存储源根目录的目录路径（<c>\</c> 分隔；空串 = 源根目录）。</summary>
    public string DirPath { get; init; } = "";

    /// <summary>存储源节点（分组抬头，自身不承载文件）。</summary>
    public bool IsStorageRoot { get; init; }

    /// <summary>存储源在线状态（离线源的节点标灰提示）。</summary>
    public bool Online { get; init; } = true;

    public string ToolTipText { get; init; } = "";

    /// <summary>直接落在这个目录里的位置数。</summary>
    public int FileCount { get; set; }

    /// <summary>本目录 + 所有子孙目录的位置数（节点上显示的就是它）。</summary>
    public int TotalCount { get; set; }

    /// <summary>子节点（存储源节点非空 / 目录节点可空）。</summary>
    public List<AssetDirNode> Children { get; } = new();

    /// <summary>树深度（0 = 存储源）；界面按它换算左缩进。</summary>
    public int Depth { get; set; }

    [ObservableProperty] private bool _isSelected;

    [ObservableProperty] private bool _isExpanded = true;

    public bool HasChildren => Children.Count > 0;

    /// <summary>展开箭头（自绘）。</summary>
    public string Arrow => !HasChildren ? "　" : (IsExpanded ? "▾" : "▸");

    /// <summary>缩进（每层 14px，与分类页一致）。</summary>
    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    /// <summary>计数文案（存储源抬头不显示）。</summary>
    public string CountText => IsStorageRoot || TotalCount <= 0 ? "" : TotalCount.ToString();

    /// <summary>离线存储源的后缀提示（在线 = 空串）。</summary>
    public string OfflineText => IsStorageRoot && !Online ? "（离线）" : "";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Arrow));
}
