namespace NewToys.Shared.Models;

/// <summary>
/// 列表页的「一列」的元数据描述（规范 3.8 列显隐机制的数据载体）。
/// <para>
/// 只描述列本身（键 / 标题 / 宽度 / 是否可排序），**不参与排序逻辑、也不存可见性状态** ——
/// 可见性由各列表页 ViewModel 上的 <c>IsXxxVisible</c> 属性单独管理（这样 XAML 绑定能拿到
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/> 通知，列显隐即时生效）。
/// </para>
/// </summary>
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

    public ListColumn(string key, string header, string width, bool sortable = true)
    {
        Key = key;
        Header = header;
        Width = width;
        Sortable = sortable;
    }
}
