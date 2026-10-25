using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.UI.Controls;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 采集状态（节头 / 行）：<c>ok</c> / <c>warn</c> / <c>error</c> / <c>idle</c>。
/// <para><b>着色</b>（节头状态点），配色走主题语义键：ok=AccentBrush / warn=CardSelectedTextBrush / error=DangerBrush / idle=SecondaryTextBrush。</para>
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
/// 信息面板里的一行：某采集器某个 <c>row_key</c> 的一组属性值。
/// <para>🔴 单元格模型 <see cref="SysInfoCell"/> / <see cref="SysInfoHeaderCell"/> 已随 
/// <c>NewToys.UI.Controls</c>（<see cref="DynamicColumnGrid"/> 同文件），本模块只留行模型。</para>
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
