namespace NewToys.Shared.Models;

/// <summary>列表页的「一列」的元数据描述（规范 3.8 列显隐机制的数据载体）。（WHY: SH-012）</summary>
public sealed class ListColumn
{
    /// <summary>排序 / 显隐用的唯一键（如 <c>"Name"</c> / <c>"Id"</c>），与 <c>SortCommand</c> 的 <c>CommandParameter</c> 对齐。</summary>
    public string Key { get; }

    /// <summary>表头上显示的标题（不含排序箭头，箭头由 ViewModel 的 <c>XxxHeader</c> 负责）。</summary>
    public string Header { get; }

    /// <summary>列宽，Avalonia 的 <c>GridLength</c> 字符串（如 <c>"3*"</c> / <c>"200"</c> / <c>"Auto"</c>）。</summary>
    public string Width { get; }

    /// <summary>该列是否参与「左键点表头排序」。纯展示列（如简介）设为 <c>false</c>。</summary>
    public bool Sortable { get; }

    /// <summary>示例值（给"这一列的典型内容"一个宽度样本，如 ★ / 29.97 / .mp4 / 1:02:03）。（WHY: SH-013）</summary>
    public string? Sample { get; }

    public ListColumn(string key, string header, string width, bool sortable = true, string? sample = null)
    {
        Key = key;
        Header = header;
        Width = width;
        Sortable = sortable;
        Sample = sample;
    }
}
