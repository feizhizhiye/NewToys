using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using NewToys.UI.Settings;

namespace NewToys.UI.Controls;

/// <summary>
/// 设置页骨架（NT-068）：<b>应用级设置页 + 4 个模块设置页共用</b>。
/// <para>
/// 整页是一棵"树"（手风琴）：每个节 = 抬头 + 正文，整体参与滚动；<see cref="Sticky"/>=true 时
/// 抬头滚出顶沿由**覆盖层浮层**钉在顶部（含 Push），并可「全部收起」。
/// </para>
/// <para>
/// 🔴 本控件承载**全部视口相关逻辑**（折叠 / 磁吸 / Push / 滚动自动展开 / 定位收敛），
/// ⛔ VM 不认识视口。折叠意图通过 <see cref="SectionToggled"/> 抛给宿主页（应用级设置页用它刷新状态栏聚合），
/// 模块设置页**零订阅**也能正常工作。
/// </para>
/// <para>
/// 🔴 两种用法：
/// <list type="bullet">
///   <item>**应用级** = 代码构节 + <c>ItemsSource</c> 绑定 + <c>ToolbarTemplate</c>（节内工具条）；</item>
///   <item>**模块级** = XAML 直接写子元素（<see cref="Sections"/> 是内容属性）+ <c>Sticky="False"</c>。</item>
/// </list>
/// </para>
/// </summary>
public partial class SettingsSectionHost : UserControl
{
    /// <summary>防重入：展开会改布局、改 <c>Offset</c> 又会触发属性通知。</summary>
    private bool _syncing;

    /// <summary>重入期间又来了新通知 ⇒ 收尾后再算一轮（⛔ 别漏掉展开节点引起的那次 <c>Extent</c> 变化）。</summary>
    private bool _pending;

    /// <summary>重入期间攒下的「这次算不算用户往下滚」（收尾补算时沿用）。</summary>
    private bool _pendingScrollDown;

    /// <summary>上一次的滚动量：用来判定"这一轮是**用户往下滚**，还是内容变短引起的 clamp"。</summary>
    private double _lastOffsetY;

    /// <summary>待定位的节：布局稳定后由 <see cref="TryPerformScroll"/> 消费。</summary>
    private SettingsSection? _scrollTarget;

    /// <summary>定位重试次数（页面刚创建时容器 / Extent 还没就绪，得等几帧；⛔ 无上限会卡死）。</summary>
    private int _scrollTries;

    /// <summary>浮层的 Push 位移（纯视觉：下一个节点顶上来时把它推出去，⛔ 不影响布局）。</summary>
    private readonly TranslateTransform _stuckShift = new();

    /// <summary>已挂 <c>CollectionChanged</c> 的外部 <see cref="ItemsSource"/>（换源时摘掉，⛔ 不泄漏）。</summary>
    private INotifyCollectionChanged? _watchedSource;

    /// <summary>已挂 <c>PropertyChanged</c> 的节（换 ItemsSource 时摘干净，⛔ 不泄漏）。</summary>
    private readonly List<SettingsSection> _watched = new();

    /// <summary>一个节点在**视口坐标系**里的实测信息（每轮滚动算一次；<see cref="Top"/> 已扣掉滚动量）。</summary>
    private readonly record struct Row(
        SettingsSection Section, Control Container, double Top, double Height, Button? Head)
    {
        public double Bottom => Top + Height;
    }

    public SettingsSectionHost()
    {
        InitializeComponent();
        StuckHeader.RenderTransform = _stuckShift;

        // 🔴 关键（v3.66 第十四轮，崩溃根因）：浮层抬头的按钮绑的是 `CommandParameter="{Binding}"`，
        //    浮层在 code-behind 赋值之前，`DataContext` 会**继承父级 = 页面 VM** ⇒ Avalonia 在它 attach 到
        //    逻辑树时按 VM 求值 ⇒ 抛异常 ⇒ **整页挂载中断**。显式置 null：显式值优先于继承。
        StuckHeader.DataContext = null;

        Sections.CollectionChanged += OnSectionsChanged;
        DataContextChanged += (_, _) => SyncContentDataContext();
        SectionScroll.LayoutUpdated += OnLayoutUpdated;
        // 滚动驱动：盯 `Offset` / `Extent` / `Viewport`（⛔ 不用 ScrollChanged 事件，属性通知更稳）
        SectionScroll.PropertyChanged += OnSectionScrollPropertyChanged;
        // 浮层在 ScrollViewer 外面 ⇒ 滚轮事件到不了它，这里手动转发（否则"鼠标停在顶部滚不动"）
        StuckHeader.PointerWheelChanged += OnStuckHeaderWheel;

        ApplyStickyMode();
        ApplyItemsSource();
    }

