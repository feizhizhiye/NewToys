using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

public partial class HomeView : UserControl
{
    private readonly IFavoritesService _fav;
    private HomeViewModel? _vm;

    public HomeView()
    {
        InitializeComponent();
        _fav = App.GetService<IFavoritesService>()!;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as HomeViewModel;
    }

    /// <summary>挂接时订阅收藏夹变化，卸载时退订 —— 瞬态页面不会泄漏单例 FavoritesService。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _fav.Changed += OnFavoritesChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _fav.Changed -= OnFavoritesChanged;
    }

    private void OnFavoritesChanged() => _vm?.BuildFavorites();

    /// <summary>
    /// 常用功能按钮右键 = 「▶ 打开 / ✕ 取消固定」—— 与「脚本管理器 → 收藏夹」按钮墙完全一致的交互。
    /// <para>
    /// 为什么在 code-behind 里现构造菜单：<c>ContextMenu</c> 挂在**独立的弹出树**上，
    /// 模板里声明的菜单取不到页面 VM，<c>$parent[UserControl]</c> 这类绑定在弹出树里会断链（规范 3.6）。
    /// 这里直接构造 <see cref="MenuItem"/> 并把命令与参数写死，最稳。
    /// （裸写 <c>MenuItem</c> 不会再撞名：应用侧菜单模型已改名 <c>AppMenuItem</c>。）
    /// </para>
    /// </summary>
    private void OnFavContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control button) return;
        if (DataContext is not HomeViewModel vm) return;
        if (button.DataContext is not FavCard card) return;
        // v3.66：尾部「显示更多」卡**不是收藏项** ⇒ 不给它右键菜单（否则会出现"取消固定 显示更多"）。
        if (card.IsMore) { e.Handled = true; return; }

        var menu = new ContextMenu { DataContext = vm };
        menu.Items.Add(new MenuItem { Header = "▶ 打开", Command = vm.NavigateCommand, CommandParameter = card.Key });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "✕ 取消固定", Command = vm.UnpinCommand, CommandParameter = card.Key });

        menu.Open(button);   // Open(control) 内部会把菜单挂到这颗按钮上并设置 PlacementTarget
        e.Handled = true;
    }
}
