namespace NewToys.MediaAssets.Models;

/// <summary>
/// 筛选条下拉的一项（**显示文案 + 值**）：值为空串 = 「全部」（不加这条过滤）。
/// <para>
/// 为什么不用元组：ComboBox 要 <c>DisplayMemberPath</c> 绑具名属性，元组只有 <c>Item1/Item2</c>，
/// 绑定写出来没人看得懂 —— 多一个三行的小类换来 XAML 可读。
/// </para>
/// </summary>
public sealed class FilterOption
{
    public string Label { get; }
    public string Value { get; }

    public FilterOption(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public override string ToString() => Label;
}
