using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shared.Models;

/// <summary>筛选运算符（<see cref="FilterClause.Op"/> 的合法取值）。</summary>
public static class FilterOperators
{
    public const string Contains = "contains";
    public const string NotContains = "notcontains";

    // ⚠️ 别叫 `Equals` —— 那会隐藏 `object.Equals(object?)` 并触发 CS0108；
    //    加 `new` 只是压警告，改名更干净（Key 只是个字符串常量，改名不影响落盘格式）。
    public const string Equal = "eq";
    public const string NotEqual = "ne";
    public const string Greater = "gt";
    public const string Less = "lt";
    public const string Empty = "empty";
    public const string NotEmpty = "notempty";
}

/// <summary>下拉框里的一项运算符（给 ComboBox 的 ItemTemplate 用）。</summary>
public sealed class FilterOperatorItem
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>该运算符是否<b>不需要填值</b>（「为空 / 不为空」）。</summary>
    public bool NoValue { get; set; }

    public static ObservableCollection<FilterOperatorItem> All { get; } = new()
    {
        new FilterOperatorItem { Key = FilterOperators.Contains,    Label = "包含" },
        new FilterOperatorItem { Key = FilterOperators.NotContains, Label = "不包含" },
        new FilterOperatorItem { Key = FilterOperators.Equal,      Label = "等于" },
        new FilterOperatorItem { Key = FilterOperators.NotEqual,   Label = "不等于" },
        new FilterOperatorItem { Key = FilterOperators.Greater,     Label = "大于" },
        new FilterOperatorItem { Key = FilterOperators.Less,        Label = "小于" },
        new FilterOperatorItem { Key = FilterOperators.Empty,       Label = "为空", NoValue = true },
        new FilterOperatorItem { Key = FilterOperators.NotEmpty,    Label = "不为空", NoValue = true },
    };
}

/// <summary>
/// <b>一条</b>筛选条件（「多项筛选」的最小单位）：<b>哪一列 + 怎么比 + 比什么</b>。
/// <para>
/// 多条条件之间是「<b>且</b>」：全部满足的行才显示。
/// 值<b>留空的条件不生效</b>（这样"先加条件后填值"不会瞬间把列表筛空），
/// 「为空 / 不为空」两个运算符不需要值，加上即生效。
/// </para>
/// <para>
/// 比较规则：列能给出数值（<c>FieldNumber</c>）且值也能解析成数字 → <b>按数字比</b>；
/// 否则按文本比（忽略大小写）。这样「大小 &gt; 100」不会变成字符串比较的怪结果。
/// </para>
/// </summary>
public partial class FilterClause : ObservableObject
{
    [ObservableProperty] private ListColumn? _column;

    [ObservableProperty] private string _op = FilterOperators.Contains;

    [ObservableProperty] private string _value = "";

    /// <summary>是否启用（取消勾选 = 临时停用这条，但不删掉）。</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>条件作用的列键（<see cref="Column"/> 为空时返回空串 = 不生效）。</summary>
    public string ColumnKey => Column?.Key ?? "";

    /// <summary>本条是否真的参与过滤（勾了 + 有值 / 不需要值）。</summary>
    public bool IsActive
    {
        get
        {
            if (!Enabled || ColumnKey.Length == 0) return false;
            if (IsNoValueOp) return true;
            return (Value ?? "").Trim().Length > 0;
        }
    }

    /// <summary>当前运算符是不是「为空 / 不为空」（界面据此隐藏输入框）。</summary>
    public bool IsNoValueOp
        => Op is FilterOperators.Empty or FilterOperators.NotEmpty;

    /// <summary>运算符下拉框的数据源（全体运算符）。</summary>
    public ObservableCollection<FilterOperatorItem> Operators => FilterOperatorItem.All;

    /// <summary>
    /// 运算符下拉框的**选中项**（XAML 只能绑对象，不能直接把 string 映到下拉项上，
    /// 所以这里做一层桥：读 = 按 <see cref="Op"/> 找项，写 = 把项的 Key 写回 <see cref="Op"/>）。
    /// </summary>
    public FilterOperatorItem? OperatorItem
    {
        get => FilterOperatorItem.All.FirstOrDefault(o => o.Key == Op);
        set { if (value != null) Op = value.Key; }
    }

    partial void OnOpChanged(string value)
    {
        OnPropertyChanged(nameof(OperatorItem));
        OnPropertyChanged(nameof(IsNoValueOp));
        OnPropertyChanged(nameof(NeedsValue));
        OnPropertyChanged(nameof(IsActive));
    }

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(IsActive));

    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(IsActive));

    partial void OnColumnChanged(ListColumn? value) => OnPropertyChanged(nameof(IsActive));

    /// <summary>界面上「要不要显示这个值输入框」。</summary>
    public bool NeedsValue => !IsNoValueOp;

    /// <summary>一行是否命中本条条件（不生效的条件一律放行）。</summary>
    public bool Matches(string? fieldText, double? fieldNumber)
    {
        if (!IsActive) return true;

        var text = fieldText ?? "";
        var input = (Value ?? "").Trim();

        if (Op == FilterOperators.Empty) return text.Trim().Length == 0;
        if (Op == FilterOperators.NotEmpty) return text.Trim().Length > 0;

        // 数值优先：两边都能当数字就用数字比（"1024" 不该小于 "512"）
        if (fieldNumber != null && double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
        {
            return Op switch
            {
                FilterOperators.Greater => fieldNumber.Value > num,
                FilterOperators.Less => fieldNumber.Value < num,
                FilterOperators.Equal => Math.Abs(fieldNumber.Value - num) < 1e-9,
                FilterOperators.NotEqual => Math.Abs(fieldNumber.Value - num) >= 1e-9,
                _ => TextMatch(text, input)
            };
        }

        return TextMatch(text, input);
    }

    private bool TextMatch(string text, string input)
    {
        var cmp = StringComparison.CurrentCultureIgnoreCase;
        return Op switch
        {
            FilterOperators.Contains => text.Contains(input, cmp),
            FilterOperators.NotContains => !text.Contains(input, cmp),
            FilterOperators.Equal => string.Equals(text, input, cmp),
            FilterOperators.NotEqual => !string.Equals(text, input, cmp),
            FilterOperators.Greater => string.Compare(text, input, cmp) > 0,
            FilterOperators.Less => string.Compare(text, input, cmp) < 0,
            _ => true
        };
    }

    /// <summary>一句话描述本条条件（状态行 / 摘要用）。</summary>
    public override string ToString()
    {
        var col = Column?.Header ?? "?";
        var label = "";
        foreach (var o in FilterOperatorItem.All)
            if (o.Key == Op) { label = o.Label; break; }
        return IsNoValueOp ? $"{col} {label}" : $"{col} {label} “{Value}”";
    }
}

/// <summary>
/// 表头右键「🔎 筛选」子菜单里的一个快捷筛选项（v3.19 / NT-006）：点一下即按「某列 = 某值」过滤。
/// <para>⚠️ <see cref="Command"/> 由 <c>ListPageViewModelBase</c> 填 —— 菜单项在**弹出树**里取不到页面 VM 的命令，
/// 故把命令挂到数据项自己身上，XAML 子菜单项直接绑 <c>{Binding Command}</c>（相对项自身的 DataContext）。</para>
/// </summary>
public sealed class FilterQuickItem
{
    public string Label { get; set; } = "";
    public string ColumnKey { get; set; } = "";
    public string Value { get; set; } = "";
    public System.Windows.Input.ICommand? Command { get; set; }
}
