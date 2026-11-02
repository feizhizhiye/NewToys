using Avalonia.Controls;
using Avalonia.Input;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>书源页：列表交互全走 VM 命令；这里只把浮层遮罩的点击转成「关闭」。</summary>
public partial class SourceListView : UserControl
{
    public SourceListView()
    {
        InitializeComponent();
    }

    /// <summary>点编辑浮层的遮罩 = 关闭（点击不穿透到卡片本体）。</summary>
    private void OnEditorBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is SourceListViewModel vm) vm.CloseEditorCommand.Execute(null);
    }

    /// <summary>点试跑浮层的遮罩 = 关闭（顺带取消还在跑的试跑）。</summary>
    private void OnDebugBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is SourceListViewModel vm) vm.CloseDebugCommand.Execute(null);
    }

    /// <summary>点校验浮层的遮罩 = 关闭（顺带取消还在跑的校验）。</summary>
    private void OnCheckBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is SourceListViewModel vm) vm.CloseCheckCommand.Execute(null);
    }
}
