using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Shell.Models;
using NewToys.Shell.Views;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// 应用级设置页（v3.65 / NT-062，需求 §4.9.2 / §6.5）：**一页多节**的复合 VM。
/// <para>
/// 七个节 = 启动与退出 / 主题外观 / 一键按钮 / 运行环境 / 网络 / 数据 / 版本与更新，整体竖向排列成一棵**树**（手风琴）：
/// 每个节 = **一个节点**（抬头 = ⌄/› + 图标 + 标题 + 一句话说明，点一下开 / 收；正文 = 该节的 View），
/// 整页一起滚动；页面**没有标题栏** —— 当前节点的抬头由 View 用**浮层**钉在页面顶部（磁吸 / sticky），
/// 节点与视口相交（滚到）时由 View 自动展开；🔴 本页**不自动折叠** ⇒ 高度只增不减，能一路滚到底
/// （见 <c>SettingsView.axaml.cs</c>）。
/// </para>
/// <para>
/// 🔴 v3.66 / NT-062 细节调整（用户 2026-10-16 验收前反馈，多轮）：
/// <list type="number">
///   <item>**树状滚动 + 节点抬头磁吸**：⛔ 无标题栏、⛔ 无分栏、⛔ 无节导航栏；
///         滚动判定（磁吸 / Push / 滚到即展开）全在 View 的 code-behind；</item>
///   <item>**详情卡契约取消**（⛔ 不再实现 <c>IDetailPanelProvider</c> / <c>IDetailCardSource</c>）——
///         一页多节里的"当前节"没有唯一值，右详情列无处安放；</item>
///   <item>状态栏**跨节聚合**：`⚠` 优先且跨全部节，否则取第一个**已展开**节的非空；
///         🔴 第五轮起**工具条不再上抛**（原走 <c>IToolbarOwner</c> ⇒ 顶到页面顶部）——
///         改为**在节内渲染**：节点抬头下方由 <c>SettingsView</c> 挂 <c>ToolbarHost</c>
///         （<see cref="SettingsSection.HasToolbar"/> 门控，用户要求"放回本节内容顶部"）；</item>
///   <item>**页内搜索整体删除**（页面标题栏与搜索框都撤了）⇒ 搜索入口 = 标题栏窗口按钮组左侧的搜索按钮，
///         经 <c>ShellViewModel.SearchSettingsCommand</c> 调本页 <see cref="SearchAndOpen"/>；</item>
///   <item>**「通用」节点拆成三个**（原来一页塞了启动与退出 / 更新检查 / 主题外观三组设置，
///         用户要求"拆开、删除通用"）：节锚点 = `settings-general`（启动与退出，⛔ Key 不变）+
///         新增 `settings-update` / `settings-theme`；三个节**共用同一个 <c>GeneralViewModel</c>**。</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ 本页**只做**「树节点开收 + 各节 View 承载 + 契约聚合」；⛔ 不承载任何设置项逻辑（逻辑在节自己的 VM 里）。
/// </para>
/// </summary>
public partial class SettingsViewModel : ObservableObject, IStatusProvider, IStatusActionProvider
{
    private readonly IServiceProvider _sp;

    /// <summary>应用级设置的节（顺序 = 需求 §4.9.2 的表；v3.66 第五轮：「通用」拆成前三个节）。</summary>
    public ObservableCollection<SettingsSection> Sections { get; } = new();

    /// <summary>最近一次被激活（外部寻址 / 展开）的节 —— Shell 的左树高亮 / 分组展开按它寻址。</summary>
    [ObservableProperty] private SettingsSection? _activeSection;

    /// <summary>节变化通知（Shell 据此重新探测契约：工具条 / 状态栏随节变）。</summary>
    public event EventHandler? SectionChanged;

    /// <summary>
    /// 请求把某个节点**滚到视口顶部**（v3.67）：VM 不认识视口 ⇒ 这里只发意图，
    /// 由 <c>SettingsView.axaml.cs</c> 执行（含两阶段收敛：先布局、再算 offset）。
    /// 触发源 = 外部寻址（<see cref="OpenSection"/>）+ 点节点抬头。
    /// </summary>
    public event Action<SettingsSection>? ScrollRequested;

