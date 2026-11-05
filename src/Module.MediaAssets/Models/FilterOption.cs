namespace NewToys.MediaAssets.Models;

/// <summary>筛选条下拉的一项（显示文案 + 值）：值为空串 = 「全部」（不加这条过滤）。（WHY: MA-008）</summary>
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
