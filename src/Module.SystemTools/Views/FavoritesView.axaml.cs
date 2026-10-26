using Avalonia.Controls;
using Avalonia.Input;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.ViewModels;

namespace NewToys.SystemTools.Views;

/// <summary>「系统工具 → 收藏夹」页（`script-favorites`）。</summary>
public partial class FavoritesView : UserControl
{
    public FavoritesView() => InitializeComponent();

    /// <summary>
    /// 收藏按钮右键 = 「▶ 运行 / ✕ 移出收藏夹」，与首页「常用功能」按钮同一套操作逻辑。
    /// <para>
    /// <c>ContextMenu</c> 挂独立弹出树，模板里声明的菜单取不到页面 VM，
    /// 故在此现构造 <see cref="MenuItem"/> 并写死命令与参数。
    /// </para>
    /// </summary>
    private void OnItemContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control button) return;
        if (button.DataContext is not SystemCommand item) return;
        if (DataContext is not FavoritesViewModel vm) return;

        var menu = new ContextMenu { DataContext = vm };
        menu.Items.Add(new MenuItem { Header = "▶ 运行", Command = vm.RunCommand, CommandParameter = item });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "✕ 移出收藏夹", Command = vm.UnfavoriteCommand, CommandParameter = item });

        menu.Open(button);
        e.Handled = true;
    }
}
