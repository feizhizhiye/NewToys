using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace NewToys.PcTools.Views;

/// <summary>
/// 列表页的「列显隐」Grid：把**可绑定的列宽规格串**解析成 <see cref="Grid.ColumnDefinitions"/>。
/// <para>
/// ⚠️ **为什么需要这个控件**（规范 3.8.5）：
/// <c>Grid.ColumnDefinitions</c> 是 **CLR 属性**，Avalonia 的 XAML 编译器**不允许**在它上面写
/// <c>{Binding}</c> —— 会直接报
/// <c>AVLN3000: Unable to find suitable setter or adder for property ColumnDefinitions ... for argument ReflectionBinding</c>。
/// 所以「列显隐」不能绑 <c>ColumnDefinitions</c>，只能改成「绑一个字符串规格 → 控件自己解析」。
/// </para>
/// <para>
/// <b>规格串规则</b>：逗号分隔，<b>可见列写它的宽度</b>（星号如 <c>3*</c> / 手调像素如 <c>180</c>）、<b>隐藏列写 <c>0</c></b>。
/// 用 <c>0</c> 而不是「真删列」，是为了让<b>列索引保持不动</b> ——
/// 表头与行模板永远共用同一份列定义（规范 3.8.2 对齐铁律①）。
/// </para>
/// <para>
/// ⚠️ 每次赋值都<b>新建</b>一个 <see cref="ColumnDefinitions"/> 实例：同一个实例不能被多个 Grid 共用。
/// </para>
/// <para>
/// 🛡️ <b>列数防线</b>：本控件保证「<see cref="Grid.ColumnDefinitions"/>.Count 永远 ≥ 子元素用到的最大
/// <c>Grid.Column</c> + 1」。这不是锦上添花，而是**必须**：
/// 行模板里的规格串来自 <c>{Binding $parent[ListBox].DataContext.VisibleColumnSpecs}</c>，
/// 在 <c>VirtualizingStackPanel.RealizeElements</c> 里容器可能先被 measure、绑定后到；
/// 那一刻若列数是 0，Avalonia 的 Grid 会走 <c>MeasureCell</c> → <c>_cells[cell]</c> 越界，
/// 抛 <c>ArgumentOutOfRangeException</c> 把整次布局打断（页面画一半 / 卡住 / 进程退出）。
/// 真实崩溃栈：
/// <c>Grid.MeasureCell → Grid.MeasureCellsGroup → Grid.MeasureOverride → VirtualizingStackPanel.RealizeElements</c>。
/// </para>
/// <para>
/// 📌 本控件在 <c>Module.PcTools</c> / <c>Module.WebTools</c> / <c>Module.Settings</c> 各有一份
/// 同名同实现的副本（模块之间禁止互相引用，见规范 4.1.1）。四份必须保持一致；改动时同步改。
/// </para>
/// </summary>
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

    /// <summary>拖动列宽把手后抛出（左列索引 + 新宽、右列索引 + 新宽，均为像素）。</summary>
    public event EventHandler<ColumnResizeEventArgs>? ColumnResize;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColumnSpecsProperty)
        {
            Rebuild();
            RebuildGrips();
        }
        else if (change.Property == IsResizableProperty)
        {
            _gripMask = "";   // 强制重建（从无到开 / 从开到无）
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
        return base.MeasureOverride(constraint);
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

    /// <summary>拖动时每列至少保留的宽度（px）。</summary>
    private const double MinColWidth = 24;

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

    /// <summary>当前每列「可见（宽 ≠ 0）」的掩码串。</summary>
    private string CurrentMask()
    {
        var sb = new System.Text.StringBuilder(ColumnDefinitions.Count);
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
        if (total < MinColWidth * 2) return;   // 两列加起来都塞不下两个最小宽 → 没得让，不拖

        // 左列目标宽：夹在 [Min, total-Min] → 右列等宽变窄，总宽守恒
        var target = Math.Clamp(_dragLeft0 + (e.GetPosition(this).X - _dragStartX), MinColWidth, total - MinColWidth);
        var applied = target - _dragLeft0;
        if (Math.Abs(applied) < 0.5) return;   // 亚像素抖动不发事件（免得每动一像素就全表重建）

        ColumnResize?.Invoke(this, new ColumnResizeEventArgs(_dragIndex, target, _dragIndex + 1, _dragRight0 - applied));
        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragIndex = -1;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _dragIndex = -1;
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
