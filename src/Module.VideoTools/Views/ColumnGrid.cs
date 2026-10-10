using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace NewToys.VideoTools.Views;

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
/// <b>规格串规则</b>：逗号分隔，<b>可见列写它的星号宽度</b>（如 <c>3*</c>）、<b>隐藏列写 <c>0</c></b>。
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
/// 📌 本控件在 <c>Module.PcTools</c> 有一份同名同实现的副本（模块之间禁止互相引用，见规范 4.1.1）。
/// 两份必须保持一致；改动时同步改。
/// </para>
/// </summary>
public class ColumnGrid : Grid
{
    /// <summary>列宽规格串（可绑定）。例：<c>"3*,4*,0,1.6*"</c> 表示第 3 列已隐藏。</summary>
    public static readonly StyledProperty<string?> ColumnSpecsProperty =
        AvaloniaProperty.Register<ColumnGrid, string?>(nameof(ColumnSpecs));

    /// <summary>
    /// 列宽规格串：可见列写星号宽度（<c>3*</c>），隐藏列写 <c>0</c>。
    /// 由 VM 的 <c>VisibleColumnSpecs</c> 提供（见 <c>VideoListViewModel</c>）。
    /// </summary>
    public string? ColumnSpecs
    {
        get => GetValue(ColumnSpecsProperty);
        set => SetValue(ColumnSpecsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColumnSpecsProperty)
            Rebuild();
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
}
