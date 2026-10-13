using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Shell.Models;
using NewToys.Shell.Views;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// Shell 根视图模型（v3.1 · 仿浏览器标签页导航模型）。
/// <para>
/// 由 v1.35 的「三栏 + CurrentGroup/CurrentItem/CurrentContent」改为「标签页驱动」：
/// 一个标签页 = 一个缓存的 View + 一份 VM + 一个历史栈。
/// 打开页面的唯一入口 = <see cref="OpenTab"/>（菜单点击、树节点、标题栏 ⚙、标签栏 ＋ 都走它）。
/// </para>
/// <para>
/// UI 契约：标签 VM 可选实现 <see cref="IDetailPanelProvider"/>（右详情面板）/
/// <see cref="IHistoryProvider"/>（历史栈），Shell 在切换标签时探测并消费。
/// </para>
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    /// <summary>壳级状态（左树折叠态 / 分组展开态）在 config\columnlayout.json 里的归属页 Key。</summary>
    private const string ShellPageKey = "shell";
    private const string HomeKey = "home";

    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly INavigationService _nav;
    private readonly IColumnLayoutService _cols;
    private readonly IProfileService _profiles;

    private IDetailPanelProvider? _detailProvider;
    private IHistoryProvider? _historyProvider;
    private IStatusProvider? _statusProvider;

    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();

    [ObservableProperty] private TabItemViewModel? _activeTab;

    /// <summary>左树节点（首页叶子 + 一级分组 + 二级功能叶子）。</summary>
    public ObservableCollection<TreeMenuItem> TreeNodes { get; } = new();

    [ObservableProperty] private bool _isTreeCollapsed;
    [ObservableProperty] private bool _isDetailPanelVisible;
    [ObservableProperty] private object? _detailPanelContent;
    [ObservableProperty] private string _statusText = "NewToys · 已就绪";

    /// <summary>底部状态栏文本（v3.9）：来自当前页的 <see cref="IStatusProvider"/>；页面没接契约就为空。</summary>
    [ObservableProperty] private string _statusBarText = "";

    /// <summary>
    /// 左树底部的「当前配置 · 版本号」（v3.28）。
    /// 版本取 <see cref="UpdateService.CurrentVersion"/>（编译时自动生成的 `1.yyyyMMdd.HH.mm0`，⛔ 禁手写）；
    /// 配置名取 <see cref="IProfileService.ActiveProfile"/>（多实例时每窗口可不同 → 一眼看出这个窗口在用哪套数据）。
    /// </summary>
    public string TreeVersionText
    {
        get
        {
            var version = UpdateService.CurrentVersion;
            var profile = _profiles?.ActiveProfile;
            return string.IsNullOrWhiteSpace(profile) ? version : $"{profile} · {version}";
        }
    }

    /// <summary>左树是否展开（= 未折叠）。UI 用它控制标题/子级显隐。</summary>
    public bool IsTreeExpanded => !IsTreeCollapsed;

    /// <summary>左树宽度：展开 180px（v3.7 由 240 收窄）、折叠 48px。</summary>
    public double TreePanelWidth => IsTreeCollapsed ? 48d : 180d;

    public ShellViewModel(IServiceProvider sp, IMenuService menu,
                          INavigationService nav, IColumnLayoutService cols,
                          IProfileService profiles)
    {
        _sp = sp; _menu = menu; _nav = nav; _cols = cols; _profiles = profiles;

        // 左树折叠态从壳级 Flag 恢复。
        _isTreeCollapsed = _cols.LoadFlag(ShellPageKey, "treeCollapsed", false);

        // 模块（功能清单页、模块首页等）通过 INavigationService.NavigateTo(key) 发起跳转，
        // 这里统一接住 —— 模块无需引用 Shell 程序集即可导航。
        _nav.Navigated += (_, e) => OpenTab(e.key);

        BuildTree();
        // 首导：首页作为第一个固定标签页（§5.4）。
        OpenTab(HomeKey);
    }

    // ==================== 状态变化 ====================

    partial void OnIsTreeCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTreeExpanded));
        OnPropertyChanged(nameof(TreePanelWidth));
        _cols.SaveFlag(ShellPageKey, "treeCollapsed", value);
    }

    partial void OnActiveTabChanged(TabItemViewModel? value)
    {
        foreach (var t in Tabs) t.IsActive = ReferenceEquals(t, value);
        BindContracts(value);
        UpdateTreeActive();
        EnsureActiveGroupExpanded(value);   // v3.6：确保当前页所属一级是展开的（不收起其它）
    }

    // ==================== 标签页：打开 / 激活 / 关闭 / 刷新 ====================

    /// <summary>打开或激活某个 key 的标签页（单例）。找不到 key → 忽略并记一行状态，不抛（§5.5）。</summary>
    [RelayCommand]
    private void OpenTab(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        var existing = Tabs.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
        if (existing != null) { ActiveTab = existing; return; }

        var tab = CreateTab(key);
        if (tab == null) { StatusText = $"找不到页面：{key}"; return; }

        Tabs.Add(tab);
        ActiveTab = tab;
    }

    /// <summary>单击标签 → 激活。</summary>
    [RelayCommand]
    private void ActivateTab(TabItemViewModel? tab)
    {
        if (tab != null) ActiveTab = tab;
    }

    /// <summary>关闭单个标签（首页不可关；关掉最后一个自动回首页）。</summary>
    [RelayCommand]
    private void CloseTab(TabItemViewModel? tab)
    {
        if (tab == null || !tab.CanClose) return;

        var wasActive = ReferenceEquals(tab, ActiveTab);
        var idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        (tab.ViewModel as IDisposable)?.Dispose();

        if (Tabs.Count == 0) { OpenTab(HomeKey); return; }
        if (wasActive) ActiveTab = Tabs[Math.Clamp(idx, 0, Tabs.Count - 1)];
    }

    /// <summary>关闭其他（首页与目标标签保留）。</summary>
    [RelayCommand]
    private void CloseOtherTabs(TabItemViewModel? tab)
    {
        if (tab == null) return;
        CloseMany(t => !ReferenceEquals(t, tab));
        ActiveTab = tab;
    }

    /// <summary>关闭右侧（该标签左侧保留）。</summary>
    [RelayCommand]
    private void CloseTabsToRight(TabItemViewModel? tab)
    {
        if (tab == null) return;
        var idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        CloseMany(t => Tabs.IndexOf(t) > idx);
    }

    /// <summary>全部关闭（首页不可关 → 最终只剩首页）。</summary>
    [RelayCommand]
    private void CloseAllTabs() => CloseMany(_ => true);

    private void CloseMany(Func<TabItemViewModel, bool> predicate)
    {
        var victims = Tabs.Where(t => t.CanClose && predicate(t)).ToList();
        if (victims.Count == 0) return;

        var activeWasVictim = ActiveTab != null && victims.Contains(ActiveTab);
        var activeIdx = ActiveTab == null ? 0 : Tabs.IndexOf(ActiveTab);
        foreach (var v in victims)
        {
            Tabs.Remove(v);
            (v.ViewModel as IDisposable)?.Dispose();
        }

        if (Tabs.Count == 0) { OpenTab(HomeKey); return; }
        if (activeWasVictim) ActiveTab = Tabs[Math.Clamp(activeIdx, 0, Tabs.Count - 1)];
    }

    /// <summary>刷新当前标签页：销毁并重建 View + VM（标签身份/位置不变）。</summary>
    [RelayCommand]
    private void Refresh()
    {
        var tab = ActiveTab;
        if (tab == null) return;
        var rebuilt = CreateTab(tab.Key);
        if (rebuilt == null) return;
        tab.Replace(rebuilt.Content, rebuilt.ViewModel);
        BindContracts(tab);
        StatusText = $"已重建：{tab.Title}";
    }

    // ==================== 标题栏 / 左树 ====================

    // 注：v3.4 起标题栏删掉了 🏠 主页 / ⚙ 设置 / 🌓 主题三个按钮 →
    //     GoHome / OpenSettings / ToggleTheme 三个命令随之移除（首页靠常驻标签返回、设置走左树、主题走设置页）。
    [RelayCommand] private void OpenFunctionList() => OpenTab("settings-all");

    /// <summary>
    /// 折叠 / 展开左树（v3.5）：
    /// **折叠时顺手把全部一级分组也收起**（用户要求"折叠菜单栏时自动折叠全部二级菜单"），并落盘。
    /// </summary>
    [RelayCommand]
    private void ToggleTree()
    {
        IsTreeCollapsed = !IsTreeCollapsed;

        if (IsTreeCollapsed)
        {
            // 折叠时把全部一级分组也收起（"折叠菜单栏时自动折叠全部二级菜单"），并落盘
            foreach (var node in TreeNodes)
            {
                if (!node.IsGroup || !node.IsExpanded) continue;
                node.IsExpanded = false;
                _cols.SaveFlag(ShellPageKey, "treeGroup:" + node.Key, false);
            }
            return;
        }

        // 展开时确保当前页所属分组是展开的（不收起其它）
        EnsureActiveGroupExpanded(ActiveTab);
    }

    /// <summary>标签页滚轮切换：切到下一个 / 上一个（循环）。</summary>
    [RelayCommand] private void NextTab() => StepTab(1);
    [RelayCommand] private void PrevTab() => StepTab(-1);

    private void StepTab(int delta)
    {
        if (Tabs.Count < 2) return;
        var i = ActiveTab == null ? 0 : Tabs.IndexOf(ActiveTab);
        if (i < 0) i = 0;
        var n = ((i + delta) % Tabs.Count + Tabs.Count) % Tabs.Count;
        ActiveTab = Tabs[n];
    }

    /// <summary>右详情开合（供标题栏/快捷键调用；列表页表头 ▤ 直接调 VM 自己的契约方法）。</summary>
    [RelayCommand] private void ToggleDetail() => _detailProvider?.ToggleDetail();

    /// <summary>左树节点点击：一级分组折叠/展开（不打开页）；叶子打开/激活标签页。</summary>
    [RelayCommand]
    private void TreeClick(TreeMenuItem? node)
    {
        if (node == null) return;

        if (node.IsGroup)
        {
            node.IsExpanded = !node.IsExpanded;
            _cols.SaveFlag(ShellPageKey, "treeGroup:" + node.Key, node.IsExpanded);   // v3.6：手动展开态落盘
            // 折叠态下点分组 → 先展开整棵树（简化实现，不做浮出面板）
            if (IsTreeCollapsed) IsTreeCollapsed = false;
            return;
        }

        OpenTab(node.Key);
    }

    // ==================== 历史栈（v3.5：契约保留、UI 已移除） ====================
    // 标题栏的「前进 / 后退」按钮已在 v3.5 删除（各页几乎全程灰显、无实际意义）。
    // IHistoryProvider 契约与下面的订阅**保留** —— 将来要恢复按钮，把命令接回来即可。

    private void OnStatePushed(object? sender, EventArgs e)
    {
        var tab = ActiveTab;
        if (tab == null || _historyProvider == null) return;
        tab.BackStack.Push(_historyProvider.CaptureState());
        tab.ForwardStack.Clear();
    }

    // ==================== 契约绑定（详情面板 / 历史栈） ====================

    private void BindContracts(TabItemViewModel? tab)
    {
        // 先解绑旧 Tab 的订阅（防内存泄漏）
        if (_detailProvider != null) { _detailProvider.DetailChanged -= OnDetailChanged; _detailProvider = null; }
        if (_historyProvider != null) { _historyProvider.StatePushed -= OnStatePushed; _historyProvider = null; }
        if (_statusProvider != null) { _statusProvider.StatusChanged -= OnStatusChanged; _statusProvider = null; }

        IsDetailPanelVisible = false;
        DetailPanelContent = null;
        StatusBarText = "";

        if (tab?.ViewModel is IDetailPanelProvider p)
        {
            _detailProvider = p;
            // 初值从 config 恢复（Shell 统一持久化，见 §3.6 / §3.8）
            var open = _cols.LoadFlag(tab.Key, IColumnLayoutService.FlagDetailPanel);
            if (open && !p.IsDetailOpen) p.OpenDetail();
            p.DetailChanged += OnDetailChanged;
            RefreshDetail(tab.Key, p);
        }

        if (tab?.ViewModel is IHistoryProvider h)
        {
            _historyProvider = h;
            h.StatePushed += OnStatePushed;
        }

        // v3.9：底部状态栏 —— 页面实现 IStatusProvider 就把状态文本交给 Shell 显示
        if (tab?.ViewModel is IStatusProvider sp)
        {
            _statusProvider = sp;
            sp.StatusChanged += OnStatusChanged;
            StatusBarText = sp.StatusText;
        }
    }

    /// <summary>页面状态变了 → 刷新底部状态栏。</summary>
    private void OnStatusChanged(object? sender, EventArgs e)
        => StatusBarText = _statusProvider?.StatusText ?? "";

    private void OnDetailChanged(object? sender, EventArgs e)
    {
        if (_detailProvider != null && ActiveTab != null)
            RefreshDetail(ActiveTab.Key, _detailProvider);
    }

    private void RefreshDetail(string pageKey, IDetailPanelProvider p)
    {
        IsDetailPanelVisible = p.HasDetailPanel && p.IsDetailOpen;
        DetailPanelContent = p.DetailContent;
        _cols.SaveFlag(pageKey, IColumnLayoutService.FlagDetailPanel, p.IsDetailOpen);
    }

    // ==================== 左树构建 / 高亮 ====================

    private void BuildTree()
    {
        TreeNodes.Clear();

        // 首页：与一级分组平级的叶子节点
        TreeNodes.Add(new TreeMenuItem
        {
            Key = HomeKey, Title = "首页", Icon = "🏠", VectorIcon = ResolveIcon("IconHome"), IsGroup = false
        });

        // 一级分组（含设置组，排在末尾 —— MenuService.Groups 已按 IsBottom/Order 排好）
        foreach (var g in _menu.Groups.Where(x => !x.IsHome))
        {
            var node = new TreeMenuItem
            {
                Key = g.Key,
                Title = g.Title,
                Icon = g.Icon,
                VectorIcon = ResolveIcon(g.Icon),   // v3.2：一级分组 icon = Geometry 资源键
                IsGroup = true,
                // v3.6：展开态恢复落盘（它是**用户状态**，不是派生状态）——首次运行默认不展开
                IsExpanded = _cols.LoadFlag(ShellPageKey, "treeGroup:" + g.Key, false)
            };
            foreach (var it in g.Items)
                node.Children.Add(new TreeMenuItem { Key = it.Key, Title = it.Title, Icon = it.Icon, IsGroup = false });
            TreeNodes.Add(node);
        }
    }

    private void UpdateTreeActive()
    {
        var key = ActiveTab?.Key;
        foreach (var node in TreeNodes)
        {
            // 二级：命中当前标签页
            foreach (var child in node.Children)
                child.IsActive = string.Equals(child.Key, key, StringComparison.Ordinal);

            // 一级：首页等叶子看自身；分组则**任一子项命中时也高亮**（v3.4：二级选中时父级一级同亮）
            node.IsActive = node.IsGroup
                ? node.Children.Any(c => c.IsActive)
                : string.Equals(node.Key, key, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 自动导航（v3.6，**替代 v3.5 的手风琴**）：切标签时**确保当前页所属的一级分组是展开的**，
    /// ⛔ 但**不收起其它已经展开的一级** —— 用户手动展开的分组要保留。
    /// <list type="bullet">
    ///   <item>**折叠态**下什么都不做（二级本来就不显示），一级高亮交给 <see cref="UpdateTreeActive"/>；</item>
    ///   <item>切到**首页**（不属于任何一级分组）→ 同样不动展开态；</item>
    ///   <item>展开态**落盘**（它是用户状态，不是派生状态）。</item>
    /// </list>
    /// </summary>
    private void EnsureActiveGroupExpanded(TabItemViewModel? tab)
    {
        if (IsTreeCollapsed) return;

        var key = tab?.Key;
        if (string.IsNullOrEmpty(key)) return;

        foreach (var node in TreeNodes)
        {
            if (!node.IsGroup || node.IsExpanded) continue;   // 已展开的不动
            if (!node.Children.Any(c => string.Equals(c.Key, key, StringComparison.Ordinal))) continue;

            node.IsExpanded = true;
            _cols.SaveFlag(ShellPageKey, "treeGroup:" + node.Key, true);
        }
    }

    /// <summary>
    /// 把菜单里的图标键解析成 <see cref="Geometry"/>（v3.2）。
    /// 取不到（旧的 emoji / 未知键）返回 null → UI 回退成文本图标，不会崩。
    /// <para>⚠️ Avalonia 12 的 <c>ResourceNodeExtensions.TryFindResource</c> 是 Try 模式（<c>out</c> 参数），
    /// 不是直接返回值 —— 别写成 <c>FindResource(key)</c>。</para>
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
        catch { /* 资源链异常不该影响界面 */ }
        return null;
    }

    // ==================== 视图解析（沿用 v1.35，改「为新标签页解析一次并缓存」） ====================

    private TabItemViewModel? CreateTab(string key)
    {
        if (string.Equals(key, HomeKey, StringComparison.Ordinal))
        {
            var home = _sp.GetRequiredService<HomeView>();
            // ⚠️ 必须**先给 View 设好 DataContext，再挂到内容区**（DataContext 是继承属性）。
            if (home.DataContext == null)
                home.DataContext = _sp.GetRequiredService<HomeViewModel>();
            return new TabItemViewModel(HomeKey, "首页", "🏠", home, home.DataContext, canClose: false);
        }

        var item = _menu.FindItem(key);
        if (item == null) return null;

        var view = ResolveView(item);
        return new TabItemViewModel(item.Key, item.Title, item.Icon, view, view.DataContext, canClose: true);
    }

    /// <summary>
    /// 按约定定位 View：XxxView → XxxViewModel。
    /// 约定：<c>NewToys.&lt;模块&gt;.Views.XxxView</c> ↔ <c>NewToys.&lt;模块&gt;.ViewModels.XxxViewModel</c>。
    /// ⚠️ 必须在 **View 自己所在的程序集** 里先找 —— 不同模块允许出现同名 View。
    /// </summary>
    private Control ResolveView(AppMenuItem it)
    {
        Type? viewType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            var t = SafeTypes(asm).FirstOrDefault(x =>
                x.Name == it.ViewTypeName
                && !x.IsAbstract && x.IsClass
                && typeof(Control).IsAssignableFrom(x));
            if (t != null) { viewType = t; break; }
        }
        if (viewType == null) return BuildErrorView($"找不到视图类型：{it.ViewTypeName}");

        Control? view;
        try { view = ActivatorUtilities.CreateInstance(_sp, viewType) as Control; }
        catch (Exception ex) { return BuildErrorView($"视图 {it.ViewTypeName} 创建失败：{ex.Message}"); }
        if (view == null) return BuildErrorView($"视图 {it.ViewTypeName} 实例化返回 null");

        // DataContext 解析失败不该把整次导航也搭进去 —— 顶多这一页没数据，不能连页面都不给。
        try { view.DataContext = ResolveViewModel(viewType, it.ViewTypeName); }
        catch (Exception ex) { return BuildErrorView($"视图 {it.ViewTypeName} 的 ViewModel 解析失败：{ex.Message}"); }
        return view;
    }

    /// <summary>
    /// 安全枚举程序集里的类型。
    /// <see cref="System.Reflection.Assembly.GetTypes"/> 只要**任意一个**类型依赖加载不了就整体抛
    /// <see cref="System.Reflection.ReflectionTypeLoadException"/>；这里退化成用异常的 <c>Types</c> 继续找。
    /// </summary>
    private static IEnumerable<Type> SafeTypes(System.Reflection.Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Select(t => t!);
        }
        catch { return Array.Empty<Type>(); }
    }

    /// <summary>按「命名空间 .Views → .ViewModels + 类名 View→ViewModel」的约定解析 VM。</summary>
    private object? ResolveViewModel(Type viewType, string viewName)
    {
        var vmName = viewName.EndsWith("View", StringComparison.Ordinal)
            ? viewName[..^4] + "ViewModel"
            : viewName + "ViewModel";

        var candidates = new List<Type?>();
        if (viewType.Namespace?.EndsWith(".Views", StringComparison.Ordinal) == true)
        {
            var ns = viewType.Namespace[..^".Views".Length] + ".ViewModels";
            candidates.Add(viewType.Assembly.GetType($"{ns}.{vmName}", throwOnError: false));
        }
        // 兜底：全程序集按类名找（用于 View 命名空间不合规的历史页面）
        candidates.Add(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(a => SafeTypes(a).FirstOrDefault(t => t.Name == vmName && !t.IsAbstract && t.IsClass))
            .FirstOrDefault(t => t != null));

        foreach (var vmType in candidates)
        {
            if (vmType == null) continue;
            try { return ActivatorUtilities.GetServiceOrCreateInstance(_sp, vmType); }
            catch { /* 换下一个候选 */ }
        }
        return null;
    }

    private Control BuildErrorView(string msg)
    {
        var tb = new TextBlock
        {
            Text = msg,
            Foreground = Avalonia.Media.Brushes.Red,
            Margin = new Avalonia.Thickness(24),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontFamily = "Consolas"
        };
        return new ScrollViewer { Content = tb };
    }
}
