using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>
/// 搜索页。控件级逻辑只有两件：回车即搜索、把 VM 的正文推给 <c>TextViewer</c>
/// （VM ⛔ 不碰控件，正文控件 ⛔ 不自己发请求）。
/// </summary>
public partial class SearchView : UserControl
{
    private SearchViewModel? _vm;

    public SearchView()
    {
        InitializeComponent();
        KeywordBox.KeyDown += OnKeywordKeyDown;
        DetachedFromVisualTree += (_, _) => Unsubscribe();

        // 标签页复用（VM 不重建）时，靠重新挂载那一刻再取一次「待读的书」
        AttachedToVisualTree += (_, _) => _vm?.CheckPendingBook();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        Unsubscribe();
        _vm = DataContext as SearchViewModel;
        if (_vm is null) return;

        _vm.ContentChanged += OnContentChanged;

        // 🔴 ContextMenu 在独立弹出树上，DataContext 不随宿主继承（否则右键菜单点了没反应）
        ResultMenu.DataContext = _vm;

        ApplyContent();
        _vm.CheckPendingBook();
    }

    private void Unsubscribe()
    {
        if (_vm is not null) _vm.ContentChanged -= OnContentChanged;
    }

    private void OnKeywordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;
        e.Handled = true;
        if (_vm.SearchCommand.CanExecute(null)) _vm.SearchCommand.Execute(null);
    }

    private void OnContentChanged(object? sender, EventArgs e) => ApplyContent();

    private void ApplyContent() => Body.SetText(_vm?.ContentText ?? "", 0);
}
