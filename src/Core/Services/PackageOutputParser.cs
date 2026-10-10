using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NewToys.Core.Services;

/// <summary>
/// 包管理器命令行输出的解析器（**纯函数**，不碰进程、不碰 UI）。
/// <para>
/// 核心思路：winget 的表格是**固定列宽**（每列宽度 = 表头与全部单元格里最宽者），
/// 所以「**用表头里各列标签的起始下标切片**」最稳 ——
/// 远好过"按两个以上空格 split"：后者会被包名里的连续空格、以及被截断成 `…` 的长名称搞崩。
/// </para>
/// <para>
/// 会被滤掉的行：① ANSI 颜色转义；② 进度条 / 转轮（含 <c>\r</c>、<c>\b</c> 与 <c>█ ▒ ░ \) | / -</c> 填充）；
/// ③ 分隔线（整行只有 <c>-</c>）；④ 表头之上的提示行（"已配置的源需要更新"之类）。
/// </para>
/// </summary>
internal static class PackageOutputParser
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex LabelRegex = new(
        @"(Name|Id|Version|Available|Source|Match|Moniker|Tag|Argument|Type|Explicit|Last Updated)",
        RegexOptions.Compiled);

    /// <summary>表头列标签（winget 的列标题是英文硬编码，不随系统语言本地化）。</summary>
    private static readonly string[] ColumnLabels =
        { "Name", "Id", "Version", "Available", "Source", "Match", "Moniker", "Tag", "Argument", "Type", "Explicit", "Last Updated" };

    /// <summary>去掉 ANSI 转义、统一换行、去掉退格符 —— 后续一律按 <c>\n</c> 切行。</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = Ansi.Replace(raw, "");
        s = s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\b", "");
        return s;
    }

    /// <summary>
    /// 解析一段包管理器表格输出。
    /// </summary>
    /// <param name="raw">原始 stdout。</param>
    /// <param name="requiredHeaderLabels">
    /// 判定"这一行是表头"所需的标签（全部命中才算）。
    /// 例：<c>list</c> / <c>search</c> 用 <c>Name+Id</c>；<c>source list</c> 用 <c>Name+Argument</c>。
    /// </param>
    /// <returns>每行一个字典（列标签 → 单元格文本），保持输出顺序。</returns>
    public static List<Dictionary<string, string>> ParseTable(string? raw, params string[] requiredHeaderLabels)
    {
        var rows = new List<Dictionary<string, string>>();
        var lines = Clean(raw).Split('\n');
        if (lines.Length == 0) return rows;

        // ---- 1. 找表头 ----
        var headerIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            if (IsNoiseLine(line)) continue;
            if (requiredHeaderLabels.All(l => Regex.IsMatch(line, $@"(^|\s){Regex.Escape(l)}(\s|$)")))
            {
                headerIndex = i;
                break;
            }
        }
        if (headerIndex < 0) return rows;

        // ---- 2. 用表头标签的起始下标算出每列的 [start, end) ----
        var columns = LabelRegex.Matches(lines[headerIndex])
            .Select(m => (Label: m.Value, Start: m.Index))
            .Where(c => ColumnLabels.Contains(c.Label))
            .GroupBy(c => c.Label)
            .Select(g => g.First())                 // 同一标签只取首次出现
            .OrderBy(c => c.Start)
            .ToList();
        if (columns.Count == 0) return rows;

        // ---- 3. 逐行切片 ----
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;
            if (IsNoiseLine(line)) continue;
            if (IsSeparator(line)) continue;        // ---- 分隔线

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < columns.Count; c++)
            {
                var start = columns[c].Start;
                var end = c + 1 < columns.Count ? columns[c + 1].Start : line.Length;
                if (start >= line.Length) { row[columns[c].Label] = ""; continue; }
                if (end > line.Length) end = line.Length;
                row[columns[c].Label] = line.Substring(start, end - start).Trim();
            }

            // 再切一次兜底：某些行整行只有第一个单元格（长名称换行）→ 丢弃
            if (!row.TryGetValue("Name", out var name) || name.Length == 0) continue;
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// 从任意输出里粗暴地捞出"长得像包 Id"的记号（含点号且无空格）。
    /// <para>
    /// 用于 <c>winget pin list</c>：不同 winget 版本那张表的列头差异较大，
    /// 而包 Id 的形状非常稳定（<c>Publisher.Package</c>），所以直接按形状捞最省事、也最不容易漏。
    /// </para>
    /// </summary>
    public static List<string> ParsePackageIds(string? raw)
    {
        var ids = new List<string>();
        // 先干掉 URL：源地址（https://cdn.winget.microsoft.com/cache）里也带点号，
        // 不先删掉就会被当成"包 Id"混进来。
        var body = Regex.Replace(Clean(raw), @"\bhttps?://\S+", " ");
        foreach (Match m in Regex.Matches(body, @"[A-Za-z0-9][A-Za-z0-9._+-]*\.[A-Za-z0-9][A-Za-z0-9._+-]*"))
        {
            var v = m.Value;
            if (v.Length > 2 && !ids.Contains(v, StringComparer.OrdinalIgnoreCase)) ids.Add(v);
        }
        return ids;
    }

    /// <summary>
    /// 解析 <c>winget show</c> 的 <c>标签: 值</c> 输出（字段名可能中英混排，两种都认）。
    /// 多行字段（说明 / 协议）会把后续不含标签的行接上。
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
                var key = NormalizeLabel(line[..idx].Trim());
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
        _ => null
    };

    /// <summary>整行只有短横线与空白（winget 的表格分隔线）。</summary>
    private static bool IsSeparator(string line)
    {
        var t = line.Trim();
        return t.Length >= 3 && t.All(ch => ch == '-' || ch == ' ' || ch == '\u2500');
    }

    /// <summary>
    /// 进度条 / 转轮 / 装饰行：winget 把它们写在同一个 <c>\r</c> 行上，
    /// 我们换成 <c>\n</c> 之后会变成独立行，必须在解析前剔除。
    /// </summary>
    private static bool IsNoiseLine(string line)
    {
        var t = line.TrimStart();
        if (t.Length == 0) return true;
        var first = t[0];
        if (first is '\\' or '|' or '/' or '-' or '\u2588' /*█*/ or '\u2592' /*▒*/
            or '\u2591' /*░*/ or '\u2593' /*▓*/ or '\u280B' /*⠋*/ or '\u28FF') return true;
        // 纯框线（│ ─ ┌ └ ├ …）
        if (t.Trim(' ', '\u2502', '\u2500', '\u250C', '\u2514', '\u251C', '\u2524', '\u2534').Length == 0) return true;
        return false;
    }
}
