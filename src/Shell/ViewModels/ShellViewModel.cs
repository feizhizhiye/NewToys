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
using NewToys.Shell.Services;
using NewToys.Shell.Views;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// Shell 根视图模型。
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

    /// <summary>
    /// 应用级设置页的**页 Key**（需求 §4.9.2）：八个节共用这一个页 Key，
    /// 七个**节锚点 Key**（`settings-general` …）在 <see cref="OpenTab"/> 里被归一成它 + 激活对应节。
    /// </summary>
    private const string SettingsKey = "settings";

    /// <summary>节锚点 Key（= 原菜单 Key，⛔ 一律不改）：打开它们 = 打开设置页并激活对应节。</summary>
    private static readonly string[] SettingsSectionKeys =
    {
        "settings-general", "settings-update", "settings-theme",
        "settings-launch", "settings-path", "settings-dns", "settings-data",
        // 「关于」已并入「版本与更新」，此键退成**别名**（不注册菜单，但旧链接仍能打开设置页）
        "settings-about"
    };

    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly INavigationService _nav;
    private readonly IColumnLayoutService _cols;
    private readonly IProfileService _profiles;
    private readonly IModuleReloadService _reload;

    /// <summary>页面 View / ViewModel 的类型检索（菜单项的 <c>ViewTypeName</c> 是字符串，只能靠它落地）。</summary>
    private readonly IPageCatalog _pages;

    /// <summary>当前标签页的契约绑定（详情 / 历史栈 / 状态栏 / 工具条）—— 独立成类见 <see cref="ShellPageBinder"/>。</summary>
    private readonly ShellPageBinder _binder;

    /// <summary>
    /// 全部标签页：**不再区分「固定首页 + 可滚动标签」两套集合** ——
    /// 首页与其余标签一样进滚动区，标签栏直接绑本集合（`ItemsControl ItemsSource="{Binding Tabs}"`）。
    /// </summary>
    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();

    [ObservableProperty] private TabItemViewModel? _activeTab;

    /// <summary>左树节点（首页叶子 + 一级分组 + 二级功能叶子）。</summary>
    public ObservableCollection<TreeMenuItem> TreeNodes { get; } = new();

    /// <summary>一级分组**展开**时触发：ShellView 据此把整组滚入视野（⛔ 收起不触发）。</summary>
    public event EventHandler<TreeMenuItem>? TreeNodeExpanded;

    [ObservableProperty] private bool _isTreeCollapsed;
    [ObservableProperty] private bool _isDetailPanelVisible;
    [ObservableProperty] private object? _detailPanelContent;

    /// <summary>
    /// 声明式详情卡：当前页描述符。非 null → 右列走 <c>DetailCardHost</c> 统一渲染，
    /// ⛔ 此时不再取 <see cref="DetailPanelContent"/>（老路留给尚未迁移的页）。
    /// </summary>
    [ObservableProperty] private NewToys.Shared.Details.DetailCardDescriptor? _detailCard;

    /// <summary>声明式详情卡的 DataContext（= **页面 VM 本身**）：描述符里所有路径都相对它解析。</summary>
    [ObservableProperty] private object? _detailCardSource;

    /// <summary>右列当前是否走声明式渲染（= <see cref="DetailCard"/> 非 null）。</summary>
    [ObservableProperty] private bool _isDetailCardActive;

    /// <summary>
    /// 声明式工具条（/ 需求 §4.8.5）：当前页 VM 若实现 <c>IToolbarSource</c> 且**工具条非空**，
    /// 这里就是它 —— <c>ToolbarHost</c> 据此渲染；否则为 null（Shell ⛔ 不留空行，页面继续用页内自绘工具条）。
    /// </summary>
    [ObservableProperty] private object? _toolbarSource;

    /// <summary>顶部工具条行是否显示（= 本页有声明式工具条）。</summary>
    [ObservableProperty] private bool _isToolbarActive;

    [ObservableProperty] private string _statusText = "NewToys · 已就绪";

    /// <summary>底部状态栏文本：来自当前页的 <see cref="IStatusProvider"/>；页面没接契约就为空。</summary>
    [ObservableProperty] private string _statusBarText = "";

    /// <summary>
    /// 底栏是否按**警示态**（红字）显示：判定 = 文本以 `⚠` 开头 ——
    /// 这是仓内既有的失败约定（如 <c>GeneralViewModel</c> 的 <c>"⚠ " + msg</c>），⛔ 不改 <see cref="IStatusProvider"/> 契约。
    /// </summary>
    public bool IsStatusError => StatusBarText.StartsWith("⚠", StringComparison.Ordinal);

    partial void OnStatusBarTextChanged(string value) => OnPropertyChanged(nameof(IsStatusError));

    /// <summary>
    /// 状态栏那一行是否**可点击跳转**（需求 §4.9.2）：
    /// 当前页实现了 <see cref="IStatusActionProvider"/> 且给了目标 Key。
    /// </summary>
    [ObservableProperty] private bool _hasStatusAction;

    /// <summary>状态栏点击提示（tooltip；没有目标时为空）。</summary>
    public string StatusActionTip => _binder.StatusActionTip;

    /// <summary>重算「状态栏可点击跳转」的两个派生属性（绑定器每次重绑后回调）。</summary>
    internal void RefreshStatusAction()
    {
        HasStatusAction = _binder.HasStatusAction;
        OnPropertyChanged(nameof(StatusActionTip));
    }

    /// <summary>当前标签对应的**左树寻址 Key**：设置页 = 当前节锚点（左树子项是节，标签页 Key 是 `settings`）。</summary>
    private string? ActiveTreeKey
        => ActiveTab?.ViewModel is SettingsViewModel svm ? svm.ActiveSectionKey : ActiveTab?.Key;

    /// <summary>
    /// 左树底部的「当前配置 · 版本号」。
    /// 版本取 <see cref="UpdateService.CurrentVersion"/>（编译时自动生成的 `1.yyMMdd.HH.mm0`，⛔ 禁手写）；
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

    /// <summary>左树宽度：展开 150px、折叠 48px。</summary>
    public double TreePanelWidth => IsTreeCollapsed ? 48d : 150d;

    public ShellViewModel(IServiceProvider sp, IMenuService menu,
                          INavigationService nav, IColumnLayoutService cols,
                          IProfileService profiles, IModuleReloadService reload,
                          IPageCatalog pages)
    {
        _sp = sp; _menu = menu; _nav = nav; _cols = cols; _profiles = profiles; _reload = reload;
        _pages = pages;

        _binder = new ShellPageBinder(this, cols);

        // 左树折叠态从壳级 Flag 恢复。
        _isTreeCollapsed = _cols.LoadFlag(ShellPageKey, "treeCollapsed", false);

        // 模块（功能清单页、模块首页等）通过 INavigationService.NavigateTo(key) 发起跳转，
        // 这里统一接住 —— 模块无需引用 Shell 程序集即可导航。
        _nav.Navigated += (_, e) => OpenTab(e.key);

        // 模块热加载（入口 = 功能清单页「增加功能」操作区，标题栏按钮已移入）→ 重建左树 + 底栏消息。
        // ShellViewModel 是进程级单例（AddSingleton）⇒ 订阅随进程生命周期，无需退订。
        _reload.Reloaded += OnModulesReloaded;

        // 🔴 标签栏**不再有固定列** ⇒ `HomeTab` / `ScrollableTabs` 镜像与 `SyncTabMirror()`
        // 整体删除（标签栏直接绑 `Tabs`，集合通知由 Avalonia 自己处理，⛔ 无需手工镜像）。
        BuildTree();
        // 首导：首页作为第一个标签页（§5.4）。
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
        // 🔴 这段是"切页的后续重活"（标签高亮 / 契约绑定 / 左树高亮 / 分组展开）。
        // ⚠️ MVVM Toolkit 生成的 `ActiveTab` 属性 setter 里，**`OnPropertyChanged(ActiveTab)` 排在
        // 本方法之后** ⇒ 这里一旦抛异常，那条通知就永远发不出（内容区绑的是 `ActiveTab.Content`，
        // 是**链式绑定**，靠这条通知重求值）⇒ 症状 = "标签切了 / 状态栏也变了，内容区却还是上一页，
        // 再点一次才好"。所以这里必须**不许抛**：异常一律变成底栏红字（可诊断，不静默）。
        try
        {
            foreach (var t in Tabs) t.IsActive = ReferenceEquals(t, value);
            _binder.Bind(value);
            UpdateTreeActive();
            EnsureActiveGroupExpanded(value);   // v3.6：确保当前页所属一级是展开的（不收起其它）
        }
        catch (Exception ex)
        {
            StatusBarText = $"⚠ 切换页面时出错：{ex.Message}";
        }
        finally
        {
            // 兜底：即使上面出了问题，也把"当前标签变了"这件事**补通知一遍** ⇒ 内容区一定重求值。
            OnPropertyChanged(nameof(ActiveTab));
        }
    }

    // ==================== 标签页：打开 / 激活 / 关闭 / 刷新 ====================

    /// <summary>
    /// 打开或激活某个 key 的标签页（单例）。找不到 key → 忽略并记一行状态，不抛（§5.5）。
    /// <para>
    /// 五个**设置节锚点 Key** 先归一成页 Key <see cref="SettingsKey"/>，
    /// 再在已打开（或新建）的设置页上激活对应节 —— **切节⛔ 不新开标签页**（需求 §4.9.2 第 1 条）。
    /// </para>
    /// </summary>
    [RelayCommand]
    private void OpenTab(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        var tabKey = NormalizeTabKey(key);
        var existing = Tabs.FirstOrDefault(t => string.Equals(t.Key, tabKey, StringComparison.Ordinal));
        if (existing != null)
        {
            ActiveTab = existing;
            // 已打开设置页 → 只切节（原始 key = 节锚点；`settings` = 通用节）
            if (existing.ViewModel is SettingsViewModel svm) svm.OpenSection(key);
            return;
        }

        // 🔴 兜底可见化：建页这一步（DI 解析 View/VM）万一抛，异常会被 UI 线程吞掉 ⇒ 变成底栏红字。
        TabItemViewModel? tab;
        try
        {
            tab = CreateTab(tabKey);
        }
        catch (Exception ex)
        {
            StatusBarText = $"⚠ 打开「{key}」失败：{ex.Message}";
            return;
        }
        if (tab == null) { StatusBarText = $"⚠ 找不到页面：{key}"; return; }

        // 🔴 **先激活、后入集合** —— `ActiveTab = tab` 会同步完成"切内容区 + 绑契约"，
        // 放在 `Tabs.Add` **之前**，就不存在"入集合那一步出问题 ⇒ 标签亮着但内容没切"的窗口。
        // ⚠️ 代价：此刻 `Tabs` 里还没有它 ⇒ `OnActiveTabChanged` 的 IsActive 循环轮不到它，
        // 故入集合后补一遍高亮。
        ActiveTab = tab;
        try
        {
            Tabs.Add(tab);
        }
        catch (Exception ex)
        {
            // 🔴 兜底可见化：历史上"标签出现了、内容区却没切、再点一次才好"的根因就在
            // `Tabs.Add` 触发的集合通知链里（异常被框架吞掉，连 logs 都不写）⇒ 这里把它变成**看得见**的
            // 底栏红字（`⚠` 前缀 = 红字，见 IsStatusError）。本页内容此时**已经**切过去了（上面两行在前），
            // 所以即使走到这里，用户看到的是正常页面 + 一行提示。
            StatusBarText = $"⚠ 打开「{tab.Title}」时出错：{ex.Message}";
        }
        foreach (var t in Tabs) t.IsActive = ReferenceEquals(t, tab);
        if (tab.ViewModel is SettingsViewModel svm2) svm2.OpenSection(key);
    }

    /// <summary>把「节锚点 Key」归一成页 Key（其余 Key 原样返回，⛔ 不影响任何既有页面）。</summary>
    private static string NormalizeTabKey(string key)
        => SettingsSectionKeys.Contains(key, StringComparer.Ordinal) ? SettingsKey : key;

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

}
