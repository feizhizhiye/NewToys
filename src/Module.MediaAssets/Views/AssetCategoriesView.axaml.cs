using Avalonia.Controls;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「分类管理」页 code-behind：选中 / 改挂 / 改名全在 VM；
/// 表格的排序 / 列宽 / 右键菜单 DataContext 由 <c>TableViewHost</c> 自理。
/// </summary>
public partial class AssetCategoriesView : UserControl
{
    public AssetCategoriesView()
    {
        InitializeComponent();
    }
}
