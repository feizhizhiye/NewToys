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

    /// <summary>
    /// 应用级设置页的**页 Key**（v3.65 / NT-062，需求 §4.9.2）：八个节共用这一个页 Key，
    /// 七个**节锚点 Key**（`settings-general` …）在 <see cref="OpenTab"/> 里被归一成它 + 激活对应节。
    /// </summary>
    private const string SettingsKey = "settings";

    /// <summary>节锚点 Key（= 原菜单 Key，⛔ 一律不改）：打开它们 = 打开设置页并激活对应节。</summary>
    private static readonly string[] SettingsSectionKeys =
    {
        "settings-general", "settings-update", "settings-theme",
        "settings-launch", "settings-path", "settings-dns", "settings-data"
    };

    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly INavigationService _nav;
    private readonly IColumnLayoutService _cols;
    private readonly IProfileService _profiles;
    private readonly IModuleReloadService _reload;

    private IDetailPanelProvider? _detailProvider;
    private IHistoryProvider? _historyProvider;
    private IStatusProvider? _statusProvider;
    private IStatusActionProvider? _statusActionProvider;

    /// <summary>当前标签是否就是应用级设置页（切节要重绑契约 / 刷左树高亮）。</summary>
    private SettingsViewModel? _settingsVm;

    /// <summary>
    /// 全部标签页（v3.66 第十八轮）：**不再区分「固定首页 + 可滚动标签」两套集合** ——
    /// 首页与其余标签一样进滚动区，标签栏直接绑本集合（`ItemsControl ItemsSource="{Binding Tabs}"`）。
    /// </summary>
    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();

    [ObservableProperty] private TabItemViewModel? _activeTab;

    /// <summary>左树节点（首页叶子 + 一级分组 + 二级功能叶子）。</summary>
    public ObservableCollection<TreeMenuItem> TreeNodes { get; } = new();

    /// <summary>一级分组**展开**时触发（NT-037）：ShellView 据此把整组滚入视野（⛔ 收起不触发）。</summary>
    public event EventHandler<TreeMenuItem>? TreeNodeExpanded;

    [ObservableProperty] private bool _isTreeCollapsed;
    [ObservableProperty] private bool _isDetailPanelVisible;
    [ObservableProperty] private object? _detailPanelContent;

    /// <summary>
    /// 声明式详情卡（NT-054）：当前页描述符。非 null → 右列走 <c>DetailCardHost</c> 统一渲染，
    /// ⛔ 此时不再取 <see cref="DetailPanelContent"/>（老路留给尚未迁移的页）。
    /// </summary>
    [ObservableProperty] private NewToys.Shared.Details.DetailCardDescriptor? _detailCard;

    /// <summary>声明式详情卡的 DataContext（= **页面 VM 本身**）：描述符里所有路径都相对它解析。</summary>
    [ObservableProperty] private object? _detailCardSource;

    /// <summary>右列当前是否走声明式渲染（= <see cref="DetailCard"/> 非 null）。</summary>
    [ObservableProperty] private bool _isDetailCardActive;

    /// <summary>
    /// 声明式工具条（NT-055 / 需求 §4.8.5）：当前页 VM 若实现 <c>IToolbarSource</c> 且**工具条非空**，
    /// 这里就是它 —— <c>ToolbarHost</c> 据此渲染；否则为 null（Shell ⛔ 不留空行，页面继续用页内自绘工具条）。
    /// </summary>
    [ObservableProperty] private object? _toolbarSource;

    /// <summary>顶部工具条行是否显示（= 本页有声明式工具条）。</summary>
    [ObservableProperty] private bool _isToolbarActive;

    [ObservableProperty] private string _statusText = "NewToys · 已就绪";

    /// <summary>底部状态栏文本（v3.9）：来自当前页的 <see cref="IStatusProvider"/>；页面没接契约就为空。</summary>
    [ObservableProperty] private string _statusBarText = "";

    /// <summary>
    /// 底栏是否按**警示态**（红字）显示（v3.64 / NT-061）：判定 = 文本以 `⚠` 开头 ——
    /// 这是仓内既有的失败约定（如 <c>GeneralViewModel</c> 的 <c>"⚠ " + msg</c>），⛔ 不改 <see cref="IStatusProvider"/> 契约。
    /// </summary>
    public bool IsStatusError => StatusBarText.StartsWith("⚠", StringComparison.Ordinal);

    partial void OnStatusBarTextChanged(string value) => OnPropertyChanged(nameof(IsStatusError));

    /// <summary>
    /// 状态栏那一行是否**可点击跳转**（v3.65 / NT-062，需求 §4.9.2 第 5 条）：
    /// 当前页实现了 <see cref="IStatusActionProvider"/> 且给了目标 Key。
    /// </summary>
    [ObservableProperty] private bool _hasStatusAction;

    /// <summary>状态栏点击提示（tooltip；没有目标时为空）。</summary>
    public string StatusActionTip => _statusActionProvider?.StatusActionTip ?? "";

    /// <summary>重算「状态栏可点击跳转」的两个派生属性。</summary>
    private void RefreshStatusAction()
    {
        HasStatusAction = !string.IsNullOrWhiteSpace(_statusActionProvider?.StatusActionKey);
        OnPropertyChanged(nameof(StatusActionTip));
    }

    /// <summary>当前标签对应的**左树寻址 Key**：设置页 = 当前节锚点（左树子项是节，标签页 Key 是 `settings`）。</summary>
    private string? ActiveTreeKey
        => ActiveTab?.ViewModel is SettingsViewModel svm ? svm.ActiveSectionKey : ActiveTab?.Key;

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

    /// <summary>左树宽度：展开 150px（v3.7 由 240 收窄）、折叠 48px。</summary>
    public double TreePanelWidth => IsTreeCollapsed ? 48d : 150d;

    public ShellViewModel(IServiceProvider sp, IMenuService menu,
                          INavigationService nav, IColumnLayoutService cols,
                          IProfileService profiles, IModuleReloadService reload)
    {
        _sp = sp; _menu = menu; _nav = nav; _cols = cols; _profiles = profiles; _reload = reload;

        // 左树折叠态从壳级 Flag 恢复。
        _isTreeCollapsed = _cols.LoadFlag(ShellPageKey, "treeCollapsed", false);

        // 模块（功能清单页、模块首页等）通过 INavigationService.NavigateTo(key) 发起跳转，
        // 这里统一接住 —— 模块无需引用 Shell 程序集即可导航。
        _nav.Navigated += (_, e) => OpenTab(e.key);

        // NT-052：模块热加载（入口 = 功能清单页「增加功能」操作区，标题栏按钮已移入）→ 重建左树 + 底栏消息。
        // ShellViewModel 是进程级单例（AddSingleton）⇒ 订阅随进程生命周期，无需退订。
        _reload.Reloaded += OnModulesReloaded;

        // 🔴 v3.66 第十八轮：标签栏**不再有固定列** ⇒ `HomeTab` / `ScrollableTabs` 镜像与 `SyncTabMirror()`
        //    整体删除（标签栏直接绑 `Tabs`，集合通知由 Avalonia 自己处理，⛔ 无需手工镜像）。
        //    v3.45~v3.66 期间"每处集合修改点显式调 SyncTabMirror()"的做法随之作废（Add / Remove 共 3 处调用点已删）。
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
        // 🔴 v3.66 第九轮：这段是"切页的后续重活"（标签高亮 / 契约绑定 / 左树高亮 / 分组展开）。
        //    ⚠️ MVVM Toolkit 生成的 `ActiveTab` 属性 setter 里，**`OnPropertyChanged(ActiveTab)` 排在
        //    本方法之后** ⇒ 这里一旦抛异常，那条通知就永远发不出（内容区绑的是 `ActiveTab.Content`，
        //    是**链式绑定**，靠这条通知重求值）⇒ 症状 = "标签切了 / 状态栏也变了，内容区却还是上一页，
        //    再点一次才好"。所以这里必须**不许抛**：异常一律变成底栏红字（可诊断，不静默）。
        try
        {
            foreach (var t in Tabs) t.IsActive = ReferenceEquals(t, value);
            BindContracts(value);
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
    /// v3.65 / NT-062：五个**设置节锚点 Key** 先归一成页 Key <see cref="SettingsKey"/>，
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

        // 🔴 v3.66 第八轮：**先激活、后入集合** —— `ActiveTab = tab` 会同步完成"切内容区 + 绑契约"，
        //    放在 `Tabs.Add` **之前**，就不存在"入集合那一步出问题 ⇒ 标签亮着但内容没切"的窗口。
        //    ⚠️ 代价：此刻 `Tabs` 里还没有它 ⇒ `OnActiveTabChanged` 的 IsActive 循环轮不到它，
        //    故入集合后补一遍高亮。
        ActiveTab = tab;
        try
        {
            Tabs.Add(tab);
        }
        catch (Exception ex)
        {
            // 🔴 兜底可见化（v3.66 第八轮）：历史上"标签出现了、内容区却没切、再点一次才好"的根因就在
            //    `Tabs.Add` 触发的集合通知链里（异常被框架吞掉，连 logs 都不写）⇒ 这里把它变成**看得见**的
            //    底栏红字（`⚠` 前缀 = 红字，见 IsStatusError）。本页内容此时**已经**切过去了（上面两行在前），
            //    所以即使走到这里，用户看到的是正常页面 + 一行提示。
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

    /// <summary>刷新当前标签页：销毁并重建 View + VM（标签身份/位置不变）。</summary>
    [RelayCommand]
    private void Refresh()
    {
        var tab = ActiveTab;
        if (tab == null) return;
        var rebuilt = CreateTab(tab.Key);
        if (rebuilt == null) return;

        // ⚠️ v3.66 第九轮：这里曾试过"先把内容区置空再挂新 View"，实测会让页面**整片空白**
        //    （内容区的 Content 换成新 View 时挂不上）⇒ 已回滚。现在就按"原地换 View"来。
        tab.Replace(rebuilt.Content, rebuilt.ViewModel);
        BindContracts(tab);
        // v3.66 第十二轮：内容区由 Shell 侧**显式挂载**（见 ShellView.SyncContentHost）——
        // 标签还是同一个、只是它缓存的 View 换了 ⇒ 这里补发一次"当前标签变了"，让内容区重挂。
        OnPropertyChanged(nameof(ActiveTab));
        StatusText = $"已重建：{tab.Title}";
    }

    // ==================== 标题栏 / 左树 ====================

    // 注：v3.4 起标题栏删掉了 🏠 主页 / ⚙ 设置 / 🌓 主题三个按钮 →
    //     OpenSettings / ToggleTheme 两个命令随之移除（设置走左树 / Ctrl+,、主题走设置页）。
    // 🔴 v3.66 第十八轮：标签栏 `＋` 从「打开功能清单页」改为「**导航到首页**」——
    //     首页收藏卡尾部自带功能清单入口（内嵌 `AllFunctionsView`），`＋` 不再直达 `settings-all`。
    [RelayCommand] private void OpenHomeTab() => OpenTab(HomeKey);

    /// <summary>
    /// 打开**应用级设置页**（v3.65 / NT-062，需求 §4.3 / §4.9.2）：入口 = 左树底部版本行的**设置齿轮**
    /// 与 `Ctrl+,`。默认落在「通用」节（页内可再切到其它节）。
    /// </summary>
    [RelayCommand] private void OpenSettings() => OpenTab("settings-general");

    /// <summary>
    /// 搜索**应用级设置页的节**（v3.66 / NT-062）：入口 = 标题栏窗口按钮组左侧的搜索按钮（回车触发）。
    /// 命中 ⇒ 打开设置页并展开该节（其余节收起，见 <c>SettingsViewModel.SearchAndOpen</c>）；
    /// 未命中 ⇒ 底栏给一行提示（⛔ 不用 `⚠` 前缀 —— 那不是错误，不该红字）。
    /// </summary>
    [RelayCommand]
    private void SearchSettings(string? keyword)
    {
        var kw = keyword?.Trim();
        if (string.IsNullOrEmpty(kw)) return;

        OpenTab(SettingsKey);
        if (ActiveTab?.ViewModel is SettingsViewModel svm && svm.SearchAndOpen(kw)) return;

        StatusText = $"没找到匹配的设置项：{kw}";
        StatusBarText = $"没找到匹配的设置项：{kw}（可试「路径」「DNS」「主题」「备份」等关键词）";
    }

    /// <summary>
    /// 状态栏 `⚠` 那一行**可点击跳转**（v3.65 / NT-062，需求 §4.9.2 第 5 条）：目标 Key 由当前页的
    /// <see cref="IStatusActionProvider"/> 给（典型 = 外部程序缺失 → 跳到「运行环境」节）。
    /// </summary>
    [RelayCommand]
    private void InvokeStatusAction()
    {
        var key = _statusActionProvider?.StatusActionKey;
        if (!string.IsNullOrWhiteSpace(key)) OpenTab(key);
    }

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
            // NT-037：展开后通知 ShellView 把整组滚入视野（靠底部的分组展开后子项会被 ScrollViewer 裁掉）
            if (node.IsExpanded) TreeNodeExpanded?.Invoke(this, node);
            return;
        }

        OpenTab(node.Key);
    }

    /// <summary>
    /// 模块热加载完成（NT-052：入口已由标题栏移入**功能清单页**「增加功能」操作区）：
    /// 重建左树 + 底栏消息（文案与旧标题栏按钮逐字一致）。
    /// <para>
    /// 规范 §3.7：只做「新增」（移除模块要重启）；单个 DLL 失败被隔离（⛔ 不拖垮宿主）；
    /// 已构建的 DI 容器无法追加服务 → 热加载模块的 RegisterServices 跳过，优先页面型模块。
    /// </para>
    /// </summary>
    private void OnModulesReloaded((int Added, int Skipped, int Failed) result)
    {
        BuildTree();   // 左树重建：新增 / 移除的模块菜单反映出来

        var (added, skipped, failed) = result;
        var msg = failed > 0
            ? $"模块重载：新增 {added} / 跳过 {skipped} / 失败 {failed}（失败不影响使用，详情见 Debug 输出）"
            : $"模块重载：新增 {added} / 跳过 {skipped} / 失败 0。";
        StatusText = msg;
        // 壳级 StatusText 没有 UI 消费 → 直接写底栏（瞬态；切标签页会被页面状态覆盖，符合预期）
        StatusBarText = msg;
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
        if (_settingsVm != null) { _settingsVm.SectionChanged -= OnSettingsSectionChanged; _settingsVm = null; }

        IsDetailPanelVisible = false;
        DetailPanelContent = null;
        DetailCard = null;
        DetailCardSource = null;
        IsDetailCardActive = false;
        StatusBarText = "";
        _statusActionProvider = null;
        RefreshStatusAction();

        // v3.65 / NT-062：应用级设置页是**复合 VM** —— 它把契约转发给当前节，
        // 切节时发 SectionChanged ⇒ 这里重新探测一遍（工具条 / 详情卡 / 状态栏随节变）。
        if (tab?.ViewModel is SettingsViewModel settings)
        {
            _settingsVm = settings;
            settings.SectionChanged += OnSettingsSectionChanged;
        }

        // v3.53 / NT-055：声明式工具条 —— 页面没接契约（或工具条为空）= 本页没有工具条（⛔ 不留空行）
        // v3.66 / NT-062：复合页可经 IToolbarOwner 把承载者换成**子 VM**（描述符里的 CommandPath 相对它解析）。
        //   ⚠️ 第五轮起应用级设置页**不再实现**该契约（节工具条改为在节点内渲染，用户要求"放回本节顶部"）
        //   ⇒ 该分支当前只为将来的复合页保留。
        var toolbarOwner = tab?.ViewModel is IToolbarOwner owner ? owner.ToolbarOwner : tab?.ViewModel;
        var toolbar = (toolbarOwner as IToolbarSource)?.Toolbar;
        IsToolbarActive = toolbar is { Count: > 0 };
        ToolbarSource = IsToolbarActive ? toolbarOwner : null;

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

        // v3.65 / NT-062：状态栏 `⚠` 可点击跳转（目标由页面给；没有目标就不可点）
        _statusActionProvider = tab?.ViewModel as IStatusActionProvider;
        RefreshStatusAction();
    }

    /// <summary>设置页**切节**（v3.65 / NT-062）：重绑契约 + 刷左树高亮（左树子项 = 节锚点）。</summary>
    private void OnSettingsSectionChanged(object? sender, EventArgs e)
    {
        var tab = ActiveTab;
        if (tab == null || !ReferenceEquals(tab.ViewModel, _settingsVm)) return;
        BindContracts(tab);
        UpdateTreeActive();
        EnsureActiveGroupExpanded(tab);
    }

    /// <summary>页面状态变了 → 刷新底部状态栏。</summary>
    private void OnStatusChanged(object? sender, EventArgs e)
    {
        StatusBarText = _statusProvider?.StatusText ?? "";
        RefreshStatusAction();     // 状态文本与「可点击目标」同生同灭（如 `⚠ 未找到 ffmpeg`）
    }

    private void OnDetailChanged(object? sender, EventArgs e)
    {
        if (_detailProvider != null && ActiveTab != null)
            RefreshDetail(ActiveTab.Key, _detailProvider);
    }

    private void RefreshDetail(string pageKey, IDetailPanelProvider p)
    {
        IsDetailPanelVisible = p.HasDetailPanel && p.IsDetailOpen;

        // 两轨择一（NT-054 需求 §4.5.1）：
        //   ① 页面实现 IDetailCardSource 且 Card 非空 → 走声明式（DetailCardHost 统一渲染），
        //      ⛔ 此时**不碰** p.DetailContent（否则白建一个自绘视图）；
        //   ② 否则退回老路（页面自绘 DetailContent），直到该页完成 L2 迁移。
        var card = (p as IDetailCardSource)?.Card;
        IsDetailCardActive = card is not null;
        DetailCard = card;
        DetailCardSource = card is null ? null : p;
        DetailPanelContent = card is null ? p.DetailContent : null;

        _cols.SaveFlag(pageKey, IColumnLayoutService.FlagDetailPanel, p.IsDetailOpen);
    }

    // ==================== 左树构建 / 高亮 ====================

    /// <summary>左树构建 / 高亮（NT-005 起 public：热加载重扫后要能重建）。</summary>
    public void BuildTree()
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
        var key = ActiveTreeKey;   // v3.65：设置页 → 当前节锚点（左树子项是节，不是页 Key）
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

        var key = ActiveTreeKey;   // v3.65：设置页按**当前节锚点**找所属分组
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
            return new TabItemViewModel(HomeKey, "首页", "🏠", home, home.DataContext,
                                        canClose: false, showInTabStrip: false);
        }

        // v3.65 / NT-062：应用级设置页（**一页多节**）—— 与首页同构：从 DI 取 View + VM。
        // ⛔ 不走菜单项解析：`settings` 是**分组**键，压根没有对应的 AppMenuItem。
        if (string.Equals(key, SettingsKey, StringComparison.Ordinal))
        {
            var settings = _sp.GetRequiredService<SettingsView>();
            if (settings.DataContext == null)
                settings.DataContext = _sp.GetRequiredService<SettingsViewModel>();
            return new TabItemViewModel(SettingsKey, "设置", "⚙", settings, settings.DataContext, canClose: true);
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
