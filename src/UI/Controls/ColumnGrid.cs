using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using NewToys.Shared.Models;

namespace NewToys.UI.Controls;

/// <summary>列表页的「列显隐」Grid：把可绑定的列宽规格串解析成 Grid.ColumnDefinitions。（WHY: UI-002）</summary>
public class ColumnGrid : Grid
{
    /// <summary>列宽规格串（可绑定）。例：<c>"3*,4*,0,1.6*"</c> 表示第 3 列已隐藏。</summary>
    public static readonly StyledProperty<string?> ColumnSpecsProperty =
        AvaloniaProperty.Register<ColumnGrid, string?>(nameof(ColumnSpecs));

    /// <summary>
    /// 列宽规格串：可见列写星号宽度（<c>3*</c>）或手调像素（<c>180</c>），隐藏列写 <c>0</c>。
    /// 由 VM 的 <c>VisibleColumnSpecs</c> 提供（见各列表页 ViewModel）。
    /// </summary>
    public string? ColumnSpecs
    {
        get => GetValue(ColumnSpecsProperty);
        set => SetValue(ColumnSpecsProperty, value);
    }

    /// <summary>
    /// 是否开启「拖动表头列边界调列宽」（v3.20，需求 §6.4.15）。默认 <c>false</c>（行模板不开）。
    /// <para>开启后自动在**相邻两个可见列**的交界处放 5px 透明把手；拖动经
    /// <see cref="ColumnResize"/> 事件把新宽度（像素）抛给 VM 改规格串，表头与所有行同规格重建。</para>
    /// </summary>
    public static readonly StyledProperty<bool> IsResizableProperty =
        AvaloniaProperty.Register<ColumnGrid, bool>(nameof(IsResizable));

    /// <summary>见 <see cref="IsResizableProperty"/>。只给**表头**那一个 ColumnGrid 开。</summary>
    public bool IsResizable
    {
        get => GetValue(IsResizableProperty);
        set => SetValue(IsResizableProperty, value);
    }

    /// <summary>
    /// 锁定列的掩码串（v3.21，需求 §4.8.1）：每位 '1' = 该列宽已锁成「表头文字宽」→ **边界不放拖拽把手**。
    /// 由 VM 的 <c>LockedColumnMask</c> 给（`ListColumn.Sample`（示例值）判定为"内容不超表头"的列）。
    /// </summary>
    public static readonly StyledProperty<string?> LockedColumnsProperty =
        AvaloniaProperty.Register<ColumnGrid, string?>(nameof(LockedColumns));

    /// <summary>见 <see cref="LockedColumnsProperty"/>。</summary>
    public string? LockedColumns
    {
        get => GetValue(LockedColumnsProperty);
        set => SetValue(LockedColumnsProperty, value);
    }

    /// <summary>
    /// 各列的**示例值文本**（';' 分隔，来自 VM 的 <c>SampleSpecs</c>）——
    /// 用它测出"一行内容大概多宽"，与表头宽比较后决定该列要不要锁成表头宽。
    /// ⛔ 分隔符用 ';'（示例值里可能有逗号，如标签 `收藏,待剪`）。
    /// </summary>
    public static readonly StyledProperty<string?> SampleSpecsProperty =
        AvaloniaProperty.Register<ColumnGrid, string?>(nameof(SampleSpecs));

    /// <summary>见 <see cref="SampleSpecsProperty"/>。</summary>
    public string? SampleSpecs
    {
        get => GetValue(SampleSpecsProperty);
        set => SetValue(SampleSpecsProperty, value);
    }

    /// <summary>拖动列宽把手后抛出（左列索引 + 新宽、右列索引 + 新宽，均为像素）。</summary>
    public event EventHandler<ColumnResizeEventArgs>? ColumnResize;

    /// <summary>首次布局后抛出一次：每列表头内容的期望宽（像素，已含单元格左右 padding）。</summary>
    public event EventHandler<HeaderMeasuredEventArgs>? HeaderMeasured;

    private bool _measured;

    /// <summary>表头各列内容的期望宽（<see cref="HeaderMeasured"/> 那次测量的结果，双击自适应用）。</summary>
    private double[]? _headerWidths;

