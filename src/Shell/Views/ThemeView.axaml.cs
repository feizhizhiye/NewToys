using Avalonia.Controls;

namespace NewToys.Shell.Views;

/// <summary>
/// 「主题外观」设置节：从「通用」节拆出，DataContext = 共用的 <c>GeneralViewModel</c>。
/// </summary>
// lint:view-no-vm —— 设置节子视图：DataContext 由父视图/节宿主注入，没有自己的 ViewModel
public partial class ThemeView : UserControl
{
    public ThemeView() => InitializeComponent();
}
