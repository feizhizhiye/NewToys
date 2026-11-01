using Avalonia.Controls;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「标签管理」页 code-behind：列表交互（表头排序 / 列宽拖拽 / 右键菜单 DataContext）已收进
/// <c>NewToys.UI.Controls.TableViewHost</c>，本页无需再接线。
/// </summary>
public partial class AssetTagsView : UserControl
{
    public AssetTagsView()
    {
        InitializeComponent();
    }
}
