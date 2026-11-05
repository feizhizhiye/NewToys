using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NewToys.SystemTools.Services;

/// <summary>winget 命令行输出的解析器（纯函数，不碰进程、不碰 UI）。（WHY: ST-015）</summary>
internal static class PackageOutputParser
{
    /// <summary>表格列标签：规范名 + 该列在输出里可能出现的写法（英文 + 简体中文）。（WHY: ST-016）</summary>
    private static readonly (string Canonical, string[] Aliases)[] LabelDefs =
    {
        ("Name",        new[] { "Name", "名称" }),
        ("Id",          new[] { "Id", "ID", "程序包 ID" }),
        ("Version",     new[] { "Version", "版本" }),
        ("Available",   new[] { "Available", "可用", "可用版本" }),
        ("Source",      new[] { "Source", "源" }),
        ("Match",       new[] { "Match", "匹配" }),
        ("Moniker",     new[] { "Moniker", "绰号", "标记" }),
        ("Tag",         new[] { "Tag", "标签" }),
        ("Argument",    new[] { "Argument", "参数" }),
        ("Type",        new[] { "Type" }),   // ⚠️ 不收中文"类型"：pin list 的表头是"包钉类型"，会被当成两个列
        ("Explicit",    new[] { "Explicit", "显式" }),
        ("LastUpdated", new[] { "Last Updated", "上次更新", "最后更新" }),
    };

    /// <summary>别名 → 规范名（由 <see cref="LabelDefs"/> 展平；查表用，⛔ 不改）。</summary>
    private static readonly Dictionary<string, string> AliasToCanonical = BuildAliasMap();

    /// <summary>在表头行里定位「列标签」用的正则（长别名优先，避免短词先命中）。</summary>
    private static readonly Regex LabelRegex = FixedWidthTable.BuildAliasRegex(AliasToCanonical);

