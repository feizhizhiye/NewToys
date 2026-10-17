using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;      // ← TryFindResource 是这里的扩展方法（挂在 IResourceHost 上）
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 功能清单 页面：按模块列出全部二级功能（图标 + 名称 + 简介），点击卡片直接跳转。
/// 右侧的二级菜单「名称 / 功能简介」面板拆除后，统一收归到此页（见《状态与路线图》v1.20 后续）。
/// <para>
/// 每个功能卡片支持**右键 → 固定 / 取消固定到首页收藏夹**，固定状态实时同步（经 <see cref="IFavoritesService.Changed"/>）。
/// </para>
/// </summary>
public partial class AllFunctionsViewModel : ViewModelBase
{
    private readonly IMenuService _menu;
    private readonly INavigationService _nav;
    private readonly IFavoritesService _fav;
    private readonly IModuleReloadService _reload;

    /// <summary>按模块分组的功能项（含简介与固定状态），已按 Order 排序。</summary>
    public ObservableCollection<FunctionGroup> Groups { get; } = new();

    public ICommand NavigateCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }

    public AllFunctionsViewModel(IMenuService menu, INavigationService nav, IFavoritesService fav,
                                 IModuleReloadService reload)
    {
        _menu = menu;
        _nav = nav;
        _fav = fav;
        _reload = reload;

        NavigateCommand = new RelayCommand<string?>(k =>
        {
            if (!string.IsNullOrEmpty(k)) _nav.NavigateTo(k);
        });
        ToggleFavoriteCommand = new RelayCommand<string?>(k =>
        {
            if (!string.IsNullOrEmpty(k)) _fav.Toggle(k);
        });

        // 收藏夹变化（右键固定/取消）→ 刷新 ★ 标记：订阅放在 View 的 VisualTree 挂接/卸载时，
        // 避免瞬态 VM 被单例 FavoritesService 长期持有（内存泄漏）。
        BuildGroups();
    }

    /// <summary>🔄 重载模块（NT-052）：重扫 Modules\ 装新模块 → 返回后刷新本页清单（新功能卡片即时出现）。
    /// 左树重建 / 底栏计数由 Shell 订阅 <see cref="IModuleReloadService.Reloaded"/> 完成。</summary>
    [RelayCommand]
    private void ReloadModules()
    {
        _reload.HotReload();   // 做事 + 同步触发 Reloaded（Shell：重建左树 + 底栏计数）
        BuildGroups();          // 本页清单刷新：新模块的功能卡片即时出现
    }

    private void BuildGroups()
    {
        Groups.Clear();
        foreach (var g in _menu.Groups)
        {
            var fg = new FunctionGroup(g.Title, g.Icon) { IconGeometry = ResolveIcon(g.Icon) };
            foreach (var it in g.Items)
            {
                fg.Items.Add(new FunctionItem(it, _fav.Contains(it.Key)));
            }
            Groups.Add(fg);
        }
    }

    public void RefreshFavorites()
    {
        foreach (var g in Groups)
            foreach (var it in g.Items)
                it.IsFavorite = _fav.Contains(it.Key);
    }

    /// <summary>单条功能（包装菜单项，额外带「是否已固定」的可观察标记）。</summary>
    public partial class FunctionItem : ViewModelBase
    {
        public string Key { get; }
        public string Icon { get; }
        public string Title { get; }
        public string Desc { get; }

        [ObservableProperty] private bool _isFavorite;

        public FunctionItem(AppMenuItem m, bool isFavorite)
        {
            Key = m.Key;
            Icon = m.Icon;
            Title = m.Title;
            Desc = m.Desc;
            _isFavorite = isFavorite;
        }
    }

    /// <summary>
    /// 模块分组（图标 + 名称 + 若干功能项）。
    /// <para>v3.2：一级分组图标改为矢量几何键，这里解析成 <see cref="Geometry"/>；取不到则回退文本。</para>
    /// </summary>
    public class FunctionGroup
    {
        public string Title { get; }
        public string Icon { get; }
        public Geometry? IconGeometry { get; init; }
        public bool HasVectorIcon => IconGeometry != null;
        public ObservableCollection<FunctionItem> Items { get; }

        public FunctionGroup(string title, string icon)
        {
            Title = title;
            Icon = icon;
            Items = new ObservableCollection<FunctionItem>();
        }
    }

    /// <summary>
    /// 把菜单里的图标键解析成 Geometry（v3.2）；取不到（旧 emoji / 未知键）返回 null。
    /// <para>⚠️ Avalonia 12 的 <c>TryFindResource</c> 是 Try 模式（<c>out</c> 参数），不是直接返回。</para>
    /// </summary>
    private static Geometry? ResolveIcon(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        try
        {
            var app = Avalonia.Application.Current;
            if (app != null && app.TryFindResource(key, out var value) && value is Geometry g)
                return g;
        }
        catch { }
        return null;
    }
}