    /// <summary>当前节锚点 Key（Shell 的左树高亮 / 分组展开按它寻址）。</summary>
    public string ActiveSectionKey => ActiveSection?.Key ?? "";

    public SettingsViewModel(IServiceProvider sp)
    {
        _sp = sp;
        BuildSections();

        // 契约聚合：订阅**每节的每个 VM**（状态栏 / 工具条随时可能变），由本页统一转成一份对外契约。
        foreach (var s in Sections)
        {
            foreach (var vm in s.ViewModels)
            {
                if (vm is IStatusProvider st) st.StatusChanged += OnSubChanged;
                if (vm is IToolbarSource tb) tb.ToolbarChanged += OnSubChanged;
            }
            // 节点开收会改变「全部收起 / 全部展开」按钮的文案（用户手点单节也要跟着变）
            s.PropertyChanged += OnSectionPropertyChanged;
        }

        var first = Sections.FirstOrDefault();
        if (first != null)
        {
            first.IsExpanded = true;
            Activate(first);
        }
    }

    // ==================== 节构建 ====================

    /// <summary>
    /// 装配七个节：每个节 = 「DI 取 View + 取 VM + 把 VM 设成 View 的 DataContext」。
    /// ⚠️ 必须先给 View 设 DataContext 再挂进内容区（DataContext 是继承属性）—— 与 Shell 的
    /// <c>HomeView</c> 同一约定。
    /// <para>🔴 v3.66 第五轮：原「通用」节的**三组设置拆成三个节**，三节**共用一个 <c>GeneralViewModel</c>**
    /// （数据同源、只是显示位置分开）⇒ 拆的是 View，⛔ 不是 VM。</para>
    /// <para>🔴 v3.67：「更新」+「关于」**合并**成「版本与更新」（8 节 → 7 节，用户拍板）——
    /// 该节由 <see cref="UpdateAboutView"/> 拼装两个子 View，<c>ViewModels</c> 有两个（状态栏 / 工具条都要聚合到）。</para>
    /// </summary>
    private void BuildSections()
    {
        // 「启动与退出 / 主题外观 / 版本与更新」三节共用这一个 VM（原「通用」页的 VM）
        var generalVm = _sp.GetRequiredService<GeneralViewModel>();

        // Key ⛔ 不变：首页收藏卡 / 收藏白名单 / Ctrl+, 都按 `settings-general` 寻址（需求 §5.1）
        Add("settings-general", "启动与退出", "⚙", "开机启动 / 托盘 / 关闭按钮行为",
            () => _sp.GetRequiredService<GeneralView>(), () => new object?[] { generalVm });

        Add("settings-theme", "主题外观", "🎨", "明暗模式与强调色",
            () => _sp.GetRequiredService<ThemeView>(), () => new object?[] { generalVm });

        Add("settings-launch", "一键按钮", "🚀", "首页三个一键按钮的程序与网址",
            () => _sp.GetRequiredService<LaunchConfigView>(), () => new object?[] { _sp.GetRequiredService<LaunchConfigViewModel>() });

        // 「运行环境」= 配置档 + 外部程序路径；进入本节才做**惰性探测**（需求 §4.9.2 第 5 条）
        var pathVm = _sp.GetRequiredService<PathViewModel>();
        Add("settings-path", "运行环境", "📁", "配置档 + 外部程序路径（ffmpeg / 播放器 / 解释器）",
            () => _sp.GetRequiredService<PathView>(), () => new object?[] { pathVm },
            onActivated: () => pathVm.ProbeExternalTools());

        Add("settings-dns", "网络", "🛡", "加密 DNS 与网络配置",
            () => _sp.GetRequiredService<DnsView>(), () => new object?[] { _sp.GetRequiredService<DnsViewModel>() });

        Add("settings-data", "数据", "💾", "备份 / 导入 config 配置",
            () => _sp.GetRequiredService<DataView>(), () => new object?[] { _sp.GetRequiredService<DataViewModel>() });

        // 🔴 v3.67（用户 2026-10-18 拍板）：原「更新」节 + 原「关于」节**合一** = 「版本与更新」
        //    （两者都围绕版本号：版本文件地址 / 检查更新 / 版本信息 / 功能清单 / 重载模块）。
        //    Key 保留 `settings-update`（原菜单 Key，⛔ 不改；收藏 / 寻址仍按它）；
        //    `settings-about` 退成**内部别名**（见 OpenSection），⛔ 不再是一个独立节点。
        var aboutVm = _sp.GetRequiredService<AboutSettingsViewModel>();
        Add("settings-update", "版本与更新", "🔄", "版本号 / 检查更新 / 功能清单 / 重载模块",
            () => _sp.GetRequiredService<UpdateAboutView>(),
            () => new object?[] { generalVm, aboutVm },
            init: v => ((UpdateAboutView)v).Init(generalVm, aboutVm));
    }

