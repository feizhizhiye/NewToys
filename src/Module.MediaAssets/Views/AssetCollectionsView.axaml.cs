using Avalonia.Controls;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「集合管理」页 code-behind：集合选择、成员排序全在 VM；
/// 成员表格的排序 / 列宽 / 右键菜单 DataContext 由 <c>TableViewHost</c> 自理。
/// </summary>
public partial class AssetCollectionsView : UserControl
{
    public AssetCollectionsView()
    {
        InitializeComponent();
    }
}
