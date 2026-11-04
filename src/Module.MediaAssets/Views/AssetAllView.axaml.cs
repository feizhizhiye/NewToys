using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

        // 多选：修饰键在**隧道**阶段接管（ListBox 自己在 Multiple 下单击是「追加」，
        // 与资源管理器「单击 = 只选这一行」不一致 ⇒ 无修饰键也一并接管）
        AssetList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        AssetList.AddHandler(PointerPressedEvent, OnHeaderPointerPressed, RoutingStrategies.Bubble);
        AssetList.AddHandler(PointerMovedEvent, OnResizePointerMoved, RoutingStrategies.Tunnel);
        AssetList.AddHandler(PointerReleasedEvent, OnResizePointerReleased, RoutingStrategies.Tunnel);
        AssetList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        AssetList.SelectionChanged += OnListSelectionChanged;

        // 底栏显隐的另一半口径 = 列表内容是否超出视口（滚动出现）。
        // 筛选下拉的弹出位置由 <c>ui:FilterDropDownAlign</c>（XAML 根节点挂载）负责，本类不再管。
        LayoutUpdated += (_, _) =>
        {
            EnableColumnResizerHitAreas();   // 表头模板重建后重新放开固定列的把手（幂等）
            ReportListOverflow();
        };
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
        if (_vm != null)
        {
            _vm.SelectionSyncRequested += OnSelectionSyncRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        if (AssetList?.ContextMenu is { } amenu) amenu.DataContext = DataContext;
        if (_vm != null)
        {
            BindColumnVisibility(_vm);
            ApplyHeaders();
        }
    }

    // ==================== 表头：点击排序（TableView 不用 Button，避免 Header 元素语法的加载问题） ====================

    /// <summary>表头列键，顺序必须与 XAML 里 <c>TableView.Columns</c> 的声明顺序一致。</summary>
    private static readonly string[] SortKeys =
    [
        "Name", "Type", "Tags", "Categories", "Collections", "Size",
        "Modified", "Storage", "Duration", "Resolution", "Codec", "Bitrate"
    ];

    /// <summary>
    /// 点表头 → 交给 VM 的 <c>SortCommand</c>（同列再点反向，VM 侧负责重排 + 刷箭头）。
    /// <para>🔴 用**坐标命中**而不是 <c>e.Source</c> 找祖先：表头模板的背景是透明/null 的，
    /// 文字以外的空白区不参与命中测试，事件根本不会带上 header —— 那样只有点到字才有反应。</para>
    /// <para>右边缘留给列宽拖拽（resizer 的命中区），所以落点太靠右时不排序。</para>
    /// </summary>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (AssetList == null) return;

        var point = e.GetPosition(AssetList);

        foreach (var header in AssetList.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            if (header.Column is not { } column) continue;
            if (header.TranslatePoint(new Point(0, 0), AssetList) is not { } origin) continue;

            var bounds = new Rect(origin, header.Bounds.Size);
            if (!bounds.Contains(point)) continue;
            // 右缘让给列宽拖拽（与 TryBeginColumnResize 用同一个命中半径，避免中间出现"点了没反应"的死区）
            if (column.CanUserEffectivelyResize && point.X > bounds.Right - BorderHitWidth) return;
            var index = -1;
            var cols = AssetList.Columns;
            for (var i = 0; i < cols.Count; i++)
                if (ReferenceEquals(cols[i], column)) { index = i; break; }

            if (index < 0 || index >= SortKeys.Length) return;
            if (!_vm.SortCommand.CanExecute(SortKeys[index])) return;

            _vm.SortCommand.Execute(SortKeys[index]);
            e.Handled = true;
            return;
        }
    }

    // ==================== 列宽拖拽（自绘：绝对坐标 + 相邻列互补） ====================

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
    /// <c>ActualWidth + delta</c>，而把手会随列宽一起移动 ⇒ 指针只推一点，宽度变了把手又移动，delta 再叠一次
    /// （正反馈，实测轻推一下列宽就跳一大截）。
    /// 这里改成：按下列边界时拦掉内置拖拽（<c>e.Handled = true</c>），之后用**指针在 AssetList 里的绝对 X**
    /// 减起始 X 得位移 —— 与把手自身位置无关。
    /// <para>🔴 **按「边界」命中，不是按「可拖列的右缘」**：固定列（类型 / 大小 / 修改时间 …）也有边界，
    /// 拖它就等于调**相邻那个可变列**（自己不动）；否则像「存储源」这种两侧都是固定列的列会彻底拖不动。</para>
    /// <para>宽度守恒：两边都可拖 ⇒ 一增一减；只有一边可拖 ⇒ 只改那一边（表格总宽随之变化）。</para>
    /// </summary>
    private bool TryBeginColumnResize(PointerPressedEventArgs e)
    {
        if (AssetList is null) return false;

        var point = e.GetPosition(AssetList);
        var hit = HitColumnBoundary(e, point);
        if (hit?.Column is not { } left) return false;

        var right = NextVisibleColumn(left);
        var leftOk = left.CanUserEffectivelyResize;
        var rightOk = right?.CanUserEffectivelyResize ?? false;
        if (!leftOk && !rightOk) return false;   // 两侧都固定 ⇒ 不可拖

        if (!BeginColumnResize(left, right, leftOk, rightOk, point.X, e.Pointer)) return false;

        e.Handled = true;   // 拦住 Thumb：不让内置拖拽启动
        return true;
    }

    /// <summary>
    /// 找光标所在的列边界：
    /// <list type="number">
    /// <item>**优先**命中表头的 <c>PART_Resizer</c>（主题给的 12px Thumb，位置由框架保证，不依赖自算几何）；</item>
    /// <item>兜底按表头矩形算最近边界（容差 <see cref="BorderHitWidth"/>）。</item>
    /// </list>
    /// </summary>
    private TableViewColumnHeader? HitColumnBoundary(PointerPressedEventArgs e, Point point)
    {
        if (AssetList is null) return null;

        if (e.Source is Visual src &&
            (src is Thumb || src.FindAncestorOfType<Thumb>() is not null))
        {
            var viaThumb = src.FindAncestorOfType<TableViewColumnHeader>(includeSelf: false);
            if (viaThumb?.Column is { IsVisible: true }) return viaThumb;
        }

        TableViewColumnHeader? best = null;
        var bestDistance = double.MaxValue;

        foreach (var header in AssetList.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            if (header.Column is not { IsVisible: true }) continue;
            if (header.TranslatePoint(new Point(header.Bounds.Width, 0), AssetList) is not { } edge) continue;

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
    /// **连命中区都没有**（所以「存储源」左侧看着不能拖）。这里用本地值覆盖绑定统一打开：
    /// 内置拖拽自身会检查列能不能拖，打开 Thumb 只是多给一块可靠的命中区 + 调整光标。
    /// </summary>
    private void EnableColumnResizerHitAreas()
    {
        if (AssetList is null) return;
        if (_enabledThumbs.Count >= AssetList.Columns.Count) return;   // 全处理过 ⇒ 不必每次布局都遍历

        foreach (var header in AssetList.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            var resizer = header.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
            if (resizer is null) continue;
            if (!resizer.IsEnabled) resizer.IsEnabled = true;
            _enabledThumbs.Add(resizer);
        }
    }

    /// <summary>已放开命中区的把手（表头重建才会换新实例）。</summary>
    private readonly HashSet<Thumb> _enabledThumbs = new();

    private bool BeginColumnResize(
        TableViewColumn left, TableViewColumn? right,
        bool leftOk, bool rightOk, double x, IPointer pointer)
    {
        // ⚠️ TableView 每次改列宽都会把 ActualWidth 重置成 NaN 等下一次测量 —— 起点取不到就换规格值，
        //    仍然取不到（星号列且未测过）就放弃本次拖拽，⛔ 不能拿 0 / NaN 当起点（列宽会瞬间塌成 0）。
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

        _resizeStartX = x;
        _resizePointer = pointer;
        pointer.Capture(AssetList);
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        return true;
    }

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
        if (AssetList is null) return;
        foreach (var c in AssetList.Columns)
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
        var cols = AssetList?.Columns;
        if (cols is null) return null;

        var start = cols.IndexOf(column);
        for (var i = start + 1; i > 0 && i < cols.Count; i++)
            if (cols[i].IsVisible) return cols[i];
        return null;
    }

    private void OnResizePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeLeft is null || AssetList is null) return;

        var delta = e.GetPosition(AssetList).X - _resizeStartX;
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
            // 右邻固定 / 不存在 ⇒ 只改左列（表格总宽跟着变）
            leftWidth = Math.Max(_resizeLeftMin, _resizeLeftStart + delta);
        }
        else
        {
            // 左列固定 ⇒ 只改右列，方向相反（边界左移 = 右列变宽）
            rightWidth = Math.Max(_resizeRightMin, _resizeRightStart - delta);
        }

        if (_leftResizable) _resizeLeft.Width = new GridLength(leftWidth, GridUnitType.Pixel);
        if (_rightResizable && _resizeRight is { } right)
            right.Width = new GridLength(rightWidth, GridUnitType.Pixel);

        e.Handled = true;
    }

    private void OnResizePointerReleased(object? sender, PointerReleasedEventArgs e)
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

    /// <summary>VM 刷新表头文案（带 ▲▼）后同步到各列 —— 列对象不在可视树里，绑不到 VM。</summary>
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not { } name) return;

        if (name.EndsWith("Header", StringComparison.Ordinal)) ApplyHeaders();
        else if (name.StartsWith("Is", StringComparison.Ordinal) &&
                 name.EndsWith("Visible", StringComparison.Ordinal)) SyncColumnVisibility();
    }

    private void ApplyHeaders()
    {
        var cols = AssetList?.Columns;
        if (_vm == null || cols == null) return;

        string[] texts =
        [
            _vm.NameHeader, _vm.TypeHeader, _vm.TagsHeader, _vm.CategoriesHeader,
            _vm.CollectionsHeader, _vm.SizeHeader, _vm.ModifiedHeader, _vm.StorageHeader,
            _vm.DurationHeader, _vm.ResolutionHeader, _vm.CodecHeader, _vm.BitrateHeader
        ];

        for (var i = 0; i < texts.Length && i < cols.Count; i++)
            if (!Equals(cols[i].Header, texts[i])) cols[i].Header = texts[i];
    }

    /// <summary>
    /// 列显隐：<see cref="TableViewColumn"/> 不在可视树里、没有 DataContext，
    /// 因此用**显式 Source 绑定**接 VM 的 <c>IsXxxVisible</c>（⛔ 别用 <c>$parent</c> 祖先绑定，解析不到是静默失败）。
    /// 顺序必须与 XAML 里 <c>TableView.Columns</c> 的声明顺序一致。
    /// <para>🔴 绑定之外再补一次 <see cref="SyncColumnVisibility"/>：类型切换（筛选栏下拉）时，
    /// 若只靠绑定推通知，列对象可能收不到 → 表头不跟着变（表现为「切到图片，码率列还在」）。</para>
    /// </summary>
    private void BindColumnVisibility(AssetAllViewModel vm)
    {
        var cols = AssetList?.Columns;
        if (cols == null) return;

        var map = new (int Index, string Prop)[]
        {
            (0, nameof(AssetAllViewModel.IsNameVisible)),
            (1, nameof(AssetAllViewModel.IsTypeVisible)),
            (2, nameof(AssetAllViewModel.IsTagsVisible)),
            (3, nameof(AssetAllViewModel.IsCategoriesVisible)),
            (4, nameof(AssetAllViewModel.IsCollectionsVisible)),
            (5, nameof(AssetAllViewModel.IsSizeVisible)),
            (6, nameof(AssetAllViewModel.IsModifiedVisible)),
            (7, nameof(AssetAllViewModel.IsStorageVisible)),
            (8, nameof(AssetAllViewModel.IsDurationVisible)),
            (9, nameof(AssetAllViewModel.IsResolutionVisible)),
            (10, nameof(AssetAllViewModel.IsCodecVisible)),
            (11, nameof(AssetAllViewModel.IsBitrateVisible))
        };

        foreach (var (index, prop) in map)
        {
            if (index >= cols.Count) continue;
            cols[index].Bind(TableViewColumn.IsVisibleProperty, new Binding(prop) { Source = vm });
        }

        SyncColumnVisibility();
    }

    /// <summary>
    /// 把 VM 的列显隐**逐列直接写**到 <see cref="TableViewColumn.IsVisible"/>（幂等，只写变化项）。
    /// <para>绑定的通知在这批列对象上不可靠（列不在可视树），类型切换 / 右键勾列后必须由这里兜底。</para>
    /// </summary>
    private void SyncColumnVisibility()
    {
        var cols = AssetList?.Columns;
        var vm = _vm;
        if (cols == null || vm == null) return;

        (int Index, bool Visible)[] states =
        [
            (0, vm.IsNameVisible), (1, vm.IsTypeVisible), (2, vm.IsTagsVisible),
            (3, vm.IsCategoriesVisible), (4, vm.IsCollectionsVisible), (5, vm.IsSizeVisible),
            (6, vm.IsModifiedVisible), (7, vm.IsStorageVisible), (8, vm.IsDurationVisible),
            (9, vm.IsResolutionVisible), (10, vm.IsCodecVisible), (11, vm.IsBitrateVisible)
        ];

        foreach (var (index, visible) in states)
            if (index < cols.Count && cols[index].IsVisible != visible) cols[index].IsVisible = visible;
    }

    // ==================== 选中（Ctrl 增选 / Shift 连选 / Ctrl+A 全选） ====================

    /// <summary>
    /// 鼠标点行：无修饰键 = 只选这一行；`Ctrl` = 切换该行；`Shift` = 从基准行连选；
    /// `Ctrl+Shift` = 追加连选。
    /// <para>
    /// 🔴 右键：命中行**不在**当前多选里 ⇒ 单选替换成这一行（资源管理器语义，否则「从磁盘删除」会删掉
    /// 上一批选中、而不是你右键的那行）；命中行已在多选里 ⇒ 保留整批，让批量菜单作用于整批。
    /// </para>
    /// </summary>
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;

        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            if ((e.Source as Control)?.DataContext is not AssetRow hit) return;
            if (_vm.SelectedRows.Any(r => r.Id == hit.Id)) return;

            _anchor = hit;
            ReplaceWith(hit);
            _vm.Selected = hit;
            PushSelectionToVm(hit);
            return;   // ⛔ 不 Handled：否则右键菜单弹不出来
        }

        if (!props.IsLeftButtonPressed) return;
        if (TryBeginColumnResize(e)) return;                            // 表头右缘：拖列宽（已 Handled）
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

    /// <summary>Ctrl+A：交给 VM 的全选命令（与右键「全选当前页」同一入口）。</summary>
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
