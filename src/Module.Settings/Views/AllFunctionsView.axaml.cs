using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.Core.Services;
using NewToys.Settings.ViewModels;
using NewToys.Shared.Services;

namespace NewToys.Settings.Views;

public partial class AllFunctionsView : UserControl
{
    private readonly IFavoritesService _fav;
    private AllFunctionsViewModel? _vm;

    public AllFunctionsView()
    {
        InitializeComponent();
        // 模块不能引用 Shell（分层硬约束）→ 走 Core 的全局访问点 AppServices。
        _fav = AppServices.Get<IFavoritesService>()!;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as AllFunctionsViewModel;
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

    private void OnFavoritesChanged() => _vm?.RefreshFavorites();

    /// <summary>
    /// 卡片右键 → 弹出「固定 / 取消固定到首页收藏夹」。
    /// <para>
    /// 用 <c>PointerPressed</c>（右键）而非纯 XAML <c>ContextMenu</c>：菜单挂在独立弹出树，不继承宿主
    /// <c>DataContext</c>，这里在 code-behind 直接把 VM 的命令与当前卡片的 Key 挂上，最稳。
    /// </para>
    /// </summary>
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (!e.GetCurrentPoint(btn).Properties.IsRightButtonPressed) return;
        e.Handled = true;

        var vm = _vm ?? DataContext as AllFunctionsViewModel;
        var item = btn.DataContext as AllFunctionsViewModel.FunctionItem;
        if (vm == null || item == null) return;

        var menu = new ContextMenu();
        var mi = new MenuItem
        {
            Header = item.IsFavorite ? "取消固定到首页收藏夹" : "固定到首页收藏夹",
            Command = vm.ToggleFavoriteCommand,
            CommandParameter = item.Key
        };
        menu.Items.Add(mi);
        menu.Open(btn);
    }
}
