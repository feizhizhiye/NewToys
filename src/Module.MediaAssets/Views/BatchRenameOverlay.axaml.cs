using Avalonia.Controls;
using Avalonia.Input;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 批量重命名弹层（遮罩 + 居中卡片）的壳。
/// <para>点击遮罩 = 取消；卡片内的点击不会冒到这里（卡片是遮罩的兄弟节点）。</para>
/// </summary>
public partial class BatchRenameOverlay : UserControl
{
    public BatchRenameOverlay()
    {
        InitializeComponent();
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is BatchRenameViewModel vm && vm.CloseCommand.CanExecute(null))
            vm.CloseCommand.Execute(null);
    }
}