    // ==================== 公开属性 / API ====================

    /// <summary>节的来源（绑定模式；为 null 时用 <see cref="Sections"/>）。</summary>
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SettingsSectionHost, IEnumerable?>(nameof(ItemsSource));

    /// <summary>
    /// 是否启用**磁吸抬头 + 滚动自动展开 + 全部收起**（默认 true）。
    /// <para>模块设置页只有 2~3 节 ⇒ 置 false：不挂浮层、不自动展开，只保留点抬头开收。</para>
    /// </summary>
    public static readonly StyledProperty<bool> StickyProperty =
        AvaloniaProperty.Register<SettingsSectionHost, bool>(nameof(Sticky), true);

    /// <summary>
    /// 节内工具条的渲染模板（DataContext = 节的 <see cref="SettingsSection.ViewModel"/>）。
    /// <para>由宿主页给：Shell 传 <c>ToolbarHost</c>；模块页不给 ⇒ 有工具条的节也渲染为空。</para>
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> ToolbarTemplateProperty =
        AvaloniaProperty.Register<SettingsSectionHost, IDataTemplate?>(nameof(ToolbarTemplate));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool Sticky
    {
        get => GetValue(StickyProperty);
        set => SetValue(StickyProperty, value);
    }

    public IDataTemplate? ToolbarTemplate
    {
        get => GetValue(ToolbarTemplateProperty);
        set => SetValue(ToolbarTemplateProperty, value);
    }

    /// <summary>
    /// 节集合（模块设置页在 XAML 里用**属性元素**写：<c>&lt;ui:SettingsSectionHost.Sections&gt;</c>）。
    /// <para>
    /// 🔴 **⛔ 不要给本属性加 <c>[Content]</c>**：会抢掉 `UserControl.Content`，本控件自己的 axaml 根
    /// `<Panel x:Name="PageRoot">` 就会被当成"一个节"塞进来 ⇒ 编译报
    /// <c>AVLN3000: Unable to find suitable setter or adder for property Sections … for argument Panel</c>。
    /// </para>
    /// </summary>
    public ObservableCollection<SettingsSection> Sections { get; } = new();

    /// <summary>任一节折叠 / 展开（含「全部收起」与滚动自动展开）后触发；<c>null</c> = 整批变化。</summary>
    public event EventHandler<SettingsSection?>? SectionToggled;

