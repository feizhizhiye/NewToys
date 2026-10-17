using Avalonia.Controls;

namespace NewToys.Shell.Views;

/// <summary>
/// 「更新」节（v3.66 / NT-062 第五轮）：从原「通用」节拆出，DataContext = 共用的 <c>GeneralViewModel</c>。
/// </summary>
public partial class UpdateView : UserControl
{
    public UpdateView() => InitializeComponent();
}
