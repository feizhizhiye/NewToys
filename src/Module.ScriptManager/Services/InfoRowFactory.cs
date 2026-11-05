using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NewToys.ScriptManager.Models;
using NewToys.UI.Controls;

namespace NewToys.ScriptManager.Services;

/// <summary>「属性值 → SysInfoRow」的只读呈现内核（/ §三.13）。（WHY: SM-029）</summary>
public static class InfoRowFactory
{
    /// <summary>建一行（列顺序 = <paramref name="columns"/> 顺序；缺的属性补空串）。</summary>
    public static SysInfoRow Create(string itemId, string rowKey, string status,
        IReadOnlyList<ScriptColumn> columns, IReadOnlyDictionary<string, string> attrs)
    {
        var cells = new List<SysInfoCell>(columns.Count);
        foreach (var col in columns)
        {
            attrs.TryGetValue(col.Key, out var value);
            cells.Add(new SysInfoCell(col.Key, Title(col), value ?? "", col.Visible));
        }
        return new SysInfoRow(itemId, rowKey, cells) { Status = status };
    }

    /// <summary>
    /// 列 schema 为空时的兜底：直接按输出属性的出现顺序建列（标题 = 属性名）。
    /// <para>用于「库里已有观测但 <c>items[].columns</c> 丢了」这种脏数据（验收 §五.15 不崩）。</para>
    /// </summary>
    public static List<ScriptColumn> ColumnsFromAttrs(IEnumerable<string> attrsInOrder)
    {
        var list = new List<ScriptColumn>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in attrsInOrder)
        {
            if (string.IsNullOrWhiteSpace(attr) || !seen.Add(attr)) continue;
            list.Add(new ScriptColumn { Key = attr, Title = attr, Visible = true, Order = list.Count, Width = "" });
        }
        return list;
    }

    /// <summary>
    /// 列 schema 增量生成（§三.13）：新出现的属性**追加到末尾**，⛔ 不动已有列的顺序 / 宽度 / 标题。
    /// </summary>
    /// <returns>是否有新增列（需要回写 <c>script.json</c>）。</returns>
    public static bool AppendNewColumns(ScriptItem item, IEnumerable<ParsedInfoRow> rows)
    {
        if (item == null) return false;

        item.Columns ??= new List<ScriptColumn>();
        var known = new HashSet<string>(item.Columns.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var row in rows)
            foreach (var kv in row.Attrs)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || !known.Add(kv.Key)) continue;
                item.Columns.Add(new ScriptColumn
                {
                    Key = kv.Key,
                    Title = kv.Key,
                    Visible = true,
                    Order = item.Columns.Count,
                    Width = "",
                });
                changed = true;
            }

        return changed;
    }

    /// <summary>列的显示标题（<see cref="ScriptColumn.Title"/> 空 ⇒ 用 key）。</summary>
    public static string Title(ScriptColumn col)
        => string.IsNullOrWhiteSpace(col.Title) ? col.Key : col.Title;

    /// <summary>列宽规格串（表头与行共用同一份）：隐藏列写 0（列索引保持不动 ⇒ 永不错位）。（WHY: SM-030）</summary>
    public static string Specs(IReadOnlyList<ScriptColumn> columns)
    {
        if (columns.Count == 0) return "";
        var parts = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            if (!col.Visible) { parts[i] = "0"; continue; }
            var w = (col.Width ?? "").Trim();
            parts[i] = w.Length == 0 ? "*" : w;
        }
        return string.Join(",", parts);
    }

    /// <summary>表头格（含列显隐 / 排序命令的挂载点，命令由调用方填）。</summary>
    public static List<SysInfoHeaderCell> BuildHeaders(IReadOnlyList<ScriptColumn> columns)
    {
        var list = new List<SysInfoHeaderCell>(columns.Count);
        foreach (var col in columns)
            list.Add(new SysInfoHeaderCell(col.Key, Title(col), col.Width ?? "", col.Visible));
        return list;
    }

    /// <summary>把一次采集的属性就地写回已有行（⛔ 不换行对象）。</summary>
    public static void Apply(SysInfoRow row, IReadOnlyDictionary<string, string> attrs)
    {
        foreach (var cell in row.Cells)
            if (attrs.TryGetValue(cell.Attr, out var v))
                cell.Text = v;
    }

    /// <summary>
    /// 「值 = 属性名」对（<see cref="SysInfoRow"/> → 字典），写入库 / 落盘用。
    /// </summary>
    public static Dictionary<string, string> ToAttrs(SysInfoRow row)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in row.Cells) map[c.Attr] = c.Text;
        return map;
    }

    /// <summary><c>pc_observations</c> 的观测行（一行 × 每属性一条）。</summary>
    public static List<ScriptObservation> ToObservations(IReadOnlyList<ParsedInfoRow> rows)
    {
        var list = new List<ScriptObservation>();
        foreach (var row in rows)
            foreach (var kv in row.Attrs)
                list.Add(new ScriptObservation(row.RowKey, kv.Key, kv.Value));
        return list;
    }

    /// <summary>友好的耗时文本（<c>412ms</c> / <c>1.2s</c>）。</summary>
    public static string DurationText(long ms)
        => ms < 1000 ? ms.ToString(CultureInfo.InvariantCulture) + "ms"
                     : (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";
}