    /// <summary>
    /// 把某节滚到视口顶部（含收敛：展开后高度会变，最多 8 帧收敛）。
    /// <para>应用级设置页由 <c>ScrollRequested</c>（外部寻址 / 搜索）调它。</para>
    /// </summary>
    public void ScrollTo(SettingsSection section)
    {
        _scrollTarget = section;
        _scrollTries = 0;
        TryPerformScroll();   // 布局已稳定时（纯滚动场景）这一次就能成
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            ApplyItemsSource();
        }
        else if (change.Property == StickyProperty)
        {
            ApplyStickyMode();
            SyncStickyHeader();
        }
    }

    // ==================== 数据源 ====================

    /// <summary>当前生效的节列表（<see cref="ItemsSource"/> 优先）。每次取新快照 ⇒ 与视觉树顺序一致。</summary>
    private List<SettingsSection> Items
        => (ItemsSource ?? Sections).Cast<object?>().OfType<SettingsSection>().ToList();

    private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyItemsSource();

    /// <summary>
    /// 把节列表推给 <c>ItemsControl</c> 并重挂 <c>PropertyChanged</c>（"全部收起"文案要跟着单节开收变）。
    /// </summary>
    private void ApplyItemsSource()
    {
        if (SectionList is null) return;

        var items = ItemsSource ?? Sections;
        if (!ReferenceEquals(SectionList.ItemsSource, items)) SectionList.ItemsSource = items;

        // 外部集合（VM 的 ObservableCollection）后来增删节 ⇒ 重挂一遍
        var source = ReferenceEquals(items, Sections) ? null : items as INotifyCollectionChanged;
        if (!ReferenceEquals(_watchedSource, source))
        {
            if (_watchedSource != null) _watchedSource.CollectionChanged -= OnSectionsChanged;
            _watchedSource = source;
            if (_watchedSource != null) _watchedSource.CollectionChanged += OnSectionsChanged;
        }

        foreach (var s in _watched) s.PropertyChanged -= OnSectionPropertyChanged;
        _watched.Clear();
        foreach (var s in Items)
        {
            s.PropertyChanged += OnSectionPropertyChanged;
            _watched.Add(s);
        }

        UpdateToggleAllText();
        SyncContentDataContext();
    }

    /// <summary>
    /// 给**没有自己 DataContext** 的节内容补上页面的 DataContext。
    /// <para>
    /// 🔴 为什么必须补：节内容的父级是 <c>ContentControl</c>，它的 DataContext = **节模型**（要解析
    /// <c>{Binding Content}</c>）⇒ 模块设置页在 XAML 里写的 <c>StackPanel</c> 会**继承到节模型**，
    /// 页面上 <c>{Binding 某个设置项}</c> 全部落空（静默空白）。
    /// </para>
    /// <para>
    /// ⚠️ 应用级设置页的节 View 在构建时**已显式设了自己的 VM** ⇒ 这里跳过（<c>DataContext: null</c> 才补），
    /// 不会把节的 VM 冲成页面 VM。
    /// </para>
    /// </summary>
    private void SyncContentDataContext()
    {
        if (DataContext is not { } vm) return;
        foreach (var s in Items)
        {
            if (s.Content is { DataContext: null } content)
                content.DataContext = vm;
        }
    }

    private void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsSection.IsExpanded)) UpdateToggleAllText();
    }

    /// <summary>
    /// 抬头右上角按钮的文案：**只要还有节点开着** ⇒「全部收起」，一个都没开 ⇒「全部展开」。
    /// <para>⚠️ 不用"全部开着才算收起"——那会让"部分展开"状态下要先点两次才能全收，
    /// 而真实诉求是**一键全收**（一屏看全所有抬头再点进去）。文案与动作始终一致。</para>
    /// </summary>
    private void UpdateToggleAllText()
    {
        if (ToggleAllButton is null) return;
        ToggleAllButton.Content = Items.Any(s => s.IsExpanded) ? "全部收起" : "全部展开";
    }

    /// <summary><see cref="Sticky"/>=false ⇒ 浮层（含「全部收起」）整块不参与，也不做滚动自动展开。</summary>
    private void ApplyStickyMode()
    {
        if (StuckHeader is null) return;
        ToggleAllButton.IsVisible = Sticky;
        if (!Sticky)
        {
            StuckHeader.IsVisible = false;
            _stuckShift.Y = 0;
        }
    }

    // ==================== 折叠 / 全部收起 ====================

    /// <summary>点抬头 = 折叠 / 展开本节（展开时滚到视口顶部，否则浮层还钉着上一节）。</summary>
    private void OnHeaderClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SettingsSection section) return;

        section.IsExpanded = !section.IsExpanded;
        section.UserCollapsed = !section.IsExpanded;
        section.CollapsedByAll = false;   // 手动点过 = 表态明确，不再算「全部收起」的临时态

        if (section.IsExpanded) ScrollTo(section);
        UpdateToggleAllText();
        SectionToggled?.Invoke(this, section);
    }

    /// <summary>
    /// 一键全收 / 全开：全收 = 每节记 <see cref="SettingsSection.UserCollapsed"/>（用户表态"我全收起了"）
    /// ⇒ 之后滚动不再自动展开，页面只剩 N 条抬头；全开 = 清标记 + 全展开。
    /// <para>⚠️ 全收**不滚动**：内容变短时 offset 会被自动 clamp，无需补偿。</para>
    /// </summary>
    private void OnToggleAllClick(object? sender, RoutedEventArgs e)
    {
        // 只要还有节点开着 ⇒ 全部收起（一键看全）；一个都没开 ⇒ 全部展开
        var collapse = Items.Any(s => s.IsExpanded);
        foreach (var s in Items)
        {
            s.IsExpanded = !collapse;
            s.UserCollapsed = collapse;
            s.CollapsedByAll = collapse;   // 全部收起只是"总览态"，滚动到哪节照样展开哪节
        }
        UpdateToggleAllText();
        SectionToggled?.Invoke(this, null);
    }

    /// <summary>
    /// 滚动让某节**进入视口** ⇒ 自动展开（只自动展开、⛔ 不自动折叠：折叠会减高度导致滚动位置来回跳）。
    /// <para>⚠️ 单节手动收起（<see cref="SettingsSection.UserCollapsed"/>）跳过；「全部收起」留下的临时态放行。</para>
    /// </summary>
    private void ExpandAuto(SettingsSection section)
    {
        if (section.IsExpanded) return;
        if (section.UserCollapsed && !section.CollapsedByAll) return;

        section.UserCollapsed = false;
        section.CollapsedByAll = false;
        section.IsExpanded = true;
        UpdateToggleAllText();
        SectionToggled?.Invoke(this, section);
    }

    // ==================== 定位 ====================

    /// <summary>每次布局：有待定位就再试一次（折叠/展开会改高度 ⇒ 必须多轮收敛）。</summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_scrollTarget != null) TryPerformScroll();
    }

    /// <summary>
    /// 把待定位节的**抬头贴到视口顶部**：<c>TranslatePoint</c> 给视口坐标 ⇒ 目标 offset = 当前 offset + Y。
    /// <para>⚠️ 收敛：目标节展开后高度变化 ⇒ 一次算不准。每帧重试，最多 8 次；差值 &lt; 0.5px 视为到位。</para>
    /// </summary>
    private void TryPerformScroll()
    {
        var target = _scrollTarget;
        if (target == null) return;

        var index = Items.IndexOf(target);
        if (index < 0) { _scrollTarget = null; return; }

        // 页面刚建：容器还没生成 / 内容还没量到 ⇒ 等下一帧
        if (SectionList.ContainerFromIndex(index) is not Control container || SectionScroll.Extent.Height <= 0)
        {
            if (++_scrollTries > 8) _scrollTarget = null;
            return;
        }

        var y = container.TranslatePoint(new Point(0, 0), SectionScroll)?.Y;
        if (y is null) return;

        var max = Math.Max(0, SectionScroll.Extent.Height - SectionScroll.Viewport.Height);
        var desired = Math.Clamp(SectionScroll.Offset.Y + y.Value, 0, max);

        if (Math.Abs(desired - SectionScroll.Offset.Y) < 0.5) { _scrollTarget = null; return; }

        SectionScroll.Offset = new Vector(0, desired);
        if (++_scrollTries > 8) _scrollTarget = null;
    }

    // ==================== 磁吸 ====================

    /// <summary>滚动 / 内容高度 / 视口尺寸变了 ⇒ 重算「磁吸节点 / 自动展开」。</summary>
    private void OnSectionScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 🔴 只有**用户往下滚**（Offset 变大）才允许自动展开：内容变短会让 Offset 被 clamp（变小），
        //    那不是"往下滚" —— 否则点「全部收起」的瞬间 Offset 回弹，顶部那节立刻被弹开，全收白点。
        var scrolledDown = false;
        if (e.Property == ScrollViewer.OffsetProperty)
        {
            var y = SectionScroll.Offset.Y;
            scrolledDown = y > _lastOffsetY + 0.5;
            _lastOffsetY = y;
        }

        if (e.Property == ScrollViewer.OffsetProperty
            || e.Property == ScrollViewer.ExtentProperty
            || e.Property == ScrollViewer.ViewportProperty)
            SyncStickyHeader(scrolledDown);
    }

    /// <summary>浮层上的滚轮 ⇒ 转给 ScrollViewer（浮层是覆盖层，事件默认到不了下面的 ScrollViewer）。</summary>
    private void OnStuckHeaderWheel(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (Math.Abs(delta) < 0.001) return;

        var max = Math.Max(0, SectionScroll.Extent.Height - SectionScroll.Viewport.Height);
        SectionScroll.Offset = new Vector(0,
            Math.Clamp(SectionScroll.Offset.Y - delta * 48, 0, max));
        e.Handled = true;
    }

    /// <summary>
    /// 一轮滚动 = 「钉住当前节点抬头（含 Push）+ 把滚到的节点展开」。
    /// ⚠️ 展开是幂等的、节点数有限 ⇒ 必然收敛，不会死循环。
    /// </summary>
    /// <param name="scrolledDown">这一轮是不是"用户往下滚"（只有它是 true 才自动展开）。</param>
    private void SyncStickyHeader(bool scrolledDown = false)
    {
        if (_syncing) { _pending = true; _pendingScrollDown |= scrolledDown; return; }
        if (!Sticky || SectionScroll is null) return;

        _syncing = true;
        _pendingScrollDown = false;
        try
        {
            var viewport = SectionScroll.Viewport.Height;
            if (viewport <= 0) return;

            var rows = MeasureRows();
            if (rows.Count == 0) return;

            // ---- 1) 磁吸节点 = 抬头已滚到视口顶沿之上、但节点还没滚完的那个（取最后一个） ----
            //    🔴 **真·顶部（Offset≈0）一律不挂浮层**：首节 Margin=12 在高 DPI / 取整下可能被算成 ≈0 或负值
            //    ⇒ 顶部残留一条带底色的抬头（看着像"标题栏没留空白"）。加 Offset 硬门槛：没滚出去就没有被顶出去的东西。
            var stuck = SectionScroll.Offset.Y <= 0.5
                ? default(Row)
                : rows.LastOrDefault(r => r.Top < -0.5 && r.Bottom > 0.5);
            ApplyStuckHeader(stuck, rows);

            // ---- 2) 自动展开：往下滚时，视口**上半部**里最靠上、还没展开的那一节，每轮只开一个 ----
            if (!scrolledDown) return;

            // 到底了就别再用"上半部"卡它：最后一节可能永远升不到上半部 ⇒ 放宽到整个视口。
            var maxOffset = Math.Max(0, SectionScroll.Extent.Height - SectionScroll.Viewport.Height);
            var band = SectionScroll.Offset.Y >= maxOffset - 1 ? viewport : viewport * 0.5;
            foreach (var row in rows)
            {
                if (row.Section.IsExpanded) continue;
                if (row.Top > band || row.Bottom <= 0.5) continue;
                ExpandAuto(row.Section);
                break;
            }
        }
        finally
        {
            _syncing = false;
            if (_pending)
            {
                var again = _pendingScrollDown;
                _pending = false;
                SyncStickyHeader(again);   // 重入期间攒下的通知，收尾补算
            }
        }
    }

    /// <summary>量一遍所有节点在视口坐标系里的位置（ItemsControl 不虚拟化，容器都在）。</summary>
    private List<Row> MeasureRows()
    {
        var rows = new List<Row>(SectionList.ItemCount);
        for (var i = 0; i < SectionList.ItemCount; i++)
        {
            if (SectionList.ContainerFromIndex(i) is not Control container) continue;
            if (container.DataContext is not SettingsSection section) continue;

            var top = container.TranslatePoint(new Point(0, 0), SectionScroll)?.Y ?? 0;
            rows.Add(new Row(section, container, top, container.Bounds.Height, FindHeader(container)));
        }
        return rows;
    }

    /// <summary>
    /// 显示 / 移动浮层抬头：DataContext 换成被钉住的那一节、横向对齐到卡片、Push 位移。
    /// <para>⛔ 不再让正身抬头 <c>Opacity=0</c>：被钉住的那节抬头本来就已经滚到视口顶沿之上，
    /// 浮层是**不透明**的且正好盖住那一截，不需要"让位"。</para>
    /// </summary>
    private void ApplyStuckHeader(Row stuck, List<Row> rows)
    {
        var hasStuck = stuck.Section != null && stuck.Head != null;
        StuckHeader.IsVisible = hasStuck;

        if (hasStuck)
        {
            if (!ReferenceEquals(StuckHeader.DataContext, stuck.Section))
                StuckHeader.DataContext = stuck.Section;

            // 横向对齐：卡片在 X 方向不随滚动变化 ⇒ 只在真的变了才写（避免每帧触发一轮布局）
            var pos = stuck.Container.TranslatePoint(new Point(0, 0), PageRoot);
            if (pos is not null)
            {
                var left = pos.Value.X;
                var right = Math.Max(0, PageRoot.Bounds.Width - left - stuck.Container.Bounds.Width);
                // 顶部留白跟着卡片走（卡片本身有 12px 上边距）——
                // 滚出去 12px 之前浮层与卡片同高，之后才贴视口顶沿 ⇒ 不会"咔"一下跳 12px。
                var top = Math.Clamp(12 - SectionScroll.Offset.Y, 0, 12);
                var margin = StuckHeader.Margin;
                if (Math.Abs(margin.Left - left) > 0.5
                    || Math.Abs(margin.Right - right) > 0.5
                    || Math.Abs(margin.Top - top) > 0.5)
                    StuckHeader.Margin = new Thickness(left, top, right, 0);
            }

            // Push：下一个节点的顶边进入"抬头高"之内 ⇒ 浮层按 (它的顶 − 抬头高) 上移（负值，滚出视口）
            var headHeight = stuck.Head!.Bounds.Height;
            var nextTop = rows.Where(r => r.Top > 0.5).Select(r => r.Top).DefaultIfEmpty(double.MaxValue).Min();
            _stuckShift.Y = headHeight > 0 && nextTop < headHeight
                ? Math.Max(-headHeight, nextTop - headHeight)
                : 0d;
        }
        else
        {
            _stuckShift.Y = 0;
        }
    }

    /// <summary>节点抬头 = 模板里带 <c>sec-head</c> 类的那个按钮。</summary>
    private static Button? FindHeader(Control container)
        => container.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(x => x.Classes.Contains("sec-head"));
}
