using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 工具条按钮组 / 单选组的一项（视图模式、图标尺寸档位、排序预设共用）。
/// <para>形状与 Shell 的 <c>ToolbarItemKind.ChipGroup</c> 对齐：<see cref="Label"/> 显示、
/// <see cref="IsSelected"/> 高亮（双向绑数据项）、点击把**数据项本身**当命令参数。</para>
/// </summary>
public partial class ChoiceItem : ObservableObject
{
    public ChoiceItem(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>面包屑的一段（点一下跳到该级目录；<see cref="Node"/> 为空 = 不可跳转）。</summary>
public sealed class BrowseCrumb
{
    public BrowseCrumb(string label, AssetDirNode? node)
    {
        Label = label;
        Node = node;
    }

    public string Label { get; }

    public AssetDirNode? Node { get; }

    public bool IsNavigable => Node != null;
}
