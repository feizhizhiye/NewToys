using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「资产浏览」页 code-behind：只做 View 才做得了的事 ——
/// 表头拖拽 / 测宽转发、两套视图的修饰键多选、双击打开、键盘（Enter / Delete / Ctrl+A）、
/// 弹出菜单 DataContext、以及底栏显隐的「内容是否超出视口」量测。
/// <para>🔴 选中真相在 VM（<c>SelectedRows</c>）：本类只把鼠标行为翻译成 <c>SelectedItems</c> 变化再回填；
/// VM 侧改选中（全选 / 反选 / 视图切换重建）走 <c>SelectionSyncRequested</c> 反向回填。</para>
/// </summary>
public partial class AssetBrowseView : UserControl
{
    /// <summary>双击判定窗口（毫秒）—— 同一卡片两次按下且间隔小于它算双击。</summary>
    private const int DoubleClickMs = 400;

    private AssetBrowseViewModel? _vm;

    private AssetTile? _anchor;
    private AssetTile? _lastClickTile;
    private DateTime _lastClickAt;

    private bool _syncingSelection;

    public AssetBrowseView()
    {
        InitializeComponent();

        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetBrowseViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetBrowseViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };

        foreach (var list in Lists)
        {
            if (list == null) continue;
            list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
            list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
            list.SelectionChanged += OnListSelectionChanged;
        }

        LayoutUpdated += (_, _) => ReportListOverflow();
    }

    private ListBox?[] Lists => new[] { TileList, DetailList };

    /// <summary>当前可见的那套视图（量测 / 同步选中只认它）。</summary>
    private ListBox? ActiveList => TileList?.IsVisible == true ? TileList : DetailList;

    private void ReportListOverflow()
    {
        if (DataContext is not AssetBrowseViewModel vm) return;
        var list = ActiveList;
        if (list == null) return;

        _scroller ??= list.FindDescendantOfType<ScrollViewer>();
        if (_scroller is not { } sv) return;
        vm.SetContentOverflow(sv.Extent.Height - sv.Viewport.Height > 1);
    }

    private ScrollViewer? _scroller;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null)
        {
            _vm.SelectionSyncRequested -= OnSelectionSyncRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as AssetBrowseViewModel;
        if (_vm != null)
        {
            _vm.SelectionSyncRequested += OnSelectionSyncRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        // ⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）
        foreach (var list in Lists)
            if (list?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }

    /// <summary>视图切换 / 列表重建后：新显示的列表要把选中回填成 VM 的选中集。</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssetBrowseViewModel.IsTileView) or nameof(AssetBrowseViewModel.IconSize))
        {
            _scroller = null;
            SyncSelection();
        }
    }

    // ==================== 鼠标：选中 + 双击打开 ====================

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not ListBox list) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.DataContext is not AssetTile tile) return;

        // 行要拿到键盘焦点，否则 Ctrl+A / Delete 到不了列表
        (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.Focus();
        e.Handled = true;

        if (IsDoubleClick(tile))
        {
            _vm.Selected = tile;
            SelectOnly(list, tile);
            _vm.OpenSelectedCommand.Execute(null);
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (shift) SelectRange(list, tile, additive: ctrl);
        else if (ctrl) Toggle(list, tile);
        else ReplaceWith(list, tile);

        if (!shift) _anchor = tile;
        _vm.Selected = tile;
        PushSelectionToVm(list, tile);
    }

    private bool IsDoubleClick(AssetTile tile)
    {
        var now = DateTime.UtcNow;
        var hit = ReferenceEquals(_lastClickTile, tile) && (now - _lastClickAt).TotalMilliseconds <= DoubleClickMs;
        _lastClickTile = hit ? null : tile;   // 命中后清零，避免三击连开两次
        _lastClickAt = now;
        return hit;
    }

    private void SelectRange(ListBox list, AssetTile target, bool additive)
    {
        if (_vm == null) return;
        var rows = _vm.Items.ToList();
        var to = rows.IndexOf(target);
        if (to < 0) return;

        var from = _anchor != null ? rows.IndexOf(_anchor) : to;
        if (from < 0) from = to;

        BulkUpdate(list, items =>
        {
            if (!additive) items.Clear();
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                if (!items.Contains(rows[i])) items.Add(rows[i]);
        });
    }

    private void Toggle(ListBox list, AssetTile tile)
        => BulkUpdate(list, items =>
        {
            if (items.Contains(tile)) items.Remove(tile);
            else items.Add(tile);
        });

    private void ReplaceWith(ListBox list, AssetTile tile)
        => BulkUpdate(list, items =>
        {
            items.Clear();
            items.Add(tile);
        });

    private void SelectOnly(ListBox list, AssetTile tile)
        => BulkUpdate(list, items =>
        {
            items.Clear();
            items.Add(tile);
        });

    /// <summary>
    /// 批量改 <c>SelectedItems</c>：**期间关掉自动滚动**。
    /// <para>🔴 每 <c>Add</c> 一项列表都会把该项滚进视野（虚拟化下还要建容器 + 触发布局）——
    /// 全选 / 大范围连选上百项就是上百次滚动，界面看起来像卡死。</para>
    /// </summary>
    private void BulkUpdate(ListBox list, Action<IList> mutate)
    {
        if (list.SelectedItems is not { } items) return;

        var autoScroll = list.AutoScrollToSelectedItem;
        list.AutoScrollToSelectedItem = false;
        _syncingSelection = true;
        try { mutate(items); }
        finally
        {
            _syncingSelection = false;
            list.AutoScrollToSelectedItem = autoScroll;
        }
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not ListBox list) return;
        PushSelectionToVm(list, null);
    }

    private void PushSelectionToVm(ListBox list, AssetTile? clicked)
    {
        if (_vm == null || list.SelectedItems is not { } items) return;
        _vm.SetSelectionFromView(items.OfType<AssetTile>().ToList(), clicked);
    }

    /// <summary>VM 侧改了选中 ⇒ 把**两套视图**都回填（切视图时就地生效，⛔ 不等下一次点击）。</summary>
    private void OnSelectionSyncRequested(object? sender, EventArgs e) => SyncSelection();

    private void SyncSelection()
    {
        if (_vm == null) return;
        foreach (var list in Lists)
        {
            if (list == null) continue;
            BulkUpdate(list, items =>
            {
                items.Clear();
                foreach (var row in _vm.SelectedRows)
                {
                    var tile = _vm.Items.FirstOrDefault(t => t.Row.Id == row.Id);
                    if (tile != null) items.Add(tile);
                }
            });
        }
    }

    // ==================== 键盘：Enter 打开 / Delete 移入回收站 / Ctrl+A 全选 ====================

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;

        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _vm.SelectAllCommand.Execute(null);
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _vm.OpenSelectedCommand.Execute(null);
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            _vm.TrashSelectedCommand.Execute(null);
        }
    }
}
