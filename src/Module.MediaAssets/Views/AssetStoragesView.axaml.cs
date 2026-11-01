using Avalonia.Controls;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「存储源」页 code-behind：业务逻辑全在 VM（目录选择走 DialogHooks）；
/// 表格的排序 / 列宽 / 右键菜单 DataContext 由 <c>TableViewHost</c> 自理。
/// </summary>
public partial class AssetStoragesView : UserControl
{
    public AssetStoragesView()
    {
        InitializeComponent();
    }
}