    private void Add(string key, string title, string icon, string desc,
                     Func<Control> viewFactory, Func<object?[]> vmFactory, Action? onActivated = null,
                     Action<Control>? init = null)
    {
        var view = viewFactory();
        // v3.67：一个节可以有多个 VM（合节）⇒ 契约聚合按列表逐个探测；首个 = 主 VM（工具条宿主用）。
        var vms = (vmFactory() ?? Array.Empty<object?>()).OfType<object>().ToArray();
        var primary = vms.FirstOrDefault();
        if (primary != null && view.DataContext == null) view.DataContext = primary;
        init?.Invoke(view);

        Sections.Add(new SettingsSection
        {
            Key = key, Title = title, Icon = icon, Desc = desc,
            // 本节自带工具条？（v3.66 第五轮：工具条⛔ 不再上抛到页面顶部，改由节点内嵌 ToolbarHost 渲染）
            HasToolbar = vms.Any(v => (v as IToolbarSource)?.Toolbar is { Count: > 0 }),
            Content = view, ViewModels = vms, OnActivated = onActivated
        });
    }

    // ==================== 折叠 / 激活 ====================

    /// <summary>
    /// 按节锚点 Key 激活对应节（外部寻址入口：首页跳转 / 收藏卡 / 托盘「打开设置」/
    /// 状态栏 `⚠` 跳转都走这里）：**展开该节、收起其余节**。
    /// <para>`settings`（组键本身）= 「通用」节；未知 Key ⛔ 不改动当前节。</para>
    /// </summary>
    public void OpenSection(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var target = string.Equals(key, "settings", StringComparison.Ordinal)
            ? Sections.FirstOrDefault()
            : Sections.FirstOrDefault(s => string.Equals(s.Key, ResolveKey(key), StringComparison.Ordinal));
        if (target == null) return;
        ExpandOnly(target);
    }

    /// <summary>
    /// 节锚点别名（v3.67）：「关于」已并入「版本与更新」⇒ `settings-about` 不再是一个节点，
    /// 但仍是**内部引用**（旧链接 / 文档 / 收藏可能还留着），统一指到 `settings-update`。
    /// </summary>
    private static string ResolveKey(string key)
        => string.Equals(key, "settings-about", StringComparison.Ordinal) ? "settings-update" : key;

    /// <summary>展开目标节、收起其余节，并把它设为当前节（外部寻址 / 搜索共用）。</summary>
    private void ExpandOnly(SettingsSection target)
    {
        foreach (var s in Sections)
            if (!ReferenceEquals(s, target)) s.IsExpanded = false;
        target.IsExpanded = true;
        target.UserCollapsed = false;   // v3.66 第七轮：外部寻址 / 搜索 = 明确要开它 ⇒ 清掉"手动收起"标记
        target.CollapsedByAll = false;  // v3.68：同上，清掉「全部收起」的临时态
        Activate(target);
        ScrollRequested?.Invoke(target);   // v3.67：光换节不够，还要**滚过去**（否则视口停在原处 = "跳错地方"）
    }

    /// <summary>
    /// 按**关键词**搜索节并跳过去（v3.66 / NT-062：入口 = 标题栏的搜索按钮，回车触发）：
    /// 命中节标题 / 一句话说明即算命中。返回是否命中（未命中由 Shell 给提示）。
    /// </summary>
    public bool SearchAndOpen(string keyword)
    {
        var kw = keyword?.Trim() ?? "";
        if (kw.Length == 0) return false;

        var hit = Sections.FirstOrDefault(s =>
            s.Title.Contains(kw, StringComparison.CurrentCultureIgnoreCase) ||
            s.Desc.Contains(kw, StringComparison.CurrentCultureIgnoreCase));
        if (hit == null) return false;

        ExpandOnly(hit);
        return true;
    }

