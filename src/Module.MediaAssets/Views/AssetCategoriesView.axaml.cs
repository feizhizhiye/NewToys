using Avalonia.Controls;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「分类管理」页 code-behind：树的展开 / 选中 / 成员加载全在 VM（⛔ 不用 TreeView，所以没有树事件要接线）；
/// 成员表格的排序 / 列宽 / 右键菜单 DataContext 由 <c>TableViewHost</c> 自理。
/// </summary>
public partial class AssetCategoriesView : UserControl
{
    public AssetCategoriesView()
    {
        InitializeComponent();
    }
}
