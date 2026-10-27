using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「全部资产」页的 code-behind：只做**View 才做得了的事**
/// （表头拖拽 / 测量转发、给 ContextMenu 补 DataContext、多选的修饰键接管、量列表是否溢出）。
/// <para>🔴 选中真相在 VM（<c>SelectedRows</c>）：本类只负责把鼠标行为翻译成
/// <c>SelectedItems</c> 的变化，再把结果回填给 VM；VM 侧改了选中（全选 / 反选 / 重建认领）
/// 走 <c>SelectionSyncRequested</c> 事件反向回填。</para>
/// </summary>
public partial class AssetAllView : UserControl
{
    private AssetAllViewModel? _vm;

    /// <summary>Shift 连选的基准行。</summary>
    private AssetRow? _anchor;

    /// <summary>程序化改 <c>SelectedItems</c> 时抑制自己的 <c>SelectionChanged</c> 回抛。</summary>
    private bool _syncingSelection;

    public AssetAllView()
    {
        InitializeComponent();

        // ① 拖表头列边界 → 转给 VM（Grid.ColumnDefinitions 不能绑，只能这样转）
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        // ② 表头首次测量完 → 让 VM 按「示例宽 ≤ 表头宽」锁列宽（v3.22 §4.8.1）
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };

        // ③ 多选：修饰键在**隧道**阶段接管（ListBox 自己在 Multiple 下单击是「追加」，
        //    与资源管理器「单击 = 只选这一行」不一致 ⇒ 无修饰键也一并接管）
        AssetList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        AssetList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        AssetList.SelectionChanged += OnListSelectionChanged;

        // ④ 底栏显隐的另一半口径 = 列表内容是否超出视口（滚动出现）。
        LayoutUpdated += (_, _) => ReportListOverflow();
    }

    /// <summary>列表滚动器的内容高 vs 视口高 → 回填 VM（超出 ⇒ 底栏才显示）。</summary>
    private void ReportListOverflow()
    {
        if (DataContext is not AssetAllViewModel vm) return;
        _listScroller ??= AssetList?.FindDescendantOfType<ScrollViewer>();
        if (_listScroller is not { } sv) return;
        vm.SetContentOverflow(sv.Extent.Height - sv.Viewport.Height > 1);
    }

    private ScrollViewer? _listScroller;

    /// <summary>
    /// 🔴 跨页取件点（规范 §4.8 硬规则 8）：别的页面请求"打开某个资产"时在这里取件并选中。
    /// ⛔ 不能放 <c>OnDataContextChanged</c> —— 切回来的那次只有可视树重挂，DataContext 不变。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is AssetAllViewModel vm)
        {
            vm.ConsumePendingAsset();
            vm.RefreshFooterVisibility();   // 兜底重算底栏显隐（时序漏推防护）
        }
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null) _vm.SelectionSyncRequested -= OnSelectionSyncRequested;
        _vm = DataContext as AssetAllViewModel;
        if (_vm != null) _vm.SelectionSyncRequested += OnSelectionSyncRequested;

        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (AssetList?.ContextMenu is { } amenu) amenu.DataContext = DataContext;
    }

    // ==================== 选中（Ctrl 增选 / Shift 连选 / Ctrl+A 全选） ====================

    /// <summary>
    /// 鼠标点行：无修饰键 = 只选这一行；`Ctrl` = 切换该行；`Shift` = 从基准行连选；
    /// `Ctrl+Shift` = 追加连选。右键不管（行右键菜单用单条 <c>Selected</c>）。
    /// </summary>
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.DataContext is not AssetRow row) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // 行要拿到键盘焦点，否则 Ctrl+A 之类的键事件到不了列表
        (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.Focus();
        e.Handled = true;

        if (shift) SelectRange(row, additive: ctrl);
        else if (ctrl) Toggle(row);
        else ReplaceWith(row);

        if (!shift) _anchor = row;
        _vm.Selected = row;          // 详情卡跟最后一次点击走
        PushSelectionToVm(row);
    }

    /// <summary>Shift 连选：从基准行到目标行整段选中；<paramref name="additive"/> = 保留原有选中（Ctrl+Shift）。</summary>
    private void SelectRange(AssetRow target, bool additive)
    {
        if (_vm == null) return;

        var rows = _vm.Items.ToList();
        var to = rows.IndexOf(target);
        if (to < 0) return;

        var from = _anchor != null ? rows.IndexOf(_anchor) : to;
        if (from < 0) from = to;

        BulkUpdateSelection(items =>
        {
            if (!additive) items.Clear();
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                if (!items.Contains(rows[i])) items.Add(rows[i]);
        });
    }

    /// <summary>Ctrl 点击：已选中就去掉，没选中就加上。</summary>
    private void Toggle(AssetRow row)
    {
        BulkUpdateSelection(items =>
        {
            if (items.Contains(row)) items.Remove(row);
            else items.Add(row);
        });
    }

    /// <summary>无修饰键：清空其余、只留这一行（资源管理器语义）。</summary>
    private void ReplaceWith(AssetRow row)
    {
        BulkUpdateSelection(items =>
        {
            items.Clear();
            items.Add(row);
        });
    }

    /// <summary>
    /// 批量改 <c>SelectedItems</c>：**期间关掉自动滚动**。
    /// <para>🔴 每 <c>Add</c> 一项 ListBox 都会把该项滚进视野（虚拟化下还要建容器 + 触发布局）——
    /// 全选 / 大范围连选上百行就是上百次滚动，界面看起来像卡死。</para>
    /// </summary>
    private void BulkUpdateSelection(Action<System.Collections.IList> mutate)
    {
        if (AssetList?.SelectedItems is not { } items) return;

        var autoScroll = AssetList.AutoScrollToSelectedItem;
        AssetList.AutoScrollToSelectedItem = false;
        _syncingSelection = true;
        try { mutate(items); }
        finally
        {
            _syncingSelection = false;
            AssetList.AutoScrollToSelectedItem = autoScroll;
        }
    }

    /// <summary>Ctrl+A：交给 VM 的全选命令（与右键「☑ 全选当前页」同一入口）。</summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        _vm?.SelectAllCommand.Execute(null);
    }

    /// <summary>列表自身的选中变化（键盘方向键 / 程序化）→ 回填 VM。</summary>
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        PushSelectionToVm(null);
    }

    private void PushSelectionToVm(AssetRow? clicked)
    {
        if (_vm == null || AssetList?.SelectedItems is not { } items) return;
        _vm.SetSelectionFromView(items.OfType<AssetRow>().ToList(), clicked);
    }

    /// <summary>VM 侧改了选中（全选 / 反选 / 清空 / 列表重建认领）⇒ 回填 SelectedItems。</summary>
    private void OnSelectionSyncRequested(object? sender, EventArgs e)
    {
        if (_vm == null) return;

        BulkUpdateSelection(items =>
        {
            items.Clear();
            foreach (var r in _vm.SelectedRows) items.Add(r);
        });
    }
}
