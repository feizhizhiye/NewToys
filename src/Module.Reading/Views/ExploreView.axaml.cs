using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>发现页。唯一的控件级逻辑：双击一本书 = 去搜索页读。</summary>
public partial class ExploreView : UserControl
{
    public ExploreView()
    {
        InitializeComponent();
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ExploreViewModel vm) return;
        if (vm.ReadCommand.CanExecute(null)) vm.ReadCommand.Execute(null);
    }
}
