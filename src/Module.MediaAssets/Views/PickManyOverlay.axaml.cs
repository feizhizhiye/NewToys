using Avalonia.Controls;
using Avalonia.Input;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>多选弹层（加标签 / 加集合）的壳：点遮罩 = 取消。</summary>
public partial class PickManyOverlay : UserControl
{
    public PickManyOverlay()
    {
        InitializeComponent();
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PickManyViewModel vm && vm.CloseCommand.CanExecute(null))
            vm.CloseCommand.Execute(null);
    }
}
