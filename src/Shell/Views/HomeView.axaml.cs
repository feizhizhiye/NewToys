using Avalonia;
using Avalonia.Controls;
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
}
