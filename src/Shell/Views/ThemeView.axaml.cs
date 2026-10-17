using Avalonia.Controls;

namespace NewToys.Shell.Views;

/// <summary>
/// 「主题外观」节（v3.66 / NT-062 第五轮）：从原「通用」节拆出，DataContext = 共用的 <c>GeneralViewModel</c>。
/// </summary>
public partial class ThemeView : UserControl
{
    public ThemeView() => InitializeComponent();
}
