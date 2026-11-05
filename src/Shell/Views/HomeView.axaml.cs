using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.Shared.Services;
using NewToys.UI.Markup;
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

    /// <summary>常用功能按钮右键 = 「打开 / ✕ 取消固定」—— 与「脚本管理器 → 收藏夹」按钮墙完全一致的交互。（WHY: SHL-053）</summary>
    private void OnFavContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control button) return;
        if (DataContext is not HomeViewModel vm) return;
        if (button.DataContext is not FavCard card) return;
        // 尾部「显示更多」卡**不是收藏项** ⇒ 不给它右键菜单（否则会出现"取消固定 显示更多"）。
        if (card.IsMore) { e.Handled = true; return; }

        var menu = new ContextMenu { DataContext = vm };
        menu.Items.Add(new MenuItem { Header = "打开", Icon = Glyph.Create("IconPlay", 14), Command = vm.NavigateCommand, CommandParameter = card.Key });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "取消固定", Icon = Glyph.Create("IconClose", 14), Command = vm.UnpinCommand, CommandParameter = card.Key });

        menu.Open(button);   // Open(control) 内部会把菜单挂到这颗按钮上并设置 PlacementTarget
        e.Handled = true;
    }
}
