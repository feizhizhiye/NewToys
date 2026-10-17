using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using NewToys.Shell.Models;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

/// <summary>
/// 应用级设置页（v3.65 / NT-062，需求 §4.9.2）：见 <c>SettingsView.axaml</c> 顶部注释。
/// <para>
/// 🔴 v3.66 第三 / 四轮：**树状（手风琴）+ 节点抬头磁吸** —— 整页是一棵"树"，每个节点 = 抬头 + 正文，
/// 整体参与滚动；页面**没有标题栏**，靠一个**浮层抬头**把"当前节点"钉在顶部（树状滚动里最常见的做法）。
/// 三件事都只有"滚动"才知道，所以全在 code-behind（⛔ 不进 VM：VM 不该认识视口）：
/// <list type="number">
///   <item>**磁吸**：跨住视口顶沿的那个节点（`顶 ≤ 0 且 底 > 0`）= 当前节点 ⇒ 浮层显示它的抬头
///         （正身抬头 `Opacity=0` 让位，⛔ 不 `IsVisible=false` —— 那会抽掉高度造成跳动）；</item>
///   <item>**Push**：下一个节点的抬头进入视口时，浮层按 `它的顶 − 抬头高` 上移 ⇒ 被顶走的手感；</item>
///   <item>**自动展开**：与视口相交的节点自动展开（滚到哪展开哪）；🔴 **本页不做自动折叠** ——
///         节点高度只增不减 ⇒ 页面就是一条能一路滚到底的长列表，⛔ 不会再"折了又开"造成来回跳的循环感。
///         手动点抬头开收走 VM 的 `ToggleSectionCommand`，与滚动展开互不干扰。</item>
/// </list>
/// </para>
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>防重入：展开会改布局、改 <c>Offset</c> 又会触发属性通知。</summary>
    private bool _syncing;

    /// <summary>重入期间又来了新通知 ⇒ 收尾后再算一轮（⛔ 别漏掉展开节点引起的那次 <c>Extent</c> 变化）。</summary>
    private bool _pending;

    /// <summary>首帧是否已量到尺寸（量到就摘掉临时的 <c>LayoutUpdated</c> 监听）。</summary>
    private bool _measured;

    /// <summary>
    /// 磁吸抬头的位置补偿：它虽然画在**滚动内容里**（为了滚轮能照常滚），但要始终贴在**视口顶** ⇒
    /// 用 <c>RenderTransform</c> 平移回滚掉的量（⛔ 不能用 Margin —— 那会撑高内容、越滚越长）。
    /// </summary>
    private readonly TranslateTransform _stuckShift = new();

    /// <summary>一个节点在视口坐标系里的实测信息（每轮滚动算一次）。</summary>
    private readonly record struct Row(SettingsSection Section, Button? Head, double Top, double Bottom);

    public SettingsView()
    {
        InitializeComponent();
        StuckHeader.RenderTransform = _stuckShift;

        // 🔴 关键（v3.66 第十四轮，崩溃根因）：浮层抬头的按钮绑的是
        //    `CommandParameter="{Binding}"` + `Command = ToggleSectionCommand`（要求 `SettingsSection`）。
        //    浮层在 code-behind 赋值之前，`DataContext` 会**继承父级 = SettingsViewModel** ⇒
        //    Avalonia 在它 attach 到逻辑树时调 `CanExecute(SettingsViewModel)` ⇒ **抛 ArgumentException**
        //    （CommunityToolkit 的 RelayCommand<T> 会校验参数类型）⇒ 异常发生在"把本页挂进内容区"的过程中，
        //    导致**整页挂载中断**（症状：画面还是上一页、刷新后残缺、有时直接崩）。
        //    这里显式置 null：**显式值优先于继承** ⇒ 在 code-behind 真正赋值之前，参数是 null（RelayCommand 接受）。
        StuckHeader.DataContext = null;

        // 首帧必须算一次：否则浮层抬头 / 自动展开要等第一次滚动才生效。
        // ⚠️ `LayoutUpdated` 触发极频繁 ⇒ 只借它等**第一次量到尺寸**（⛔ 不常驻）。
        SectionScroll.LayoutUpdated += OnFirstLayout;
        // 滚动驱动：盯 `Offset` / `Extent` 两个属性（⛔ 不用 XAML 的 ScrollChanged 事件，属性通知更稳）
        SectionScroll.PropertyChanged += OnSectionScrollPropertyChanged;
    }

    /// <summary>滚动（或内容高度变化）⇒ 重算「磁吸节点 / 自动展开」。</summary>
    private void OnSectionScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty || e.Property == ScrollViewer.ExtentProperty)
            SyncStickyHeader();
    }

    /// <summary>等首帧布局：量到尺寸就摘掉这个临时监听（见 <see cref="SyncStickyHeader"/>）。</summary>
    private void OnFirstLayout(object? sender, EventArgs e)
    {
        SyncStickyHeader();
        if (_measured) SectionScroll.LayoutUpdated -= OnFirstLayout;
    }

    /// <summary>
    /// 一轮滚动 = 「钉住当前节点抬头（含 Push）+ 把滚到的节点展开」。
    /// ⚠️ 展开是幂等的、节点数有限 ⇒ 必然收敛，不会死循环。
    /// </summary>
    private void SyncStickyHeader()
    {
        if (_syncing) { _pending = true; return; }
        if (DataContext is not SettingsViewModel vm) return;

        _syncing = true;
        try
        {
            var viewport = SectionScroll.Bounds.Height;

            // ---- 1) 量：每个节点在视口坐标系里的位置 + 抬头实测高 ----
            var rows = new List<Row>();
            for (var i = 0; i < SectionList.ItemCount; i++)
            {
                if (SectionList.ContainerFromIndex(i) is not Control container) continue;
                if (container.DataContext is not SettingsSection section) continue;

                var top = container.TranslatePoint(new Point(0, 0), SectionScroll)?.Y ?? 0;
                rows.Add(new Row(section, FindHeader(container), top, top + container.Bounds.Height));
            }
            if (rows.Count == 0 || viewport <= 0) return;
            _measured = true;

            // ---- 2) 磁吸节点 = 跨住视口顶沿的那个（取最后一个） ----
            var stuck = rows.LastOrDefault(r => r.Top <= 0 && r.Bottom > 0);
            ApplyStuckHeader(stuck, rows);

            // ---- 3) 与视口相交（滚到）的节点 ⇒ 自动展开 ----
            // 🔴 v3.66 第四轮：⛔ 不再自动折叠、也不再做滚动补偿 —— 折叠会减高度、滚动就会来回跳，
            // 用户感知成"循环"。只保留"滚到就展开"⇒ 高度只增不减，一路滚到底即可。
            foreach (var row in rows)
                if (!row.Section.IsExpanded && row.Bottom > 0 && row.Top < viewport)
                    vm.ExpandSection(row.Section);
        }
        finally
        {
            _syncing = false;
            if (_pending) { _pending = false; SyncStickyHeader(); }   // 重入期间攒下的通知，收尾补算
        }
    }

    /// <summary>
    /// 把"当前节点"的抬头钉到视口顶部：浮层显示它的抬头 + 正身抬头让位（`Opacity=0`）+ 被下一个节点顶走时的上移量。
    /// </summary>
    private void ApplyStuckHeader(Row stuck, List<Row> rows)
    {
        var hasStuck = stuck.Head != null;

        StuckHeader.IsVisible = hasStuck;
        StuckHeader.DataContext = hasStuck ? stuck.Section : null;

        // Push：下一个节点的抬头进入视口（在"抬头高"之内）时，浮层按 (它的顶 − 抬头高) 上移。
        var push = 0d;
        if (hasStuck)
        {
            var headHeight = stuck.Head!.Bounds.Height;
            var nextTop = rows.Where(r => r.Top > 0).Select(r => r.Top).DefaultIfEmpty(double.MaxValue).Min();
            if (nextTop < headHeight) push = nextTop - headHeight;
        }

        // 平移回"滚掉的量" ⇒ 视觉上钉在视口顶（浮层在滚动内容里，本来会跟着滚上去）。
        _stuckShift.Y = SectionScroll.Offset.Y + push;

        // 正身抬头让位：只有"被钉住的那一个"要隐形（它在顶部露出的那截由浮层代表）。
        foreach (var row in rows)
            if (row.Head != null)
                row.Head.Opacity = hasStuck && ReferenceEquals(row.Section, stuck.Section) ? 0d : 1d;
    }

    /// <summary>节点抬头 = 模板里带 <c>sec-head</c> 类的那个按钮。</summary>
    private static Button? FindHeader(Control container)
        => container.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(x => x.Classes.Contains("sec-head"));
}
