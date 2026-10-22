using System;
using System.Collections.Generic;
using System.Linq;

namespace NewToys.SystemTools.Services;

/// <summary>
/// **机读输出**（TSV）的通用解析（NT-081 步骤 4，与具体包管理器无关）。
/// <para>
/// 对应后端姿势：<c>dpkg-query -W -f='${{Package}}\t${{Version}}\t${{Status}}\n'</c> 之类
/// —— 命令自己吐带分隔符的机读数据，⛔ 不碰固定列宽、⛔ 不关心中文表头。
/// </para>
/// <para>
/// JSON 变体暂缓（YAGNI）：现有 Linux 包管理器后端无一产出 JSON；真需要时按同格式补
/// <c>ParseJsonArray</c> 即可，页面无感。
/// </para>
/// </summary>
internal static class TsvRows
{
    /// <summary>
    /// TSV 文本 → 行模型（每行一个字典：列名 → 单元格）。
    /// </summary>
    /// <param name="raw">原始 stdout。</param>
    /// <param name="hasHeader">首行是否为列名行（true = 用它当键；false = 用 <paramref name="columnNames"/>）。</param>
    /// <param name="columnNames">
    /// <paramref name="hasHeader"/>=false 时的列名（顺序 = 列序）。
    /// 实际列数更多 ⇒ 多出的列命名为 <c>col&lt;n&gt;</c>；更少 ⇒ 截断（取前 N 列）。
    /// </param>
    public static List<Dictionary<string, string>> ParseTsv(
        string? raw, bool hasHeader, IReadOnlyList<string>? columnNames = null)
    {
        var rows = new List<Dictionary<string, string>>();
        if (string.IsNullOrEmpty(raw)) return rows;

        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n').Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return rows;

        IReadOnlyList<string> names;
        var body = lines;
        if (hasHeader)
        {
            names = lines[0].Split('\t');
            body = lines.Skip(1).ToList();
        }
        else
        {
            names = columnNames ?? Array.Empty<string>();
        }

        foreach (var line in body)
        {
            var cells = line.Split('\t');
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < cells.Length; c++)
            {
                var key = c < names.Count ? names[c] : "col" + (c + 1);
                if (string.IsNullOrEmpty(key)) key = "col" + (c + 1);
                row[key] = cells[c].Trim();
            }
            rows.Add(row);
        }
        return rows;
    }
}