    /// <summary>各列示例值文本宽（同上；<c>double.MaxValue</c> = 该列没给示例值）。</summary>
    private double[]? _sampleWidths;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColumnSpecsProperty)
        {
            Rebuild();
            RebuildGrips();
        }
        // ⛔ 别写成 `is A or B` —— Avalonia 分析器要求这里必须是**常量比较**（CS9135），用 == 逐个比。
        else if (change.Property == IsResizableProperty || change.Property == LockedColumnsProperty)
        {
            _gripMask = "";   // 强制重建（开关变了 / 锁定集合变了）
            RebuildGrips();
        }
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        RebuildGrips();
    }

    /// <summary>
    /// 🛡️ 布局前的最后一道防线：列数不够就先补齐。
    /// Grid 自己在 <c>MeasureOverride</c> 开头读 <c>ColumnDefinitions</c> 建 cell 表，
    /// 所以在这里补是安全的（补完立刻是幂等的，不会反复触发重排）。
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        EnsureEnoughColumns();
        var size = base.MeasureOverride(constraint);
        MeasureHeaderOnce();   // v3.21：测出「表头宽」抛给 VM（只做一次）
        return size;
    }

    /// <summary>
    /// 只测一次：把每列表头内容的**期望宽**（<c>DesiredSize.Width</c>，已含 col-header / list-cell 的左右 padding）
    /// 抛给 VM —— 供 <c>ListColumn.Sample（示例值判定列宽）</c> 的列把宽度锁成表头宽。
    /// <para>⛔ 只在 <see cref="IsResizable"/>（表头那一个）上做；行模板的 ColumnGrid 不测。</para>
    /// </summary>
    private void MeasureHeaderOnce()
    {
        if (_measured || !IsResizable) return;
        if (ColumnDefinitions.Count == 0) return;

        var widths = new double[ColumnDefinitions.Count];
        var any = false;
        foreach (var child in Children)
        {
            if (child is not Control c || !c.IsVisible) continue;   // 隐藏列不测
            var i = GetColumn(c);
            if (i < 0 || i >= widths.Length) continue;
            var w = c.DesiredSize.Width;
            if (w <= 0) continue;
            if (w > widths[i]) widths[i] = w;
            any = true;
        }
        if (!any) return;      // 首帧还没测出来 → 等下一次布局

        _measured = true;
        var samples = MeasureSampleWidths(widths.Length);
        _headerWidths = widths;
        _sampleWidths = samples;
        HeaderMeasured?.Invoke(this, new HeaderMeasuredEventArgs(widths, samples));
        (DataContext as ListPageViewModelBase)?.LockColumnsBySample(widths, samples);
    }

    /// <summary>
    /// 测示例值文本的宽（行内容字号 12 + 单元格左右内边距 8+8），供 VM 与表头宽比较。
    /// <para>⛔ 测不出来（0）或该列没给示例值 → 记 <c>double.MaxValue</c>（视为"比表头宽" → 不锁），
    /// 免得因为测不到就把所有列都锁死。</para>
    /// </summary>
    private double[]? MeasureSampleWidths(int count)
    {
        var specs = SampleSpecs;
        if (string.IsNullOrEmpty(specs)) return null;

        var parts = specs!.Split(';');
        var widths = new double[count];
        for (var i = 0; i < count; i++)
        {
            var text = i < parts.Length ? parts[i].Trim() : "";
            if (text.Length == 0) { widths[i] = double.MaxValue; continue; }

            var tb = new TextBlock { Text = text, FontSize = 12 };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var w = tb.DesiredSize.Width;
            widths[i] = w <= 0 ? double.MaxValue : w + 16;   // +16 = 单元格左右各 8
        }
        return widths;
    }

    private void Rebuild() => ColumnDefinitions = ParseSpecs(ColumnSpecs, RequiredColumnCount());

    /// <summary>子元素里用到的最大的 <c>Grid.Column</c> + 1 = 至少需要的列数（没有子元素时为 1）。</summary>
    private int RequiredColumnCount()
    {
        var max = 0;
        var children = Children;
        for (var i = 0; i < children.Count; i++)
        {
            var c = GetColumn(children[i]);
            if (c > max) max = c;
        }
        return max + 1;
    }

    private void EnsureEnoughColumns()
    {
        var need = RequiredColumnCount();
        while (ColumnDefinitions.Count < need)
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    }

    private static ColumnDefinitions ParseSpecs(string? specs, int minCount)
    {
        var defs = new ColumnDefinitions();

        if (!string.IsNullOrWhiteSpace(specs))
        {
            // ⚠️ 用 Split(',')，**不要** RemoveEmptyEntries：
            //    丢空片段会让后面所有列整体左移（列索引错位），并且列总数变少 → 触发上面那种越界。
            //    空片段一律补成 Auto。
            foreach (var part in specs.Split(','))
            {
                var s = part.Trim();

                // GridLength.Parse 认得 "3*" / "120" / "0" / "Auto"；规格串由本工程 VM 生成，不会非法。
                defs.Add(new ColumnDefinition
                {
                    Width = s.Length == 0 ? GridLength.Auto : GridLength.Parse(s)
                });
            }
        }

        // 规格串还没到（绑定未解析）/ 偏短时兜底补齐：宁可列宽不对，也绝不能越界崩。
        if (minCount < 1) minCount = 1;
        while (defs.Count < minCount)
            defs.Add(new ColumnDefinition { Width = GridLength.Auto });

        return defs;
    }

    // ==================== 表头列宽拖拽把手（v3.20，需求 §6.4.15） ====================

    /// <summary>拖动时每列的兜底最小宽（px）：表头宽度测不出来时用它。</summary>
    private const double MinColWidth = 24;

    /// <summary>
    /// 某一列允许的最小宽 = max(兜底 24, 表头文字宽) —— 拖到最窄也不会把表头文字裁掉。
    /// <para>表头宽来自 <see cref="HeaderMeasured"/> 那次测量；还没测到 / 隐藏列 → 用兜底值。</para>
    /// </summary>
    private double MinWidthFor(int i)
    {
        var hw = _headerWidths != null && i >= 0 && i < _headerWidths.Length ? _headerWidths[i] : 0;
        return Math.Max(MinColWidth, Math.Ceiling(hw));
    }

    /// <summary>把手宽（px）：贴在列右缘，半在左列半在右列上方。</summary>
    private const double GripWidth = 5;

    /// <summary>当前把手布局对应的「可见性掩码」（如 <c>"01110"</c>）；没变就不重建把手。</summary>
    private string _gripMask = "";

    /// <summary>已生成的把手（拖拽中⛔ 不许重建：会弄丢指针捕获）。</summary>
    private readonly List<Border> _grips = new();

    // 拖动状态
    private int _dragIndex = -1;
    private double _dragStartX;
    private double _dragLeft0;
    private double _dragRight0;

    // 双击检测（同把手 500ms 内两次按下 = 自适应内容宽）
    private DateTime _lastPressAt;
    private int _lastPressCol = -1;

    /// <summary>按当前「可见性掩码」重建把手；掩码没变（只是宽度变了）→ 什么都不做。</summary>
    private void RebuildGrips()
    {
        if (!IsResizable) { RemoveGrips(); return; }

        var mask = CurrentMask();
        if (mask == _gripMask) return;   // 宽度变化时把手靠 Grid 布局自动跟随，⛔ 不重建
        _gripMask = mask;

        RemoveGrips();
        for (var i = 0; i + 1 < ColumnDefinitions.Count; i++)
        {
            if (!ColVisible(i) || !ColVisible(i + 1)) continue;   // 隐藏列（宽 0）的边界不放把手

            var grip = new Border
            {
                Width = GripWidth,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.SizeWestEast)
            };
            SetColumn(grip, i);
            grip.PointerPressed += OnGripPressed;
            grip.PointerMoved += OnGripMoved;
            grip.PointerReleased += OnGripReleased;
            grip.PointerCaptureLost += OnGripCaptureLost;
            Children.Add(grip);   // 后加 = 画在上层，把手才吃得到鼠标
            _grips.Add(grip);
        }
    }

    private void RemoveGrips()
    {
        foreach (var g in _grips)
        {
            g.PointerPressed -= OnGripPressed;
            g.PointerMoved -= OnGripMoved;
            g.PointerReleased -= OnGripReleased;
            g.PointerCaptureLost -= OnGripCaptureLost;
            Children.Remove(g);
        }
        _grips.Clear();
    }

    /// <summary>当前「可见性掩码」：可见性一变就要重建把手（列宽变化时把手靠 Grid 布局自动跟随）。</summary>
    private string CurrentMask()
    {
        var sb = new System.Text.StringBuilder(ColumnDefinitions.Count + 8);
        for (var i = 0; i < ColumnDefinitions.Count; i++)
            sb.Append(ColVisible(i) ? '1' : '0');
        return sb.ToString();
    }

    private bool ColVisible(int i)
    {
        if (i < 0 || i >= ColumnDefinitions.Count) return false;
        var w = ColumnDefinitions[i].Width;
        // 像素 0 = 隐藏列；星号 / Auto 都算可见
        return w.GridUnitType == GridUnitType.Pixel ? w.Value != 0 : true;
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border g) return;
        var i = GetColumn(g);
        if (i < 0 || i + 1 >= ColumnDefinitions.Count) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed != true) return;   // 只要左键

        // 双击同一把手 = 按内容自适应（表头宽 / 示例值宽取大者）
        var now = DateTime.UtcNow;
        if (i == _lastPressCol && (now - _lastPressAt).TotalMilliseconds <= 500)
        {
            _lastPressCol = -1;
            AutoFitColumn(i);
            e.Handled = true;
            return;
        }
        _lastPressCol = i;
        _lastPressAt = now;

        _dragIndex = i;
        _dragStartX = e.GetPosition(this).X;
        _dragLeft0 = ColumnDefinitions[i].ActualWidth;
        _dragRight0 = ColumnDefinitions[i + 1].ActualWidth;
        e.Pointer.Capture(g);
        e.Handled = true;
    }

    // ⚠️ Avalonia 12 的 PointerMoved 事件用的是**普通 PointerEventArgs**（没有 PointerMovedEventArgs 这个类型；
    //    只有 PointerPressed / PointerReleased / PointerCaptureLost 有专用参数类）。
    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragIndex < 0) return;

        var total = _dragLeft0 + _dragRight0;
        var minLeft = MinWidthFor(_dragIndex);
        var minRight = MinWidthFor(_dragIndex + 1);
        if (total < minLeft + minRight) return;   // 两列加起来都塞不下各自最小宽 → 没得让，不拖

        // 左列目标宽：夹在 [左最小, total-右最小] → 右列等宽变窄，总宽守恒
        var target = Math.Clamp(_dragLeft0 + (e.GetPosition(this).X - _dragStartX), minLeft, total - minRight);
        var applied = target - _dragLeft0;
        if (Math.Abs(applied) < 0.5) return;   // 亚像素抖动不发事件（免得每动一像素就全表重建）

        ApplyResize(_dragIndex, target, _dragIndex + 1, _dragRight0 - applied);
        e.Handled = true;
    }

    /// <summary>
    /// 双击把手：该列宽 = max(表头宽, 示例值宽) + 一点余量，右侧列让出/吃进差额（总宽守恒）。
    /// <para>⛔ 不用行内容的真实宽：虚拟化下只有可见行存在，且行在另一个控件树里，测不到「最长的那条」。</para>
    /// </summary>
    private void AutoFitColumn(int i)
    {
        if (i < 0 || i + 1 >= ColumnDefinitions.Count) return;

        var total = ColumnDefinitions[i].ActualWidth + ColumnDefinitions[i + 1].ActualWidth;
        var minLeft = MinWidthFor(i);
        var minRight = MinWidthFor(i + 1);
        if (total < minLeft + minRight) return;

        var hw = _headerWidths != null && i < _headerWidths.Length ? _headerWidths[i] : 0;
        var sw = _sampleWidths != null && i < _sampleWidths.Length ? _sampleWidths[i] : 0;
        if (sw > 5000) sw = 0;                     // double.MaxValue = 该列没给示例值

        var want = Math.Max(hw, sw);
        if (want <= 0) want = ColumnDefinitions[i].ActualWidth;
        else want += 4;

        var target = Math.Clamp(want, minLeft, total - minRight);
        if (Math.Abs(target - ColumnDefinitions[i].ActualWidth) < 0.5) return;
        ApplyResize(i, target, i + 1, total - target);
    }

    /// <summary>把新宽度交出去：先抛事件（老调用方），再直接转给 VM —— 后者让页面无需再写 code-behind 接线。</summary>
    private void ApplyResize(int leftIndex, double leftWidth, int rightIndex, double rightWidth)
    {
        ColumnResize?.Invoke(this, new ColumnResizeEventArgs(leftIndex, leftWidth, rightIndex, rightWidth));
        (DataContext as ListPageViewModelBase)?.ResizeColumns(leftIndex, leftWidth, rightIndex, rightWidth);
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragIndex = -1;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _dragIndex = -1;
}

