using Avalonia.Controls;
using Avalonia.Input;
using NewToys.PcTools.ViewModels;
using NewToys.Shared.Models;

namespace NewToys.PcTools.Views;

public partial class FavoritesView : UserControl
{
    public FavoritesView() { InitializeComponent(); }

    /// <summary>
    /// 卡片右键 = 「▶ 运行 / ✕ 取消固定」。
    /// <para>
    /// 为什么在 code-behind 里现构造菜单：<c>ContextMenu</c> 挂在**独立的弹出树**上，
    /// 模板里声明的菜单取不到页面 VM，<c>$parent[UserControl]</c> 这类绑定在弹出树里也断链（见规范 3.6）。
    /// 这里直接构造 <see cref="MenuItem"/> 并把命令与参数写死，最稳，也不依赖任何 DataContext 继承。
    /// （裸写 <c>MenuItem</c> 不会再撞名：应用侧菜单模型已改名 <c>AppMenuItem</c>，见 MenuModels.cs 顶部约定。）
    /// </para>
    /// </summary>
    private void OnCardContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control card) return;
        if (DataContext is not FavoritesViewModel vm) return;
        if (card.DataContext is not QuickAction item) return;

        var menu = new ContextMenu { DataContext = vm };
        menu.Items.Add(new MenuItem { Header = "▶ 运行", Command = vm.RunCommand, CommandParameter = item });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "✕ 取消固定", Command = vm.UnfavoriteCommand, CommandParameter = item });

        menu.Open(card);   // Open(control) 内部会把菜单挂到这张卡片上并设置 PlacementTarget
        e.Handled = true;
    }
}
