using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 功能清单 页面：按模块列出全部二级功能（图标 + 名称 + 简介），点击卡片直接跳转。
/// 右侧的二级菜单「名称 / 功能简介」面板拆除后，统一收归到此页（见项目现状 v1.20 后续）。
/// <para>
/// 每个功能卡片支持**右键 → 固定 / 取消固定到首页收藏夹**，固定状态实时同步（经 <see cref="IFavoritesService.Changed"/>）。
/// </para>
/// </summary>
public partial class AllFunctionsViewModel : ViewModelBase
{
    private readonly IMenuService _menu;
    private readonly INavigationService _nav;
    private readonly IFavoritesService _fav;

    /// <summary>按模块分组的功能项（含简介与固定状态），已按 Order 排序。</summary>
    public ObservableCollection<FunctionGroup> Groups { get; } = new();

    public ICommand NavigateCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }

    public AllFunctionsViewModel(IMenuService menu, INavigationService nav, IFavoritesService fav)
    {
        _menu = menu;
        _nav = nav;
        _fav = fav;

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

    private void BuildGroups()
    {
        Groups.Clear();
        foreach (var g in _menu.Groups)
        {
            var fg = new FunctionGroup(g.Title, g.Icon);
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

    /// <summary>模块分组（图标 + 名称 + 若干功能项）。</summary>
    public class FunctionGroup
    {
        public string Title { get; }
        public string Icon { get; }
        public ObservableCollection<FunctionItem> Items { get; }

        public FunctionGroup(string title, string icon)
        {
            Title = title;
            Icon = icon;
            Items = new ObservableCollection<FunctionItem>();
        }
    }
}
