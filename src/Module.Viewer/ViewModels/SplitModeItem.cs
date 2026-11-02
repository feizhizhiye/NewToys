using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Viewer.ViewModels;

/// <summary>
/// 工具条上的一档分屏（按钮组 <c>ChipGroup</c> 的数据项）。
/// <para>文案就是图标字形，高亮跟随 <see cref="IsSelected"/>（宿主按它切 <c>src-btn.selected</c> 类）。</para>
/// </summary>
public partial class SplitModeItem : ObservableObject
{
    /// <summary>按钮上的字形（▭ 单张 / ◫ 左右 / ⊟ 上下 / ▦ 2×2）。</summary>
    public string Label { get; init; } = "";

    /// <summary>一屏行数。</summary>
    public int Rows { get; init; } = 1;

    /// <summary>一屏列数。</summary>
    public int Cols { get; init; } = 1;

    [ObservableProperty] private bool _isSelected;
}
