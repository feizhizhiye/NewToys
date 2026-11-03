using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using Avalonia;
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

    /// <summary>
    /// 🔴 跨页取件点（规范 §4.8 硬规则 8）：标签详情卡点「🎬 视频 → 资产浏览」时在这里取件，
    /// 本页切到索引结果集模式。⛔ 不能放 <c>OnDataContextChanged</c> —— 切回来只有可视树重挂。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is AssetBrowseViewModel vm) vm.ConsumePendingResultSet();
    }

    public AssetBrowseView()
    {
        InitializeComponent();

        foreach (var list in Lists)
        {
            if (list == null) continue;
            list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
            list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
            list.SelectionChanged += OnListSelectionChanged;
        }

        if (TreeList != null)
            TreeList.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);

        LayoutUpdated += (_, _) => ReportListOverflow();
    }

    private ListBox?[] Lists => new[] { TileList, DetailList };

    /// <summary>当前可见的那套视图（量测 / 同步选中只认它）。</summary>
    private ListBox? ActiveList => TileList?.IsVisible == true ? TileList : DetailList;

    private void ReportListOverflow()
    {
        if (DataContext is not AssetBrowseViewModel) return;
        var list = ActiveList;
        if (list == null) return;

        // 切视图后可见列表换了实例 ⇒ 在新列表上重放一次选中（此时它的 ItemsSource 已就位）。
        // 🔴 不能只靠 VM 的属性通知：那一瞬绑定还没更新完，新列表的源仍是 null。
        if (!ReferenceEquals(list, _lastSyncedList))
        {
            _lastSyncedList = list;
            DetachScroller();
            SyncSelection();
        }

        // 第一次布局时 ListBox 模板可能还没建出 ScrollViewer ⇒ 每轮都补找一次（找到后就只靠属性通知）
        if (_scroller == null)
        {
            _scroller = list.FindDescendantOfType<ScrollViewer>();
            if (_scroller is { } found) found.PropertyChanged += OnScrollMetricsChanged;
        }

        if (_scroller is null) return;

        // 首次 / 换列表后补报一次；后续滚动由 ScrollViewer 的属性通知驱动（见 OnScrollMetricsChanged）
        ReportViewport();
    }

    /// <summary>
    /// 滚动驱动缩略图：盯 <c>Offset</c> / <c>Extent</c> / <c>Viewport</c> 三个属性。
    /// <para>🔴 不靠 <c>LayoutUpdated</c>：滚动只改 ScrollViewer 偏移，不保证每帧都触发本页布局回调 ⇒
    /// 滚到底部时可视区从没上报过，缩略图就一直停在首屏那 36 张。</para>
    /// </summary>
    private void OnScrollMetricsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty
            && e.Property != ScrollViewer.ExtentProperty
            && e.Property != ScrollViewer.ViewportProperty) return;

        ReportViewport();
    }

    /// <summary>把当前滚动位置报给 VM（位置 / 内容高度都没变 ⇒ 不重复报）。</summary>
    private void ReportViewport()
    {
        if (_vm == null || _scroller is not { } sv) return;

        var offset = sv.Offset.Y;
        var extent = sv.Extent.Height;
        if (!double.IsNaN(_lastReportedOffset)
            && Math.Abs(offset - _lastReportedOffset) <= 1
            && Math.Abs(extent - _lastReportedExtent) <= 1) return;

        _lastReportedOffset = offset;
        _lastReportedExtent = extent;
        _vm.ReportViewport(offset, sv.Viewport.Height, extent);
        _vm.SetContentOverflow(extent - sv.Viewport.Height > 1);
    }

    private void DetachScroller()
    {
        if (_scroller != null) _scroller.PropertyChanged -= OnScrollMetricsChanged;
        _scroller = null;
        _lastReportedOffset = double.NaN;
        _lastReportedExtent = double.NaN;
    }

    /// <summary>上一次上报的滚动位置 / 内容高度（<c>NaN</c> = 下次一定上报：换视图 / 换列表后要重算窗口）。</summary>
    private double _lastReportedOffset = double.NaN;

    private double _lastReportedExtent = double.NaN;

    private ScrollViewer? _scroller;

    /// <summary>上一次把选中回放进哪个列表（切视图时变）。</summary>
    private ListBox? _lastSyncedList;

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
        if (TreeList?.ContextMenu is { } tmenu) tmenu.DataContext = DataContext;
    }

    /// <summary>
    /// 树行右键：把右键瞄准的节点报给 VM，菜单项据此决定能不能点。
    /// <para>🔴 右键**只瞄准**：⛔ 不改 <c>SelectedNode</c>、⛔ 不导航（那会让右键顺手切了目录）。</para>
    /// <para>🔴 在 <c>PointerPressed</c> 上记而不是 <c>ContextRequested</c>：后者常被菜单自己先吃掉。</para>
    /// </summary>
    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        _vm.ContextNode = (e.Source as Control)?.DataContext as AssetDirNode;
    }

    /// <summary>视图切换 / 列表重建后：新显示的列表要把选中回填成 VM 的选中集。</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssetBrowseViewModel.IsTileView) or nameof(AssetBrowseViewModel.IconSize))
        {
            DetachScroller();
            SyncSelection();
        }
    }

    // ==================== 鼠标：选中 + 双击打开 ====================

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not ListBox list) return;

        var props = e.GetCurrentPoint(this).Properties;

        // 右键必须在按压阶段定行（ContextRequested 常被菜单自己吃掉）。
        // 命中项已在选中集里 ⇒ 保留整批（批量菜单照样作用于整批）；否则替换为该项。
        if (props.IsRightButtonPressed)
        {
            if ((e.Source as Control)?.DataContext is not AssetTile hit) return;
            if (list.SelectedItems?.Contains(hit) != true)
            {
                SelectOnly(list, hit);
                _anchor = hit;
            }
            _vm.Selected = hit;
            PushSelectionToVm(list, hit);
            return;   // ⛔ 不 Handled：否则右键菜单弹不出来
        }

        if (!props.IsLeftButtonPressed) return;
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
        // 隐藏视图的数据源是 null（见 VM 的 TileSource / DetailSource）：源被置空 / 重建时的
        // 选中清空**不是**用户操作，⛔ 不能拿它去清 VM 的选中集。
        if (!ReferenceEquals(list, ActiveList) || list.ItemsSource == null) return;
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
            if (list?.ItemsSource == null) continue;   // 隐藏视图没有数据源 ⇒ 不碰它的 SelectedItems
            BulkUpdate(list, items =>
            {
                items.Clear();
                // 🔴 按**绝对路径**认领：未入库项没有 Id（合成行 Id=0），用 Id 会把它们全匹配上
                foreach (var tile in _vm.SelectedTiles)
                {
                    var hit = _vm.Items.FirstOrDefault(t =>
                        string.Equals(t.AbsPath, tile.AbsPath, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) items.Add(hit);
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
