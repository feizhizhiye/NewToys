using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using NewToys.Shared.Models;

namespace NewToys.UI.Controls;

/// <summary>
/// 列表页的表格本体：原生 <see cref="TableView"/> + 本项目约定。
/// <para>
/// 用一个控件替代「ListBox + ColumnGrid 自绘表头 + 各页 code-behind 接线」，页面只需声明列与单元格模板：
/// <list type="bullet">
///   <item>列显隐 / 表头文案 / 排序键全部从 <see cref="ListPageViewModelBase.ColumnLayout"/> 推出来，
///         XAML ⛔ 不用再写 <c>IsXxxVisible</c> 绑定与 <c>SortCommand</c> + <c>CommandParameter</c>；</item>
///   <item>列宽拖拽、表头点击排序、表头右键列菜单、<see cref="Control.ContextMenu"/> 的 DataContext 同步都在本类里；</item>
///   <item>密度与配色（行 28 / 字 12 / 语义色选中）在 <c>Styles/TableViewHost.axaml</c>，按类型选择器只作用本控件。</item>
/// </list>
/// </para>
/// <para>
/// 🔴 列顺序铁律：XAML 里 <c>TableViewHost.Columns</c> 的声明顺序必须与 VM 的
/// <see cref="ListPageViewModelBase.ColumnLayout"/> 完全一致 —— 显隐 / 表头 / 排序键都按下标对齐。
/// </para>
/// <para>
/// 🔴 选择语义按页各自声明（<c>SelectionMode</c> + <c>SelectedItem</c> 绑定）；
/// 需要 VM 侧维护多选集合的页（全部资产）仍用各自的 code-behind，本类不接管选中。
/// </para>
/// </summary>
public class TableViewHost : TableView
{
    private ListPageViewModelBase? _vm;

    private Point _lastRightPress;
    private bool _rightPressPending;
    private bool _headerMode;
    private TableViewColumn? _menuColumn;

    /// <summary>页面行菜单的原始项：表头模式清空 <c>Items</c> 后要原样还回去。</summary>
    private readonly List<object> _rowMenuItems = new();
    private bool _rowItemsCaptured;

    /// <summary>列的初始宽度（XAML 声明值），供「恢复本页默认列宽」写回。</summary>
    private readonly Dictionary<TableViewColumn, GridLength> _defaultWidths = new();

    /// <summary>单列自适应的宽度上限，避免长路径示例值把其他列挤到 0。</summary>
    private const double MaxAutoFitWidth = 320;

    /// <summary>
    /// 🔴 必须按基类取主题：Avalonia 用自己的类型找 ControlTheme、不沿继承链回溯，
    /// 主题里只登记了 <see cref="TableView"/> ⇒ 不重写这个键，本控件**没有模板**，
    /// 表现是整块表格（表头 + 行）不渲染，而 VM / 底栏状态一切正常（静默空白，无异常）。
    /// </summary>
    protected override Type StyleKeyOverride => typeof(TableView);

    public TableViewHost()
    {
        if (Columns is INotifyCollectionChanged incc)
            incc.CollectionChanged += (_, _) => SyncColumns();

        // 排序：Bubble 阶段（此时 Tunnel 阶段的列宽拖拽已决定要不要吃掉这次按下）
        AddHandler(PointerPressedEvent, OnHeaderPressed, RoutingStrategies.Bubble);

        // 列宽拖拽：Tunnel 阶段先于内置 Thumb，命中列边界就吃掉事件
        AddHandler(PointerPressedEvent, OnResizePressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnResizeMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnResizeReleased, RoutingStrategies.Tunnel);

        // 右键按下只记落点；菜单内容在 ContextMenu.Opening 时按落点重建（框架打开时序不影响结果）。
        AddHandler(PointerPressedEvent, OnRightPressRecorded, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, OnContextRequestedPreview, RoutingStrategies.Tunnel);

        LayoutUpdated += (_, _) =>
        {
            EnsureSynced();
            EnableResizerHitAreas();
        };
    }

