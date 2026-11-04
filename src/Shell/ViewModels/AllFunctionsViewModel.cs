using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;      // ← TryFindResource 是这里的扩展方法（挂在 IResourceHost 上）
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// 功能清单视图（**首页内嵌**，⛔ 不是独立页）：按模块列出全部二级功能（图标 + 名称 + 简介），点击卡片直接跳转。
/// <para>
/// 每个功能卡片支持**右键 → 固定 / 取消固定到首页收藏夹**，固定状态实时同步（经 <see cref="IFavoritesService.Changed"/>）。
/// </para>
/// <para>
/// 🔴 入口 = 首页收藏卡尾部「显示更多」就地展开（<c>HomeViewModel</c> 用 <c>GetRequiredService</c> 现行装配）；
/// 本类不注册菜单项、不进标签栏。页内「🔄 重载模块」在「版本与更新」的关于部分。
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

    /// <summary>左侧摘要：模块数 / 功能数（构建完清单后通知一次）。</summary>
    public string SummaryText => $"全部功能 · {Groups.Count} 个模块 / {Groups.Sum(g => g.Items.Count)} 项";

    /// <summary>当前是否所有模块都展开 —— 只决定右上角按钮的文案。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleAllText))]
    private bool _allExpanded = true;

    public string ToggleAllText => AllExpanded ? "↕ 全收起" : "↕ 全展开";

    /// <summary>一次展开 / 折叠所有模块节（默认展开，按钮一键收起后只剩模块标题，最省纵向空间）。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        AllExpanded = !AllExpanded;
        foreach (var g in Groups) g.IsExpanded = AllExpanded;
    }

    /// <summary>点模块标题：展开 / 折叠该节。</summary>
    [RelayCommand]
    private void ToggleGroup(FunctionGroup? group)
    {
        if (group != null) group.IsExpanded = !group.IsExpanded;
    }

    private void BuildGroups()
    {
        Groups.Clear();
        // 🔴 v3.65：`IsHome` 分组（如承载「功能清单」自身的 `system` 宿主组）**不进清单** ——
        //    与左树（ShellViewModel.BuildTree）和首页模块计数（HomeViewModel.StatusText）同一口径。
        foreach (var g in _menu.Groups.Where(x => !x.IsHome))
        {
            var fg = new FunctionGroup(g.Title, g.Icon) { IconGeometry = ResolveIcon(g.Icon) };
            foreach (var it in g.Items)
            {
                fg.Items.Add(new FunctionItem(it, _fav.Contains(it.Key)));
            }
            Groups.Add(fg);
        }
        OnPropertyChanged(nameof(SummaryText));
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
    /// 模块分组（图标 + 名称 + 若干功能项），**可折叠**（像设置页的节：点标题展开 / 收起）。
    /// <para>图标是矢量几何键，这里解析成 <see cref="Geometry"/>；取不到则回退文本。</para>
    /// </summary>
    public partial class FunctionGroup : ObservableObject
    {
        public string Title { get; }
        public string Icon { get; }
        public Geometry? IconGeometry { get; init; }
        public bool HasVectorIcon => IconGeometry != null;
        public ObservableCollection<FunctionItem> Items { get; }

        /// <summary>本模块是否展开（默认展开，可单独收起，也可用顶部按钮全部收起）。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Glyph))]
        private bool _isExpanded = true;

        /// <summary>折叠箭头：展开 ▾ / 折叠 ▸。</summary>
        public string Glyph => IsExpanded ? "▾" : "▸";

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