    /// <summary>
    /// 点折叠块抬头 = 折叠 / 展开本节（展开时激活它，其余节**保持原状** —— 允许同时展开多节）。
    /// <para>🔴 v3.66 第七轮：手动**折叠**要"作数"—— 记 <see cref="SettingsSection.UserCollapsed"/>，
    /// 否则本节还在视口里，滚动的自动展开下一帧就把它弹开（用户实测"折叠点了没用"）。
    /// 手动**展开**则清掉这个标记。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleSection(SettingsSection? section)
    {
        if (section == null) return;
        section.IsExpanded = !section.IsExpanded;
        section.UserCollapsed = !section.IsExpanded;
        section.CollapsedByAll = false;   // v3.68：手动点过 = 表态明确，不再算「全部收起」的临时态
        if (section.IsExpanded) ActiveSection = section;
        // 折叠**也要**通知：状态栏按"哪几节展开"算（见 StatusText）。
        SectionChanged?.Invoke(this, EventArgs.Empty);
        // v3.67：点开 = 明确要看它 ⇒ 滚到视口顶部（否则它停在视口下部，磁吸浮层还钉着上一节、激活色对不上）
        if (section.IsExpanded) ScrollRequested?.Invoke(section);
    }

    // ==================== 全部收起 / 全部展开（v3.67） ====================

    /// <summary>
    /// 抬头右上角那个按钮的文案：**只要还有节点开着** ⇒「全部收起」，一个都没开 ⇒「全部展开」。
    /// <para>⚠️ 不用"全部开着才算收起"——那会让"部分展开"状态下要先点两次才能全收，
    /// 而用户的真实诉求就是**一键全收**（一屏看全 7 条抬头再点进去）。文案与点击动作始终一致。</para>
    /// </summary>
    public string ToggleAllText
        => Sections.Any(s => s.IsExpanded) ? "全部收起" : "全部展开";