    // ==================== 与 VM 对齐（显隐 / 表头） ====================

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as ListPageViewModelBase;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;

        SyncColumns();
        SyncContextMenu(DataContext);
    }

    /// <summary>派生类排序后会刷自己的 <c>XxxHeader</c>（带 ▲▼）⇒ 跟着重算表头文案。</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && name.EndsWith("Header", StringComparison.Ordinal)) ApplyHeaders();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != ContextMenuProperty) return;

        if (change.OldValue is ContextMenu old) old.Opening -= OnMenuOpening;
        if (change.NewValue is ContextMenu menu)
        {
            menu.DataContext = DataContext;
            menu.Opening += OnMenuOpening;
            _rowItemsCaptured = false;
        }
    }

    /// <summary>⚠️ ContextMenu 在独立弹出树上，DataContext 不随宿主继承（规范 §3.8）。</summary>
    private void SyncContextMenu(object? dataContext)
    {
        if (ContextMenu is { } menu) menu.DataContext = dataContext;
    }

    /// <summary>
    /// 把 VM 的列元数据套到 <see cref="TableView.Columns"/> 上：按下标绑定
    /// <c>Is{Key}Visible</c>（显隐）+ 重算表头文案（<see cref="ApplyHeaders"/>）。
    /// <para>⛔ 不能用祖先 / <c>$parent</c> 绑定：<see cref="TableViewColumn"/> 不在可视树里、没有 DataContext，
    /// 解析不到是静默失败（表现 = 表头空白 / 隐藏列不生效），只能显式给 Source。</para>
    /// <para>表头文案**不绑**各页的 <c>XxxHeader</c> 属性：不是每个页 VM 都有（主从页的表头原本手写在 XAML 里），
    /// 绑定缺失属性会得到空表头。文案统一由列元数据 + 排序箭头拼，排序态由基类只读属性给出。</para>
    /// </summary>
    private void SyncColumns()
    {
        var cols = Columns;
        if (cols is null || _vm is null) return;

        _syncedColumnCount = cols.Count;

        var layout = _vm.ColumnLayout;
        for (var i = 0; i < cols.Count && i < layout.Count; i++)
        {
            cols[i].Bind(TableViewColumn.IsVisibleProperty,
                new Binding("Is" + layout[i].Key + "Visible") { Source = _vm, Mode = BindingMode.OneWay });
            if (!_defaultWidths.ContainsKey(cols[i])) _defaultWidths[cols[i]] = cols[i].Width;
        }

        ApplyHeaders();
    }

    /// <summary>上一次同步时的列数（0 / 不匹配 ⇒ 说明 <c>Columns</c> 是在 DataContext 之后才填的，需重跑）。</summary>
    private int _syncedColumnCount = -1;

    /// <summary>
    /// 🔴 兜底同步：XAML 里 <c>DataContext</c> 通常先于 <c>Columns</c> 就位，
    /// 那一刻列集合还是空的 —— 没有这次补跑，表头与列显隐会**静默不生效**。
    /// </summary>
    private void EnsureSynced()
    {
        var cols = Columns;
        if (cols is null || _vm is null) return;
        if (_syncedColumnCount == cols.Count) return;
        SyncColumns();
    }

    /// <summary>表头文案 = <see cref="ListColumn.Header"/> + 排序箭头（当前排序列才有）。</summary>
    private void ApplyHeaders()
    {
        var cols = Columns;
        if (cols is null || _vm is null) return;

        var layout = _vm.ColumnLayout;
        for (var i = 0; i < cols.Count && i < layout.Count; i++)
        {
            var column = layout[i];
            if (cols[i].Header is Control) continue;   // 页面自备表头控件（如勾选列的三态 CheckBox）
            var arrow = string.Equals(column.Key, _vm.SortColumnKey, StringComparison.OrdinalIgnoreCase)
                ? (_vm.IsSortDescending ? " ▼" : " ▲")
                : "";
            var text = column.Header + arrow;
            if (!Equals(cols[i].Header, text)) cols[i].Header = text;
        }
    }

    // ==================== 表头：点击排序 ====================

    /// <summary>
    /// 点表头 → 交给 VM 的 <c>SortCommand</c>（同列再点反向，VM 负责重排 + 刷箭头）。
    /// <para>🔴 用**坐标命中**而不是 <c>e.Source</c> 找祖先：表头模板背景是透明 / null，
    /// 文字以外的空白区不参与命中测试，事件根本不会带上 header —— 那样只有点到字才有反应。</para>
    /// <para>右边缘留给列宽拖拽，所以落点太靠右时不排序。</para>
    /// </summary>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var point = e.GetPosition(this);

        foreach (var header in this.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            if (header.Column is not { } column) continue;
            if (header.TranslatePoint(new Point(0, 0), this) is not { } origin) continue;

            var bounds = new Rect(origin, header.Bounds.Size);
            if (!bounds.Contains(point)) continue;
            if (column.CanUserEffectivelyResize && point.X > bounds.Right - BorderHitWidth) return;

            var index = IndexOf(column);
            if (index < 0) return;

            var layout = _vm.ColumnLayout;
            if (index >= layout.Count) return;
            var key = layout[index].Key;
            if (!layout[index].Sortable) return;
            if (!_vm.SortCommand.CanExecute(key)) return;

            _vm.SortCommand.Execute(key);
            e.Handled = true;
            return;
        }
    }

    private int IndexOf(TableViewColumn column)
    {
        var cols = Columns;
        if (cols is null) return -1;
        for (var i = 0; i < cols.Count; i++)
            if (ReferenceEquals(cols[i], column)) return i;
        return -1;
    }

    // ==================== 列宽拖拽（按「列边界」命中 + 相邻列互补） ====================

    /// <summary>列边界的命中半径：表头两侧各有 8px 内边距，光标多数落在文字旁而不是那条 1px 线上。</summary>
    private const double BorderHitWidth = 10;

    /// <summary>表头行的垂直容差（上下各放宽几像素，免得压在边框线正中时判不中）。</summary>
    private const double BorderHitHeight = 4;

    /// <summary>列 → 拖拽下限宽（表头文字宽，按列缓存）。</summary>
    private readonly Dictionary<TableViewColumn, double> _minWidthCache = new();

    private TableViewColumn? _resizeLeft;
    private TableViewColumn? _resizeRight;
    private bool _leftResizable, _rightResizable;
    private double _resizeLeftStart, _resizeRightStart;
    private double _resizeLeftMin, _resizeRightMin;
    private double _resizeStartX;
    private IPointer? _resizePointer;
    private bool _resizeFrozen;

    /// <summary>
    /// 🔴 拖拽全程**不吃 Thumb 的 delta**：内置 <c>TableViewColumnHeader.OnResizerDragDelta</c> 每帧算
    /// <c>ActualWidth + delta</c>，而把手会随列宽一起移动 ⇒ 指针只推一点、宽度变了把手又移动、delta 再叠一次
    /// （正反馈，实测轻推一下列宽就跳一大截）。
    /// 这里改成：按下列边界时拦掉内置拖拽（<c>e.Handled = true</c>），之后用**指针在控件内的绝对 X**
    /// 减起始 X 得位移 —— 与把手自身位置无关。
    /// <para>🔴 **按「边界」命中，不是按「可拖列的右缘」**：固定列（类型 / 大小 / 修改时间 …）也有边界，
    /// 拖它就等于调**相邻那个可变列**（自己不动）；否则两侧都是固定列的列会彻底拖不动。</para>
    /// <para>宽度守恒：两边都可拖 ⇒ 一增一减；只有一边可拖 ⇒ 只改那一边。</para>
    /// </summary>
    private bool TryBeginColumnResize(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        var hit = HitColumnBoundary(e, point);
        if (hit?.Column is not { } left) return false;

        var right = NextVisibleColumn(left);
        var leftOk = left.CanUserEffectivelyResize;
        var rightOk = right?.CanUserEffectivelyResize ?? false;
        if (!leftOk && !rightOk) return false;

        var leftStart = ActualOrSpecified(left);
        if (leftStart <= 0) return false;

        _resizeLeft = left;
        _resizeRight = right;
        _leftResizable = leftOk;
        _rightResizable = rightOk;
        _resizeLeftStart = leftStart;
        _resizeLeftMin = MinWidthOf(left);
        _resizeRightStart = right is null ? 0 : Math.Max(ActualOrSpecified(right), MinWidthOf(right));
        _resizeRightMin = right is null ? 0 : MinWidthOf(right);
        _resizeFrozen = false;   // 星号列等到真的拖动了才固化（只按一下不拖 ⛔ 不该改布局口径）
        _resizeStartX = point.X;
        _resizePointer = e.Pointer;

        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// 找光标所在的列边界：优先命中表头的 <c>PART_Resizer</c>（主题给的 12px Thumb，位置由框架保证），
    /// 兜底按表头矩形算最近边界（容差 <see cref="BorderHitWidth"/>）。
    /// </summary>
    private TableViewColumnHeader? HitColumnBoundary(PointerPressedEventArgs e, Point point)
    {
        if (e.Source is Visual src && (src is Thumb || src.FindAncestorOfType<Thumb>() is not null))
        {
            var viaThumb = src.FindAncestorOfType<TableViewColumnHeader>(includeSelf: false);
            if (viaThumb?.Column is { IsVisible: true }) return viaThumb;
        }

        TableViewColumnHeader? best = null;
        var bestDistance = double.MaxValue;

        foreach (var header in this.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            if (header.Column is not { IsVisible: true }) continue;
            if (header.TranslatePoint(new Point(header.Bounds.Width, 0), this) is not { } edge) continue;

            if (point.Y < edge.Y - BorderHitHeight ||
                point.Y > edge.Y + header.Bounds.Height + BorderHitHeight) continue;

            var distance = Math.Abs(point.X - edge.X);
            if (distance > BorderHitWidth || distance >= bestDistance) continue;

            bestDistance = distance;
            best = header;
        }
        return best;
    }

    /// <summary>
    /// 主题把 <c>PART_Resizer</c> 的 IsEnabled 绑到列的 <c>CanUserEffectivelyResize</c> ⇒ 固定列的边界
    /// **连命中区都没有**。这里用本地值覆盖绑定统一打开：内置拖拽自身会检查列能不能拖，
    /// 打开 Thumb 只是多给一块可靠的命中区 + 调整光标。
    /// </summary>
    private void EnableResizerHitAreas()
    {
        var cols = Columns;
        if (cols is null) return;
        if (_enabledThumbs.Count >= cols.Count) return;   // 全处理过 ⇒ 不必每次布局都遍历

        foreach (var header in this.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            var resizer = header.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
            if (resizer is null) continue;
            if (!resizer.IsEnabled) resizer.IsEnabled = true;
            _enabledThumbs.Add(resizer);
        }
    }

    /// <summary>已放开命中区的把手（表头重建才会换新实例）。</summary>
    private readonly HashSet<Thumb> _enabledThumbs = new();

    /// <summary>列的实际宽（未测量时为 NaN）→ 退化到规格里的像素值；都拿不到返回 0。</summary>
    private static double ActualOrSpecified(TableViewColumn column)
    {
        var actual = column.ActualWidth;
        if (!double.IsNaN(actual) && actual > 0) return actual;
        return column.Width.IsAbsolute ? column.Width.Value : 0;
    }

    /// <summary>把除被拖两列之外的可见星号列按当前实际宽固化（否则一列变像素，剩余星号列会重新分空间）。</summary>
    private void FreezeStarWidths(TableViewColumn left, TableViewColumn? right)
    {
        var cols = Columns;
        if (cols is null) return;

        foreach (var c in cols)
        {
            if (ReferenceEquals(c, left) || ReferenceEquals(c, right)) continue;
            if (!c.IsVisible || c.Width.IsAbsolute) continue;
            var w = c.ActualWidth;
            if (double.IsNaN(w) || w <= 0) continue;
            c.Width = new GridLength(w, GridUnitType.Pixel);
        }
    }

    /// <summary>右侧**最近的可见列**（跳过隐藏列；不管能不能拖 —— 拖边界时由它充当被压缩的那一边）。</summary>
    private TableViewColumn? NextVisibleColumn(TableViewColumn column)
    {
        var cols = Columns;
        if (cols is null) return null;

        var start = IndexOf(column);
        for (var i = start + 1; i > 0 && i < cols.Count; i++)
            if (cols[i].IsVisible) return cols[i];
        return null;
    }

    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        TryBeginColumnResize(e);   // 命中判定在 HitColumnBoundary 里按表头矩形做
    }

    private void OnResizeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeLeft is null) return;

        var delta = e.GetPosition(this).X - _resizeStartX;
        if (!_resizeFrozen && Math.Abs(delta) > 0.5)
        {
            FreezeStarWidths(_resizeLeft, _resizeRight);   // 先固化其余列，再改这一对 ⇒ 别的列不重分配
            _resizeFrozen = true;
        }

        var leftWidth = _resizeLeftStart;
        var rightWidth = _resizeRightStart;

        if (_leftResizable && _rightResizable)
        {
            leftWidth = Math.Max(_resizeLeftMin, _resizeLeftStart + delta);
            var grow = leftWidth - _resizeLeftStart;
            rightWidth = Math.Max(_resizeRightMin, _resizeRightStart - grow);
            grow = _resizeRightStart - rightWidth;      // 右邻到底 ⇒ 左列同步停（总宽守恒）
            leftWidth = _resizeLeftStart + grow;
        }
        else if (_leftResizable)
        {
            leftWidth = Math.Max(_resizeLeftMin, _resizeLeftStart + delta);
        }
        else
        {
            rightWidth = Math.Max(_resizeRightMin, _resizeRightStart - delta);
        }

        if (_leftResizable) _resizeLeft.Width = new GridLength(leftWidth, GridUnitType.Pixel);
        if (_rightResizable && _resizeRight is { } right)
            right.Width = new GridLength(rightWidth, GridUnitType.Pixel);

        e.Handled = true;
    }

    private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizeLeft is null) return;

        _resizePointer?.Capture(null);
        _resizePointer = null;
        _resizeLeft = null;
        _resizeRight = null;
        Cursor = null;
        e.Handled = true;
    }

    /// <summary>
    /// 列的下限宽 = **表头文字宽 + 左右内边距（8+8）+ 2 余量**。
    /// 排序箭头（" ▲" / " ▼"）不算进「表头文字」—— 否则点一下排序就能把最小宽顶大。
    /// </summary>
    private double MinWidthOf(TableViewColumn column)
    {
        if (_minWidthCache.TryGetValue(column, out var cached)) return cached;

        var text = (column.Header as string ?? "")
            .Replace(" ▲", "", StringComparison.Ordinal)
            .Replace(" ▼", "", StringComparison.Ordinal);

        var probe = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.Bold };
        probe.Measure(Size.Infinity);

        var width = Math.Ceiling(probe.DesiredSize.Width) + 18;
        _minWidthCache[column] = width;
        return width;
    }

    // ==================== 表头右键（列宽 / 列显隐） ====================

    /// <summary>右键按下只记落点与命中列；⛔ 不碰 ContextMenu 属性（框架何时读它不可控）。</summary>
    private void OnRightPressRecorded(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _headerMode = false;
            _menuColumn = null;
            return;
        }

        _lastRightPress = e.GetPosition(this);
        _rightPressPending = true;
        _headerMode = TryHitHeader(_lastRightPress, out _menuColumn);
    }

    /// <summary>键盘触发（Shift+F10 / 菜单键）没有 PointerPressed ⇒ 一律回到行菜单。</summary>
    private void OnContextRequestedPreview(object? sender, ContextRequestedEventArgs e)
    {
        if (_rightPressPending)
        {
            _rightPressPending = false;
            // 再算一次：按压时列头可能尚未完成布局，菜单打开前重算更准。
            _headerMode = TryHitHeader(_lastRightPress, out _menuColumn);
            return;
        }
        _headerMode = false;
        _menuColumn = null;
    }

    /// <summary>
    /// 表头行命中：取所有列头的包围盒（列间空隙 / 最右空白也算表头），表头背景透明，
    /// ⛔ 靠 e.Source 找祖先会漏掉文字以外的空白区。
    /// </summary>
    private bool TryHitHeader(Point point, out TableViewColumn? column)
    {
        column = null;
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        foreach (var header in this.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            if (header.Column is not { IsVisible: true }) continue;
            if (header.TranslatePoint(new Point(0, 0), this) is not { } origin) continue;

            var rect = new Rect(origin, header.Bounds.Size);
            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
            if (rect.Contains(point)) column = header.Column;
        }

        if (double.IsInfinity(left)) return false;
        return new Rect(left, top, right - left, bottom - top).Contains(point);
    }

    /// <summary>菜单打开的最后一刻按落点重建内容：表头 = 列宽 + 列显隐，行区 = 页面原始功能项。</summary>
    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.DataContext = DataContext;

        if (!_rowItemsCaptured)
        {
            _rowMenuItems.Clear();
            foreach (var item in menu.Items)
                if (item is not null) _rowMenuItems.Add(item);
            _rowItemsCaptured = true;
        }

        menu.Items.Clear();
        if (_headerMode) BuildHeaderItems(menu);
        else foreach (var item in _rowMenuItems) menu.Items.Add(item);
    }

    /// <summary>两段口径：列宽自适应 / 恢复默认 → 列显隐勾选（首列标识列置灰）。⛔ 不放筛选与行功能。</summary>
    private void BuildHeaderItems(ContextMenu menu)
    {
        if (_vm is null) return;

        var fit = new MenuItem { Header = "↔ 本列宽度自适应", IsEnabled = _menuColumn is not null };
        fit.Click += (_, _) => AutoFitColumn();
        menu.Items.Add(fit);

        var reset = new MenuItem { Header = "⟲ 恢复本页默认列宽" };
        reset.Click += (_, _) => ResetColumnWidths();
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());

        var layout = _vm.ColumnLayout;
        for (var i = 0; i < layout.Count; i++)
        {
            var column = layout[i];
            var item = new MenuItem
            {
                Header = column.Header,
                ToggleType = MenuItemToggleType.CheckBox,
                CommandParameter = column.Key,
                IsEnabled = i > 0
            };

            if (i == 0) ToolTip.SetTip(item, "标识列固定显示，不可隐藏");

            item.Bind(MenuItem.IsCheckedProperty,
                new Binding("Is" + column.Key + "Visible") { Source = _vm, Mode = BindingMode.OneWay });

            if (i > 0)
                item.Bind(MenuItem.CommandProperty,
                    new Binding(nameof(ListPageViewModelBase.ToggleColumnCommand)) { Source = _vm });

            menu.Items.Add(item);
        }
    }

    /// <summary>只调右键命中的一列：表头文字宽与示例值宽取大，封顶 320px（⛔ 不全文测宽、不批量调全部列）。</summary>
    private void AutoFitColumn()
    {
        if (_menuColumn is null || _vm is null) return;

        var width = MinWidthOf(_menuColumn);
        var index = IndexOf(_menuColumn);
        var samples = _vm.SampleSpecs.Split(';');
        if (index >= 0 && index < samples.Length && samples[index].Length > 0)
        {
            var probe = new TextBlock { Text = samples[index], FontSize = 12 };
            probe.Measure(Size.Infinity);
            width = Math.Max(width, probe.DesiredSize.Width + 18);
        }

        width = Math.Min(Math.Ceiling(width), MaxAutoFitWidth);
        if (width > 0) _menuColumn.Width = new GridLength(width, GridUnitType.Pixel);
    }

    private void ResetColumnWidths()
    {
        var cols = Columns;
        if (cols is null) return;
        foreach (var column in cols)
            if (_defaultWidths.TryGetValue(column, out var width)) column.Width = width;
    }
}