    private static Dictionary<string, string> BuildAliasMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (canonical, aliases) in LabelDefs)
            foreach (var a in aliases) map[a] = canonical;
        return map;
    }

    /// <summary>认识但不装进模型的 winget show 字段。（WHY: ST-017）</summary>
    private static readonly HashSet<string> IgnoredLabels = new(StringComparer.Ordinal)
    {
        "发布服务器 URL", "发布服务器支持 URL", "作者", "绰号", "版权", "版权所有",
        "许可证 URL", "安装程序类型", "安装程序 URL", "安装程序 SHA256", "安装程序区域设置",
        "发布日期", "协议", "文档", "安装范围", "最低操作系统版本", "标签", "依赖项",
        "Copyright", "Copyright Url", "Author", "Installer Type", "Installer Url",
        "Installer SHA256", "Release Date", "License Url", "Agreements", "Documentations",
    };

    /// <summary>去掉 ANSI 转义、统一换行、去掉退格符 —— 后续一律按 <c>\n</c> 切行。</summary>
    public static string Clean(string? raw) => FixedWidthTable.Clean(raw);

    /// <summary>解析一段包管理器表格输出。</summary>
    /// <param name="raw">原始 stdout。</param>
    /// <param name="requiredHeaderLabels">
    /// <returns>每行一个字典（**规范列名** → 单元格文本），保持输出顺序。</returns>
    public static List<Dictionary<string, string>> ParseTable(string? raw, params string[] requiredHeaderLabels)
    {
        var rows = new List<Dictionary<string, string>>();
        var lines = Clean(raw).Split('\n');
        if (lines.Length == 0) return rows;

        // ---- 1. 找表头（用规范名判定，与输出语言无关） ----
        var headerIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            if (IsNoiseLine(line)) continue;
            var labels = FindLabels(line);
            if (requiredHeaderLabels.All(r => labels.Any(l => l.Label == r)))
            {
                headerIndex = i;
                break;
            }
        }
        if (headerIndex < 0) return rows;

        // ---- 2. 用列标签的起始下标算出每列的 [start, end) ----
        var columns = FindLabels(lines[headerIndex]);
        if (columns.Count == 0) return rows;

            // ---- 3. 逐行切片（通用机械在 FixedWidthTable；续行合并是 winget 固有启发式） ----
            for (var i = headerIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Trim().Length == 0) continue;
                if (FixedWidthTable.IsNoiseLine(line)) continue;
                if (FixedWidthTable.IsSeparator(line)) continue;        // ---- 分隔线

                var row = FixedWidthTable.SliceRow(line, columns);

            // 整行只剩第一个单元格（长名称换行那种残行）→ 丢弃
            if (!row.TryGetValue("Name", out var name) || name.Length == 0) continue;

            // 🔴 名称自动换行时 Id 也会折到下一行（本行只剩 `e`，完整 `Microsoft.Edge` 在下一行）。
            //    三条同时成立才判续行：① 本行 Id 是最后有值的一列且整行顶到行尾；② 下一行 Id 位有内容、Version 位为空；
            //    ③ 两段合起来像一条 Id（本行段不含 `.` 而下段含，或本行段 < 4 字符且两段都不含空格）。
            //    ⚠️ 判成续行时保留本行 Name（别用下行的空 Name 覆盖），只拼 Id；被切断的 Version（`1…`）直接丢掉。
            if (row.TryGetValue("Id", out var idCell) && idCell.Length > 0 && IdIsLastColumn(line, columns, row)
                && i + 1 < lines.Length && TryGetColumnAt(lines[i + 1], columns, "Id", out var rest) && rest.Length > 0
                && TryGetColumnAt(lines[i + 1], columns, "Version", out var nextVer) && nextVer.Length == 0
                && ((!idCell.Contains('.') && rest.Contains('.'))
                    || (idCell.Length < 4 && !idCell.Contains(' ') && !rest.Contains(' '))))
            {
                row["Id"] = idCell + rest;
                if (row.TryGetValue("Version", out var v0) && v0.Length > 0 && v0.Length <= 2) row["Version"] = "";
                i++;                                 // 续行已消费（否则会被当成一条孤立行）
            }

            rows.Add(row);
        }
        return rows;
    }

    // ==================== 显示宽度 / 切片 / 回填 / 噪行 ====================
    // 🔴 全部在 FixedWidthTable（通用化；winget 实测判据四条不变）。
    // winget 只通过上面的包装调用；本文件只剩固有启发式（别名表 / 续行合并 / pin 兜底 / 标签-值）。

    /// <summary>Id 是否就是本行**有值的最右一列**（⇒ 换行时它会被从中间切断）。</summary>
    private static bool IdIsLastColumn(string line, List<(string Label, int StartW)> columns, Dictionary<string, string> row)
    {
        var idx = columns.FindIndex(c => c.Label == "Id");
        if (idx < 0 || line.Length == 0 || char.IsWhiteSpace(line[^1])) return false;

        for (var c = idx + 1; c < columns.Count; c++)
        {
            if (row.TryGetValue(columns[c].Label, out var v) && v.Length > 0) return false;
        }
        return true;
    }

    /// <summary>按列切片规则取某一行的某列文本（同样按**显示宽度**切；机械在 <see cref="FixedWidthTable"/>）。</summary>
    private static bool TryGetColumnAt(string line, List<(string Label, int StartW)> columns, string label, out string value)
    {
        value = "";
        var idx = columns.FindIndex(c => c.Label == label);
        if (idx < 0) return false;

        var startW = columns[idx].StartW;
        var endW = idx + 1 < columns.Count ? columns[idx + 1].StartW : int.MaxValue;
        if (startW >= FixedWidthTable.DisplayWidth(line)) return true;

        value = FixedWidthTable.SliceByWidth(line, startW, endW).Trim();
        return true;
    }

    /// <summary>把一行里的列标签解析成「规范名 + 起始**显示列**」，按出现顺序排列（通用机械见 <see cref="FixedWidthTable"/>）。</summary>
    private static List<(string Label, int StartW)> FindLabels(string line)
        => FixedWidthTable.FindColumns(line, LabelRegex, AliasToCanonical);

    /// <summary>从任意输出里粗暴地捞出"长得像包 Id"的记号（含点号且无空格）。</summary>
    public static List<string> ParsePackageIds(string? raw)
        => ParsePins(raw).Select(p => p.Id).ToList();

    /// <summary>解析 winget pin list：按表头切片取 Id 与版本。（WHY: ST-020）</summary>
    public static List<(string Id, string Version)> ParsePins(string? raw)
    {
        var list = new List<(string Id, string Version)>();

        foreach (var row in ParseTable(raw, "Name", "Id"))
        {
            var id = row.GetValueOrDefault("Id", "").Trim();
            if (id.Length == 0) continue;
            if (list.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add((id, row.GetValueOrDefault("Version", "").Trim()));
        }

        if (list.Count == 0)                       // 兜底：极少数版本表头认不出
        {
            var body = Regex.Replace(Clean(raw), @"\bhttps?://\S+", " ");
            foreach (Match m in Regex.Matches(body, @"[A-Za-z0-9][A-Za-z0-9._+-]*\.[A-Za-z0-9][A-Za-z0-9._+-]*"))
            {
                var v = m.Value;
                if (v.Length <= 2) continue;
                // ⛔ 纯版本号（24.08 / 1.2.3）不是包 Id —— 老 bug 就出在这里
                if (Regex.IsMatch(v, @"^[0-9]+(\.[0-9]+)+$")) continue;
                if (list.Any(x => string.Equals(x.Id, v, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add((v, ""));
            }
        }
        return list;
    }

    /// <summary>
    /// 解析 <c>winget show</c> 的 <c>标签: 值</c> 输出（字段名可能中英混排，两种都认）。
    /// 多行字段（说明 / 协议）会把后续不含标签的行接上；
    /// 命中 <see cref="IgnoredLabels"/> 的字段会**终结**上一个字段并被丢弃（不再污染它）。
    /// </summary>
    public static Dictionary<string, string> ParseKeyValues(string? raw)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = Clean(raw);
        string? current = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (current != null && buffer.Count > 0)
                map[current] = string.Join("\n", buffer).Trim();
            buffer.Clear();
        }

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Trim().Length == 0) { if (current != null) buffer.Add(""); continue; }
            if (IsSeparator(line)) continue;

            var idx = line.IndexOf(':');
            if (idx > 0 && idx < 64)
            {
                var rawKey = line[..idx].Trim();

                // 认识但不装进模型的字段（发布服务器 URL / 作者 / 版权 …）→ 结束当前字段后丢弃
                if (IgnoredLabels.Contains(rawKey)) { Flush(); current = null; continue; }

                var key = NormalizeLabel(rawKey);
                if (key != null)
                {
                    Flush();
                    current = key;
                    buffer.Add(line[(idx + 1)..].Trim());
                    continue;
                }
            }
            if (current != null) buffer.Add(line.Trim());
        }
        Flush();
        return map;
    }

    /// <summary>把 winget show 的字段名（可能是中文）归一化成模型字段名；不认识的返回 null。</summary>
    private static string? NormalizeLabel(string label) => label switch
    {
        "Version" or "版本" => "Version",
        "Publisher" or "发布者" or "发行者" => "Publisher",
        "Description" or "说明" or "描述" => "Description",
        "Homepage" or "主页" or "主页 URL" or "主页 Url" or "项目主页" => "Homepage",
        "License" or "许可证" or "许可" => "License",
        "Install Location" or "安装位置" or "安装路径" => "InstallLocation",
        "Name" or "名称" => "Name",
        _ => null
    };

    /// <summary>整行只有短横线与空白（winget 的表格分隔线）。</summary>
    private static bool IsSeparator(string line) => FixedWidthTable.IsSeparator(line);

    /// <summary>
    /// 进度条 / 转轮 / 装饰行：winget 把它们写在同一个 <c>\r</c> 行上，
    /// 我们换成 <c>\n</c> 之后会变成独立行，必须在解析前剔除。
    /// </summary>
    private static bool IsNoiseLine(string line) => FixedWidthTable.IsNoiseLine(line);
}