/// <summary>表头首次布局后的实测宽度（每列一个；够不到 / 隐藏列 = 0）+ 示例值文本宽（可为 null）。</summary>
public sealed class HeaderMeasuredEventArgs : EventArgs
{
    /// <summary>每列表头内容的实测宽（已含单元格内边距）。</summary>
    public IReadOnlyList<double> Widths { get; }

    /// <summary>每列**示例值**的文本宽（没给示例 / 测不到 = <see cref="double.MaxValue"/>）。</summary>
    public IReadOnlyList<double>? SampleWidths { get; }

    public HeaderMeasuredEventArgs(IReadOnlyList<double> widths, IReadOnlyList<double>? sampleWidths)
    {
        Widths = widths;
        SampleWidths = sampleWidths;
    }
}

/// <summary>拖动表头列边界的参数：左列索引 + 新宽、右列索引 + 新宽（像素）。</summary>
public sealed class ColumnResizeEventArgs : EventArgs
{
    public int LeftIndex { get; }
    public double LeftWidth { get; }
    public int RightIndex { get; }
    public double RightWidth { get; }

    public ColumnResizeEventArgs(int leftIndex, double leftWidth, int rightIndex, double rightWidth)
    {
        LeftIndex = leftIndex;
        LeftWidth = leftWidth;
        RightIndex = rightIndex;
        RightWidth = rightWidth;
    }
}
