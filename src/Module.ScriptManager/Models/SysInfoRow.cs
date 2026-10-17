using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 行 / 脚本状态（NT-067 §三.3）：<c>ok</c> / <c>warn</c> / <c>error</c>。
/// <para>本期**不着色**，字段只落模型 —— 它是 §2.1-4 管道 / 工作流引擎的地基。</para>
/// </summary>
public static class SysInfoStatus
{
    public const string Ok = "ok";

    /// <summary>非致命：stderr 有内容但 stdout 仍可解析（只记不上报）。</summary>
    public const string Warn = "warn";

    /// <summary>该脚本失败：保留上次值并标 error，状态栏出「⚠ 脚本失败：&lt;file&gt;」。</summary>
    public const string Error = "error";
}

/// <summary>
/// 表格里的一格（列位由 manifest 的 <c>columns[].key</c> 决定，值 = <c>values[key-1]</c>）。
/// <para>🔴 <see cref="Text"/> 是**可观察**的：定时刷新时就地改它，行对象与选中项都不换 ⇒ 列表不重建、不闪。</para>
/// </summary>
public sealed partial class SysInfoCell : ObservableObject
{
    public SysInfoCell(string columnKey, int key, string header, string text, bool isVisible)
    {
        ColumnKey = columnKey;
        Key = key;
        Header = header;
        Text = text;
        IsVisible = isVisible;
    }

    /// <summary>列键（= <c>"c" + key</c>），列显隐 / 排序 / 筛选都用它（与 <c>ListColumn.Key</c> 同一口径）。</summary>
    public string ColumnKey { get; }

    /// <summary>1-based 稳定列位。</summary>
    public int Key { get; }

    /// <summary>列标题（详情卡里当小标签用）。</summary>
    public string Header { get; }

    /// <summary>单元格文本。</summary>
    [ObservableProperty] private string _text = "";

    /// <summary>该列当前是否显示（列显隐变化时就地翻转，行对象不重建）。</summary>
    [ObservableProperty] private bool _isVisible;
}

/// <summary>
/// 表头的一格：既给表头按钮用（<see cref="Text"/> 含排序箭头），也给「⚙ 列设置」面板与列显隐右键菜单用。
/// </summary>
public sealed partial class SysInfoHeaderCell : ObservableObject
{
    public SysInfoHeaderCell(string columnKey, int key, string title, string width, bool isVisible)
    {
        ColumnKey = columnKey;
        Key = key;
        Title = title;
        Text = title;
        Width = width;
        IsVisible = isVisible;
    }

    /// <summary>列键（= <c>"c" + key</c>）。</summary>
    public string ColumnKey { get; }

    /// <summary>1-based 稳定列位。</summary>
    public int Key { get; }

    /// <summary>列标题原文（不含排序箭头；右键菜单 / 列设置面板用它）。</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>表头按钮文案（标题 + 排序箭头，由 <c>RefreshView</c> 维护）。</summary>
    [ObservableProperty] private string _text = "";

    /// <summary>列宽规格（<c>140</c> / <c>2*</c> / <c>Auto</c>；「⚙ 列设置」里可改，保存即写回 manifest）。</summary>
    [ObservableProperty] private string _width = "";

    /// <summary>该列当前是否显示。</summary>
    [ObservableProperty] private bool _isVisible;

    /// <summary>
    /// 「切换这一列的显隐」命令（**每项自带命令**：菜单项 / 勾选框都在弹出树或模板里，
    /// 取不到页面 VM 的命令 —— 与基类 <c>FilterQuickItem</c> 同一套路）。
    /// </summary>
    public System.Windows.Input.ICommand? ToggleCommand { get; set; }
}

/// <summary>
/// 信息面板页的一行（NT-067 §三.3）：`SysInfoRow { ScriptId, RowKey, Status, Values[] }` 的可绑定形态
/// —— <c>Values[]</c> 在这里展开成 <see cref="Cells"/>（每格带列键 / 标题 / 可见性）。
/// </summary>
public sealed partial class SysInfoRow : ObservableObject
{
    public SysInfoRow(string scriptId, string rowKey, string group, IReadOnlyList<SysInfoCell> cells)
    {
        ScriptId = scriptId;
        RowKey = rowKey;
        Group = group;
        Cells = cells;
    }

    /// <summary>出这行的脚本：<c>builtin</c>（内置采集器）或 <c>scripts[].file</c> 去扩展名。</summary>
    public string ScriptId { get; }

    /// <summary>同一脚本内唯一的行键（<c>key</c> 字段 → 第 1 列文本 → <c>#序号</c>）。</summary>
    public string RowKey { get; }

    /// <summary>脚本的 <c>group</c>（归类 / 快捷筛选用）。</summary>
    public string Group { get; }

    /// <summary>各列的值（顺序 = manifest 的 <c>columns[]</c> 数组顺序）。</summary>
    public IReadOnlyList<SysInfoCell> Cells { get; }

    /// <summary>行状态（<see cref="SysInfoStatus"/>）；本期只落字段、不着色。</summary>
    [ObservableProperty] private string _status = SysInfoStatus.Ok;

    /// <summary>按列下标取文本（排序 / 筛选用；越界返回空串）。</summary>
    public string this[int index]
        => index >= 0 && index < Cells.Count ? Cells[index].Text : "";
}
