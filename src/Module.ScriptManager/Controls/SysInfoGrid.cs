using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Controls;

/// <summary>
/// 信息面板页的**动态列**容器（NT-067 §三.5 / 决策 D1）：把「列宽规格串 + 单元格集合」渲染成一格一行。
/// <para>
/// 🔴 为什么需要它：<c>Shared/ListPageViewModelBase</c> 的列机制是「<c>Columns</c> 元数据 + 每列一个具名
/// <c>IsXxxVisible</c> 属性 + XAML 静态列」（XAML 绑不了字典），而本页列数由 manifest 决定、运行时可变
/// ⇒ 静态列不可行。框架层冻结（不改 Shared），所以在**页内自绘动态列**：
/// <see cref="ColumnSpecs"/> 解析成 <see cref="Grid.ColumnDefinitions"/>，
/// 每个单元格 / 表头按钮按 <c>Grid.Column</c> 落位。
/// </para>
/// <para>
/// 两种用法（同一控件，互斥）：
/// <list type="bullet">
///   <item><see cref="HeaderCells"/> 非空 → 渲染一行 <c>Button.col-header</c>（点 = 排序，参数 = 列键）；</item>
///   <item><see cref="Cells"/> 非空 → 渲染一行 <c>TextBlock.list-cell</c>（第 1 列带 <c>accent</c>）。</item>
/// </list>
/// 文本 / 可见性走**绑定**（单元格对象是可观察的）⇒ 定时刷新就地改值时不重建可视树，界面不闪。
/// </para>
/// <para>
/// ⚠️ 与 <c>UI/Controls/ColumnGrid</c> 的分工：那个是「列宽可拖动 + 示例值锁宽」的表头栅格（框架内已有列机制用）；
/// 本控件只做「任意列数 + 规格串 → 网格」的渲染，不接拖拽（改宽走 manifest，见卡 §三.5 代价）。
/// </para>
/// </summary>
public class SysInfoGrid : Grid
{
    /// <summary>列宽规格串（可绑定）：<c>"140,2*,3*"</c>，隐藏列写 <c>0</c>（与 <c>ColumnGrid</c> 同一口径）。</summary>
    public static readonly StyledProperty<string?> ColumnSpecsProperty =
        AvaloniaProperty.Register<SysInfoGrid, string?>(nameof(ColumnSpecs));

    /// <summary>单元格集合（数据行的用法）。</summary>
    public static readonly StyledProperty<IReadOnlyList<SysInfoCell>?> CellsProperty =
        AvaloniaProperty.Register<SysInfoGrid, IReadOnlyList<SysInfoCell>?>(nameof(Cells));

    /// <summary>表头集合（表头的用法）。</summary>
    public static readonly StyledProperty<IReadOnlyList<SysInfoHeaderCell>?> HeaderCellsProperty =
        AvaloniaProperty.Register<SysInfoGrid, IReadOnlyList<SysInfoHeaderCell>?>(nameof(HeaderCells));

    /// <summary>表头点击时的排序命令（参数 = 列键 <c>c1</c> / <c>c2</c>…）。</summary>
    public static readonly StyledProperty<ICommand?> SortCommandProperty =
        AvaloniaProperty.Register<SysInfoGrid, ICommand?>(nameof(SortCommand));

    public string? ColumnSpecs
    {
        get => GetValue(ColumnSpecsProperty);
        set => SetValue(ColumnSpecsProperty, value);
    }

    public IReadOnlyList<SysInfoCell>? Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public IReadOnlyList<SysInfoHeaderCell>? HeaderCells
    {
        get => GetValue(HeaderCellsProperty);
        set => SetValue(HeaderCellsProperty, value);
    }

    public ICommand? SortCommand
    {
        get => GetValue(SortCommandProperty);
        set => SetValue(SortCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // ⛔ 别写成 `is A or B`：分析器要求这里是常量比较（CS9135），用 == 逐个比。
        if (change.Property == ColumnSpecsProperty
            || change.Property == CellsProperty
            || change.Property == HeaderCellsProperty
            || change.Property == SortCommandProperty)
            Rebuild();
    }

    /// <summary>按当前三个属性重建列定义 + 子元素（幂等）。</summary>
    private void Rebuild()
    {
        Children.Clear();

        if (HeaderCells is { Count: > 0 } headers)
        {
            ApplySpecs(headers.Count);
            for (var i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                var btn = new Button { DataContext = h, Command = SortCommand, CommandParameter = h.ColumnKey };
                btn.Classes.Add("col-header");
                btn.Bind(ContentControl.ContentProperty, new Binding(nameof(SysInfoHeaderCell.Text)));
                btn.Bind(IsVisibleProperty, new Binding(nameof(SysInfoHeaderCell.IsVisible)));
                SetColumn(btn, i);
                Children.Add(btn);
            }
            return;
        }

        if (Cells is { Count: > 0 } cells)
        {
            ApplySpecs(cells.Count);
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                var tb = new TextBlock { DataContext = cell };
                tb.Classes.Add("list-cell");
                if (i == 0) tb.Classes.Add("accent");   // 第 1 列（分类）用强调色，与其它列表页一致
                tb.Bind(TextBlock.TextProperty, new Binding(nameof(SysInfoCell.Text)));
                tb.Bind(IsVisibleProperty, new Binding(nameof(SysInfoCell.IsVisible)));
                SetColumn(tb, i);
                Children.Add(tb);
            }
        }
    }

    /// <summary>
    /// 规格串 → <see cref="ColumnDefinitions"/>。
    /// ⚠️ 用 <c>Split(',')</c> 且**不**丢空片段（丢空会让后面的列整体左移 ⇒ 列索引错位）；
    /// 最后一个兜底：列定义数永远 ≥ 子元素数（否则 Grid 布局期会越界抛异常）。
    /// </summary>
    private void ApplySpecs(int count)
    {
        var defs = new ColumnDefinitions();

        var specs = ColumnSpecs;
        if (!string.IsNullOrWhiteSpace(specs))
        {
            foreach (var part in specs.Split(','))
                defs.Add(new ColumnDefinition { Width = ParseWidth(part) });
        }

        while (defs.Count < count) defs.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions = defs;
    }

    /// <summary>单个宽度片段（空 / 非法 ⇒ <c>Auto</c>，⛔ 不让布局期抛异常）。</summary>
    private static GridLength ParseWidth(string part)
    {
        var s = (part ?? "").Trim();
        if (s.Length == 0) return GridLength.Auto;
        try { return GridLength.Parse(s); }
        catch { return GridLength.Auto; }
    }
}
