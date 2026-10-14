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

    /// <summary>图标文本（历史字段：v3.2 前用 emoji 占位；一级图标已全走 <see cref="VectorIcon"/>）。</summary>
    /// <remarks>
    /// ⚠️ 当前**不参与渲染**：NT-045 删掉了视图里的 emoji 回退分支，NT-046 又去掉了行首展开箭头，
    /// 一/二级行的图标位置都只认 <see cref="VectorIcon"/>。保留字段是因为模块注册仍带 icon 值
    /// （数据驱动图标属技术债，见《工程规范》§2 末条）。
    /// </remarks>
    public string Icon { get; init; } = "";

    /// <summary>矢量图标（v3.2）：一级菜单/首页用；为 null 时不显示图标（已无文本回退）。</summary>
    public Geometry? VectorIcon { get; init; }

    public bool HasVectorIcon => VectorIcon != null;

    /// <summary>是否为一级分组（可展开、点击只展开/折叠不打开标签页）。</summary>
    public bool IsGroup { get; init; }

    [ObservableProperty] private bool _isExpanded = true;

    [ObservableProperty] private bool _isActive;

    public ObservableCollection<TreeMenuItem> Children { get; } = new();
}
