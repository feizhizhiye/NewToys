using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

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
/// <b>规格串规则</b>：逗号分隔，<b>可见列写它的星号宽度</b>（如 <c>3*</c>）、<b>隐藏列写 <c>0</c></b>。
/// 用 <c>0</c> 而不是「真删列」，是为了让<b>列索引保持不动</b> ——
/// 表头与行模板永远共用同一份列定义（规范 3.8.2 对齐铁律①）。
/// </para>
/// <para>
/// ⚠️ 每次赋值都<b>新建</b>一个 <see cref="ColumnDefinitions"/> 实例：同一个实例不能被多个 Grid 共用。
/// </para>
/// </summary>
public class ColumnGrid : Grid
{
    /// <summary>列宽规格串（可绑定）。例：<c>"3*,4*,0,1.6*"</c> 表示第 3 列已隐藏。</summary>
    public static readonly StyledProperty<string?> ColumnSpecsProperty =
        AvaloniaProperty.Register<ColumnGrid, string?>(nameof(ColumnSpecs));

    /// <summary>
    /// 列宽规格串：可见列写星号宽度（<c>3*</c>），隐藏列写 <c>0</c>。
    /// 由 VM 的 <c>VisibleColumnSpecs</c> 提供（见各列表页 ViewModel）。
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
            ColumnDefinitions = ParseSpecs(change.GetNewValue<string?>());
    }

    private static ColumnDefinitions ParseSpecs(string? specs)
    {
        var defs = new ColumnDefinitions();
        if (string.IsNullOrWhiteSpace(specs)) return defs;

        foreach (var part in specs.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = part.Trim();
            if (s.Length == 0) continue;

            // GridLength.Parse 认得 "3*" / "120" / "0" / "Auto"；规格串由本工程 VM 生成，不会非法。
            defs.Add(new ColumnDefinition { Width = GridLength.Parse(s) });
        }

        return defs;
    }
}
