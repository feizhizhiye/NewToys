using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>
/// 搜索页。控件级逻辑只有三件：回车即搜索、书源浮层点遮罩收起、右键菜单拿到 DataContext
/// （ContextMenu 在独立弹出树上，不继承宿主 DataContext）。
/// <para>⛔ 本页不渲染正文 —— 正文统一在阅读页。</para>
/// </summary>
public partial class SearchView : UserControl
{
    private SearchViewModel? _vm;

    public SearchView()
    {
        InitializeComponent();
        KeywordBox.KeyDown += OnKeywordKeyDown;

        // 标签页复用（VM 不重建）时，靠重新挂载那一刻再取一次「换源带过来的书名」
        AttachedToVisualTree += (_, _) => _vm?.CheckPendingKeyword();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _vm = DataContext as SearchViewModel;
        if (_vm is null) return;

        ResultMenu.DataContext = _vm;
    }

    private void OnKeywordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;
        e.Handled = true;
        if (_vm.SearchCommand.CanExecute(null)) _vm.SearchCommand.Execute(null);
    }

    private void OnSourceBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null) _vm.ShowSources = false;
    }
}
