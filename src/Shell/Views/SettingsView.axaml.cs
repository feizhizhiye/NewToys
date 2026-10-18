using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using NewToys.Shell.Models;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

/// <summary>
/// 应用级设置页（v3.65 / NT-062，需求 §4.9.2）：见 <c>SettingsView.axaml</c> 顶部注释。
/// <para>
/// 🔴 v3.66 第三 / 四轮：**树状（手风琴）+ 节点抬头磁吸** —— 整页是一棵"树"，每个节点 = 抬头 + 正文，
/// 整体参与滚动；页面**没有标题栏**，靠一个**浮层抬头**把"当前节点"钉在顶部。
/// </para>
/// <para>
/// 🔴 v3.68：浮层从"滚动内容里的钉子户（反补滚动量）"改为**ScrollViewer 的兄弟覆盖层**。
/// 原来那套 <c>RenderTransform = Offset.Y + push</c> 的补偿是推算，实测三种症状：滚到顶部残留空白浮层、
/// 顶部抬头被裁一半、抬头与正文错位。覆盖层由布局自己定位 ⇒ 位置恒正确，代码里再也没有滚动量补偿。
/// </para>
/// <para>
/// 折叠 / 展开的**意图**（用户点了 / 全部收起 / 外部寻址）归 VM；视口相关的一切（磁吸 / Push / 激活色 /
/// 滚动自动展开）只在本文件，⛔ VM 不认识视口。
/// </para>
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>防重入：展开会改布局、改 <c>Offset</c> 又会触发属性通知。</summary>
    private bool _syncing;

    /// <summary>重入期间又来了新通知 ⇒ 收尾后再算一轮（⛔ 别漏掉展开节点引起的那次 <c>Extent</c> 变化）。</summary>
    private bool _pending;

    /// <summary>重入期间攒下的「这次算不算用户往下滚」（收尾补算时沿用）。</summary>
    private bool _pendingScrollDown;

    /// <summary>上一次的滚动量：用来判定"这一轮是**用户往下滚**，还是内容变短引起的 clamp"。</summary>
    private double _lastOffsetY;

    /// <summary>待定位的节点（v3.67）：布局稳定后由 <see cref="TryPerformScroll"/> 消费。</summary>
    private SettingsSection? _scrollTarget;

    /// <summary>定位重试次数（页面刚创建时容器 / Extent 还没就绪，得等几帧；⛔ 无上限会卡死）。</summary>
    private int _scrollTries;

    /// <summary>当前 VM（用于挂 / 摘 <c>ScrollRequested</c>）。</summary>
    private SettingsViewModel? _vm;

    /// <summary>浮层的 Push 位移（纯视觉：下一个节点顶上来时把它推出去，⛔ 不影响布局）。</summary>
    private readonly TranslateTransform _stuckShift = new();

    /// <summary>一个节点在**视口坐标系**里的实测信息（每轮滚动算一次；<see cref="Top"/> 已扣掉滚动量）。</summary>
    private readonly record struct Row(
        SettingsSection Section, Control Container, double Top, double Height, Button? Head)
    {
        public double Bottom => Top + Height;
    }

    public SettingsView()
    {
        InitializeComponent();
        StuckHeader.RenderTransform = _stuckShift;

        // 🔴 关键（v3.66 第十四轮，崩溃根因）：浮层抬头的按钮绑的是
        //    `CommandParameter="{Binding}"` + `Command = ToggleSectionCommand`（要求 `SettingsSection`）。
        //    浮层在 code-behind 赋值之前，`DataContext` 会**继承父级 = SettingsViewModel** ⇒
        //    Avalonia 在它 attach 到逻辑树时调 `CanExecute(SettingsViewModel)` ⇒ **抛 ArgumentException**
        //    ⇒ 异常发生在"把本页挂进内容区"的过程中，导致**整页挂载中断**。
        //    这里显式置 null：**显式值优先于继承** ⇒ 在 code-behind 真正赋值之前，参数是 null（RelayCommand 接受）。
        StuckHeader.DataContext = null;

        DataContextChanged += OnDataContextChanged;
        SectionScroll.LayoutUpdated += OnLayoutUpdated;
        // 滚动驱动：盯 `Offset` / `Extent` / `Viewport`（⛔ 不用 ScrollChanged 事件，属性通知更稳）
        SectionScroll.PropertyChanged += OnSectionScrollPropertyChanged;
        // 浮层在 ScrollViewer 外面 ⇒ 滚轮事件到不了它，这里手动转发（否则"鼠标停在顶部滚不动"）
        StuckHeader.PointerWheelChanged += OnStuckHeaderWheel;
    }

    /// <summary>VM 换人（标签页切来切去 / 重建）⇒ 重挂定位事件。</summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.ScrollRequested -= OnScrollRequested;
        _vm = DataContext as SettingsViewModel;
        if (_vm != null) _vm.ScrollRequested += OnScrollRequested;
    }

    /// <summary>VM 请求定位（外部寻址 / 点节点抬头）⇒ 记下来，交给布局回调去算。</summary>
    private void OnScrollRequested(SettingsSection section)
    {
        _scrollTarget = section;
        _scrollTries = 0;
        TryPerformScroll();   // 布局已稳定时（纯滚动场景）这一次就能成
    }

    /// <summary>每次布局：有待定位就再试一次（折叠/展开会改高度 ⇒ 必须多轮收敛）。</summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_scrollTarget != null) TryPerformScroll();
    }

    /// <summary>
    /// 把待定位节点的**抬头贴到视口顶部**。
    /// <para>
    /// 换算：<c>TranslatePoint(→SectionScroll)</c> 给的是**视口坐标**（已扣掉滚动量）⇒
    /// 目标 offset = 当前 offset + 该 Y。设完 <c>Offset</c> 会触发 <see cref="SyncStickyHeader(bool)"/>。
    /// </para>
    /// <para>⚠️ 收敛：目标节展开后高度变化 ⇒ 一次算不准。故每帧重试，最多 <c>8</c> 次；差值 &lt; 0.5px 视为到位。</para>
    /// </summary>
    private void TryPerformScroll()
    {
        var target = _scrollTarget;
        if (target == null) return;
        if (DataContext is not SettingsViewModel vm) { _scrollTarget = null; return; }

        var index = vm.Sections.IndexOf(target);
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

    /// <summary>滚动 / 内容高度 / 视口尺寸变了 ⇒ 重算「磁吸节点 / 激活色 / 自动展开」。</summary>
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
    /// 一轮滚动 = 「钉住当前节点抬头（含 Push）+ 上激活色 + 把滚到的节点展开」。
    /// ⚠️ 展开是幂等的、节点数有限 ⇒ 必然收敛，不会死循环。
    /// </summary>
    /// <param name="scrolledDown">这一轮是不是"用户往下滚"（只有它是 true 才自动展开）。</param>
    private void SyncStickyHeader(bool scrolledDown = false)
    {
        if (_syncing) { _pending = true; _pendingScrollDown |= scrolledDown; return; }
        if (DataContext is not SettingsViewModel vm) return;

        _syncing = true;
        _pendingScrollDown = false;
        try
        {
            var viewport = SectionScroll.Viewport.Height;
            if (viewport <= 0) return;

            var rows = MeasureRows();
            if (rows.Count == 0) return;

            // ---- 1) 磁吸节点 = 抬头已经滚到视口顶沿之上、但节点还没滚完的那个（取最后一个） ----
            //    ⚠️ 用 -0.5 而不是 0：Offset=0（真·顶部）时首节点 Top=12>0 ⇒ 浮层必然不显示
            //    （原来 `Top <= 0` 在浮点/边距边界上会把首节点判成"钉住" ⇒ 顶部多出一个空白浮层）。
            var stuck = rows.LastOrDefault(r => r.Top < -0.5 && r.Bottom > 0.5);
            ApplyStuckHeader(stuck, rows);

            // ---- 2) 自动展开：往下滚时，视口**上半部**里最靠上、还没展开的那一节，每轮只开一个 ----
            //    🔴 v3.68：不再要求 `Top >= 0` —— 抬头刚被滚过顶沿就再也等不到（用户实测"向下滚动不自动展开"）；
            //    也不再"相交就开"（一次滚很远会连开多节、页面猛跳）。一轮一个 ⇒ 高度只增、视觉平滑。
            if (!scrolledDown) return;

            // 到底了就别再用"上半部"卡它：最后一节可能永远升不到上半部（下面没内容可滚了）⇒ 放宽到整个视口。
            var maxOffset = Math.Max(0, SectionScroll.Extent.Height - SectionScroll.Viewport.Height);
            var band = SectionScroll.Offset.Y >= maxOffset - 1 ? viewport : viewport * 0.5;
            foreach (var row in rows)
            {
                if (row.Section.IsExpanded) continue;
                if (row.Top > band || row.Bottom <= 0.5) continue;
                vm.ExpandSection(row.Section);
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
    /// 显示 / 移动浮层抬头：datacontext 换成被钉住的那一节、横向对齐到卡片、Push 位移、上激活色。
    /// <para>⛔ 不再让正身抬头 <c>Opacity=0</c>：被钉住的那节抬头本来就已经滚到视口顶沿之上，
    /// 浮层是**不透明**的且正好盖住那一截，不需要"让位"（那套是滚动内容内方案才需要的）。</para>
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
                var margin = StuckHeader.Margin;
                if (Math.Abs(margin.Left - left) > 0.5 || Math.Abs(margin.Right - right) > 0.5)
                    StuckHeader.Margin = new Thickness(left, 0, right, 0);
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

        // 激活色只给"被钉住的那个"（纯视觉，⛔ 不动 VM 的 ActiveSection）
        foreach (var row in rows)
            row.Section.IsActive = hasStuck && ReferenceEquals(row.Section, stuck.Section);
    }

    /// <summary>节点抬头 = 模板里带 <c>sec-head</c> 类的那个按钮。</summary>
    private static Button? FindHeader(Control container)
        => container.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(x => x.Classes.Contains("sec-head"));
}
