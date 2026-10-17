using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 采集状态（节头 / 行）：<c>ok</c> / <c>warn</c> / <c>error</c> / <c>idle</c>。
/// <para>NT-068 起<b>着色</b>（节头状态点），配色走主题语义键：ok=AccentBrush / warn=CardSelectedTextBrush / error=DangerBrush / idle=SecondaryTextBrush。</para>
/// </summary>
public static class SysInfoStatus
{
    public const string Ok = "ok";

    /// <summary>非致命：stderr 有内容 / 退出码非 0 但 stdout 仍可解析。</summary>
    public const string Warn = "warn";

    /// <summary>该采集器本次失败（保留上次数据，节头标 error）。</summary>
    public const string Error = "error";

    /// <summary>还没采过（节体给引导文案）。</summary>
    public const string Idle = "idle";

    /// <summary>给人看的中文标签。</summary>
    public static string Label(string? status) => status switch
    {
        Ok => "正常",
        Warn => "有警告",
        Error => "出错",
        _ => "未采集",
    };

    /// <summary>只认真实行状态（idle 归一成 ok）。</summary>
    public static string NormalizeRow(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        Warn => Warn,
        Error => Error,
        _ => Ok,
    };
}

/// <summary>
/// 表格里的一格（NT-068 §三.13）。
/// <para>🔴 <see cref="Attr"/> = 输出 <c>attrs</c> 的<b>属性名字符串</b>（与 <c>pc_observations.attr</c> 同一键空间），
/// ⛔ 不再是 NT-067 的 1-based 列位。</para>
/// <para><see cref="Text"/> 可观察：刷新时就地改值 ⇒ 行对象与选中项不换、界面不闪。</para>
/// </summary>
public sealed partial class SysInfoCell : ObservableObject
{
    public SysInfoCell(string attr, string title, string text, bool isVisible)
    {
        Attr = attr;
        Title = title;
        Text = text;
        IsVisible = isVisible;
    }

    /// <summary>列 key（= 属性名）。</summary>
    public string Attr { get; }

    /// <summary>列标题（详情卡 / 复制时当小标签用）。</summary>
    public string Title { get; }

    /// <summary>详情卡 <c>FieldKind.List</c> 的标签字段（历史名字，等价 <see cref="Title"/>）。</summary>
    public string Header => Title;

    /// <summary>单元格文本。</summary>
    [ObservableProperty] private string _text = "";

    /// <summary>该列当前是否显示。</summary>
    [ObservableProperty] private bool _isVisible;
}

/// <summary>
/// 表头的一格：节内表头按钮（点 = 本节按该列排序）+ 列显隐菜单。
/// </summary>
public sealed partial class SysInfoHeaderCell : ObservableObject
{
    public SysInfoHeaderCell(string attr, string title, string width, bool isVisible)
    {
        Attr = attr;
        Title = title;
        Text = title;
        Width = width;
        IsVisible = isVisible;
    }

    /// <summary>列 key（= 属性名）。</summary>
    public string Attr { get; }

    /// <summary>列标题原文（不含排序箭头）。</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>表头按钮文案（标题 + 排序箭头）。</summary>
    [ObservableProperty] private string _text = "";

    /// <summary>列宽规格（<c>140</c> / <c>2*</c> / <c>Auto</c>）；本期不接拖拽，写回 <c>script.json</c> 的列 schema。</summary>
    [ObservableProperty] private string _width = "";

    /// <summary>该列当前是否显示。</summary>
    [ObservableProperty] private bool _isVisible;

    /// <summary>切换这一列显隐（**每项自带命令**：菜单项在弹出树里取不到页面 VM 的命令）。</summary>
    public ICommand? ToggleCommand { get; set; }

    /// <summary>点这一列表头 → 本节按该列排序（同样自带命令）。</summary>
    public ICommand? SortCommand { get; set; }
}

/// <summary>
/// 信息面板里的一行：某采集器某个 <c>row_key</c> 的一组属性值。
/// </summary>
public sealed partial class SysInfoRow : ObservableObject
{
    public SysInfoRow(string itemId, string rowKey, IReadOnlyList<SysInfoCell> cells)
    {
        ItemId = itemId;
        RowKey = rowKey;
        Cells = cells;
    }

    /// <summary>出这行的采集器（<see cref="ScriptItem.Id"/>）。</summary>
    public string ItemId { get; }

    /// <summary>同一采集器内唯一的行键。</summary>
    public string RowKey { get; }

    /// <summary>各列的值（顺序 = 该采集器的列 schema 顺序）。</summary>
    public IReadOnlyList<SysInfoCell> Cells { get; }

    /// <summary>行状态（<see cref="SysInfoStatus"/>，着色由节头承担，行内只落字段）。</summary>
    [ObservableProperty] private string _status = SysInfoStatus.Ok;

    /// <summary>按列下标取文本（排序 / 复制用；越界返回空串）。</summary>
    public string this[int index]
        => index >= 0 && index < Cells.Count ? Cells[index].Text : "";

    /// <summary>按属性名取文本（筛选 / 复制用；没有该属性返回空串）。</summary>
    public string Of(string attr)
    {
        foreach (var c in Cells)
            if (string.Equals(c.Attr, attr, System.StringComparison.OrdinalIgnoreCase))
                return c.Text;
        return "";
    }
}