    /// <summary>
    /// 一键全收 / 全开（v3.67）。
    /// <para>
    /// 全收 = 每个节点都记 <see cref="SettingsSection.UserCollapsed"/>（= 用户表态"我全收起了"）
    /// ⇒ 之后滚动**不再自动展开**，页面只剩 N 条抬头：一屏看全、点哪条跳哪条。全开 = 清标记 + 全展开。
    /// </para>
    /// <para>⚠️ 全收**不滚动**：内容变短时 <c>ScrollViewer.Offset</c> 会被自动 clamp，无需补偿。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleAllSections()
    {
        // 只要还有节点开着 ⇒ 全部收起（一键看全）；一个都没开 ⇒ 全部展开
        var collapse = Sections.Any(s => s.IsExpanded);
        foreach (var s in Sections)
        {
            s.IsExpanded = !collapse;
            s.UserCollapsed = collapse;
            s.CollapsedByAll = collapse;   // v3.68：全部收起只是"总览态"，滚动到哪节照样展开哪节
        }
        OnPropertyChanged(nameof(ToggleAllText));
        SectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>单节点开收 → 刷新「全部收起 / 全部展开」的文案。</summary>
    private void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsSection.IsExpanded))
            OnPropertyChanged(nameof(ToggleAllText));
    }

    // ==================== 滚动展开（v3.66 第四轮：树状滚动，由 View 驱动） ====================

    /// <summary>
    /// 滚动让某个节点**进入视口** ⇒ 自动展开它。🔴 本页**只自动展开、不自动折叠** ——
    /// 折叠会减高度、滚动位置就得补偿，视觉上就是"折了又开"的来回跳（用户反馈成"循环"）；
    /// 不折叠 ⇒ 高度只增不减，页面就是一条能一路滚到底的长列表。
    /// <para>⚠️ 手动收起的节点（<see cref="SettingsSection.UserCollapsed"/>）**跳过**：用户的意图优先，
    /// 否则滚到它又会被自动弹开。</para>
    /// <para>⚠️ 不改 <c>ActiveSection</c>（滚动中切换会触发各节的惰性探测 / 工具条抖动）；
    /// 但状态栏按"哪几节展开"算，故要发一次通知。</para>
    /// </summary>
    public void ExpandSection(SettingsSection? section)
    {
        if (section is not { IsExpanded: false }) return;
        // 🔴 v3.68：只有**单节手动收起**才拦；「全部收起」留下的临时态要放行，
        //    否则点了「全部收起」之后往下滚永远是空抬头（用户实测"向下滚动不能自动展开"）。
        if (section.UserCollapsed && !section.CollapsedByAll) return;
        section.UserCollapsed = false;
        section.CollapsedByAll = false;
        section.IsExpanded = true;
        SectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Activate(SettingsSection section)
    {
        ActiveSection = section;
        SectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>「运行环境」节的惰性探测：**第一次展开**时触发（重复展开 = 重新探测一次，不会累积订阅）。</summary>
    partial void OnActiveSectionChanged(SettingsSection? oldValue, SettingsSection? newValue)
    {
        if (newValue?.OnActivated == null) return;
        if (ReferenceEquals(oldValue, newValue)) return;
        newValue.OnActivated.Invoke();
    }

    // ==================== 页内搜索：已删除（v3.66） ====================
    //
    // 原「页内搜索框 + 结果列表 / 过滤节」两版都撤了 —— 入口统一搬到**标题栏**的搜索按钮
    // （`ShellView` 的 `BtnSettingsSearch` → `ShellViewModel.SearchSettingsCommand` → 本页
    //  `SearchAndOpen`）。页面顶部现在只留**冻结首行**的节导航（点一下快速折叠 / 切换）。

    // ==================== 契约：IStatusProvider（跨节聚合） ====================

    /// <summary>
    /// 状态栏文本 = 各节的聚合：**`⚠` 优先且**跨全部节**（外部程序缺失一类的警示必须看得见，
    /// 不因为它所在的那节折叠着就被吞掉），否则取**第一个已展开节**的非空状态。
    /// ⛔ 一页多节没有"当前节"，所以不能只取某一个固定节。
    /// </summary>
    public string StatusText
    {
        get
        {
            foreach (var s in Sections)
                foreach (var v in s.ViewModels)
                    if (v is IStatusProvider p
                        && p.StatusText.StartsWith("⚠", StringComparison.Ordinal))
                        return p.StatusText;

            foreach (var s in Sections.Where(x => x.IsExpanded))
                foreach (var v in s.ViewModels)
                    if (v is IStatusProvider p && !string.IsNullOrWhiteSpace(p.StatusText))
                        return p.StatusText;

            return "";
        }
    }

    public event EventHandler? StatusChanged;

    // ==================== 契约：IStatusActionProvider（状态栏 `⚠` 可点跳转） ====================

    /// <summary>状态栏 `⚠` 的跳转目标（各节里第一个给了目标 Key 的）。</summary>
    public string? StatusActionKey
        => FirstAction()?.StatusActionKey;

    public string StatusActionTip => FirstAction()?.StatusActionTip ?? "";

    private IStatusActionProvider? FirstAction()
        => Sections.SelectMany(s => s.ViewModels)
                   .OfType<IStatusActionProvider>()
                   .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.StatusActionKey));

    // ==================== 工具条：⛔ 不再上抛（v3.66 第五轮） ====================
    //
    // 原实现 = 「实现 IToolbarOwner，把承载者让给已展开且有工具条的那一节 VM」⇒ Shell 的顶部工具条行
    // 会渲染「运行环境」节的那一排按钮（保存 / 浏览 / 新建配置…）。用户反馈：**按钮跑到了页面顶部**，
    // 要求在**本节内容顶部**显示 ⇒ 改为节点内嵌：`SettingsView.axaml` 的节点模板里挂一个
    // `ToolbarHost`（`SettingsSection.HasToolbar` 门控，Source = 节 VM）。
    // 契约 `IToolbarOwner`（Shared/Services/IToolbarSource.cs）**保留**（框架层不动），当前无实现者。

    /// <summary>任一节的契约发生变化 → 转成对本页的通知（Shell 重取状态栏）。</summary>
    private void OnSubChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusActionKey));
        OnPropertyChanged(nameof(StatusActionTip));
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
