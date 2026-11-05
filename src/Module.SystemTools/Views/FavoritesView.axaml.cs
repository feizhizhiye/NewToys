using Avalonia.Controls;
using Avalonia.Input;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.ViewModels;
using NewToys.UI.Markup;

namespace NewToys.SystemTools.Views;

/// <summary>「系统工具 → 收藏夹」页（`script-favorites`）。</summary>
public partial class FavoritesView : UserControl
{
    public FavoritesView() => InitializeComponent();

    /// <summary>收藏按钮右键 = 「运行 / ✕ 移出收藏夹」，与首页「常用功能」按钮同一套操作逻辑。</summary>
    private void OnItemContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control button) return;
        if (button.DataContext is not SystemCommand item) return;
        if (DataContext is not FavoritesViewModel vm) return;

        var menu = new ContextMenu { DataContext = vm };
        menu.Items.Add(new MenuItem { Header = "运行", Icon = Glyph.Create("IconPlay", 14), Command = vm.RunCommand, CommandParameter = item });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "移出收藏夹", Icon = Glyph.Create("IconClose", 14), Command = vm.UnfavoriteCommand, CommandParameter = item });

        menu.Open(button);
        e.Handled = true;
    }
}
