using System;
using System.Collections.Generic;
using System.Linq;

namespace NewToys.SystemTools.Services;

/// <summary>机读输出（TSV）的通用解析（与具体包管理器无关）。（WHY: ST-025）</summary>
internal static class TsvRows
{
    /// <summary>TSV 文本 → 行模型（每行一个字典：列名 → 单元格）。</summary>
    /// <param name="raw">原始 stdout。</param>
    /// <param name="hasHeader">首行是否为列名行（true = 用它当键；false = 用 <paramref name="columnNames"/>）。</param>
    /// <param name="columnNames">
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
