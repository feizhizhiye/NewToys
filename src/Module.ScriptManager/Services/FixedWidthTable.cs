using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// **固定列宽表格**的通用解析机械（NT-081 步骤 4，与具体包管理器无关）。
/// <para>
/// 适用后端：winget / flatpak <c>--columns</c> / snap list 之类「按列对齐、表头在上」的人读输出。
/// 机读输出（TSV / JSON）⛔ 不走这里，见 <see cref="TsvRows"/>。
/// </para>
/// <para>
/// 🔴 按**显示宽度**切片（不是字符下标）：数据行里有中文 ⇒ 汉字占 2 列，
/// 按字符下标切会把后面几列整体左移（winget 实测：版本列里出现 Id 的字母）。
/// </para>
/// </summary>
internal static class FixedWidthTable
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    /// <summary>去掉 ANSI 转义、统一换行、去掉退格符 —— 后续一律按 <c>\n</c> 切行。</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = Ansi.Replace(raw, "");
        s = s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\b", "");
        return s;
    }

    /// <summary>整行只有短横线与空白（表格分隔线）。</summary>
    public static bool IsSeparator(string line)
    {
        var t = line.Trim();
        return t.Length >= 3 && t.All(ch => ch == '-' || ch == ' ' || ch == '\u2500');
    }

    /// <summary>
    /// 进度条 / 转轮 / 装饰行（被写在同一个 <c>\r</c> 行上，换行后会变成独立行）。
    /// </summary>
    public static bool IsNoiseLine(string line)
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

    // ==================== 显示宽度（🔴 CJK 全角 = 2 列） ====================

    /// <summary>东亚宽 / 全角字符的码点区间（造表格时按 2 列计）。</summary>
    private static readonly (int Lo, int Hi)[] WideRanges =
    {
        (0x1100, 0x115F), (0x2E80, 0x303E), (0x3041, 0x33FF), (0x3400, 0x4DBF),
        (0x4E00, 0x9FFF), (0xA000, 0xA4CF), (0xAC00, 0xD7A3), (0xF900, 0xFAFF),
        (0xFE10, 0xFE19), (0xFE30, 0xFE6F), (0xFF00, 0xFF60), (0xFFE0, 0xFFE6),
        (0x20000, 0x2FFFD), (0x30000, 0x3FFFD),
    };

    /// <summary>字符串占多少**显示列**（全角 2、其余 1；代理对按码点算一次）。</summary>
    public static int DisplayWidth(string s)
    {
        var w = 0;
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];                     // 🔴 必须是 int：代理对下面要塞码点（char 装不下）
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            w += IsWide(cp) ? 2 : 1;
        }
        return w;
    }

    private static bool IsWide(int cp)
    {
        foreach (var (lo, hi) in WideRanges)
            if (cp >= lo && cp <= hi) return true;
        return false;
    }

    /// <summary>显示列 <paramref name="width"/> 处对应的字符下标（超出则返回行尾）。</summary>
    private static int IndexOfWidth(string s, int width)
    {
        var w = 0;
        var i = 0;
        while (i < s.Length && w < width)
        {
            var step = 1;
            var cp = (int)s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                step = 2;
            }
            w += IsWide(cp) ? 2 : 1;
            i += step;
        }
        return i;
    }

    /// <summary>按显示列区间取子串（<paramref name="endW"/> 给 <c>int.MaxValue</c> = 取到行尾）。</summary>
    public static string SliceByWidth(string s, int startW, int endW)
    {
        var a = IndexOfWidth(s, startW);
        var b = endW == int.MaxValue ? s.Length : IndexOfWidth(s, endW);
        return b <= a ? "" : s.Substring(a, b - a);
    }

    // ==================== 表头 / 行切片 ====================

    /// <summary>
    /// 由「别名 → 规范名」字典构造表头匹配正则。
    /// 🔴 别名按长度倒序拼装：正则的交替是「先匹配先赢」，短别名放前面会截断长别名。
    /// </summary>
    public static Regex BuildAliasRegex(IReadOnlyDictionary<string, string> aliasToCanonical)
    {
        var aliases = aliasToCanonical.Keys
            .OrderByDescending(a => a.Length)
            .Select(Regex.Escape);
        return new Regex(string.Join("|", aliases), RegexOptions.Compiled);
    }

    /// <summary>
    /// 把表头行解析成「规范名 + 起始**显示列**」，按出现顺序排列。
    /// 🔴 存的是显示列而不是字符下标：表头是 ASCII，但数据行里有中文 ⇒ 两者不对齐。
    /// </summary>
    public static List<(string Label, int StartW)> FindColumns(
        string line, Regex aliasRegex, IReadOnlyDictionary<string, string> aliasToCanonical)
    {
        return aliasRegex.Matches(line)
            .Select(m => (Label: aliasToCanonical.TryGetValue(m.Value, out var c) ? c : null, StartW: DisplayWidth(line[..m.Index])))
            .Where(c => c.Label != null)
            .Select(c => (Label: c.Label!, c.StartW))
            .GroupBy(c => c.Label)
            .Select(g => g.First())                 // 同一规范名只取首次出现
            .OrderBy(c => c.StartW)
            .ToList();
    }

    /// <summary>
    /// 按列布局切一行 ⇒ 「规范名 → 单元格文本」（含**溢出回填**）。
    /// </summary>
    public static Dictionary<string, string> SliceRow(string line, List<(string Label, int StartW)> columns)
    {
        var cells = new string[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            var startW = columns[c].StartW;
            var endW = c + 1 < columns.Count ? columns[c + 1].StartW : int.MaxValue;
            cells[c] = startW >= DisplayWidth(line) ? "" : SliceByWidth(line, startW, endW);
        }

        ReflowOverflow(cells, columns);

        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var c = 0; c < columns.Count; c++) row[columns[c].Label] = cells[c].Trim();
        return row;
    }

    /// <summary>
    /// 单元格**溢出回填**：列宽是固定的，内容超长时后端不会截断，而是**顶满后挤进下一列**
    /// （winget 实测：Id 比 Id 列宽 1 ⇒ 版本列变成 <c>e 8000.836.2153.0</c>）。
    /// <para>
    /// 判据（四条同时成立才搬）：① 本列内容顶满列宽；② 本列末尾不是空白；③ 下一列开头不是空白（没被空格分隔）；
    /// ④ 下一列里还有空格 ⇒ 把**第一个空格之前的那段**还给本列（剩下的才是本列真正的值）。
    /// </para>
    /// </summary>
    private static void ReflowOverflow(string[] cells, List<(string Label, int StartW)> columns)
    {
        for (var c = 0; c + 1 < cells.Length; c++)
        {
            var width = columns[c + 1].StartW - columns[c].StartW;
            var cur = cells[c];
            var next = cells[c + 1];
            if (width <= 0 || cur.Length == 0 || next.Length == 0) continue;
            if (char.IsWhiteSpace(cur[^1]) || char.IsWhiteSpace(next[0])) continue;
            if (DisplayWidth(cur.Trim()) < width) continue;

            var sp = next.IndexOf(' ');
            if (sp <= 0 || sp == next.Length - 1) continue;

            cells[c] = cur + next[..sp];
            cells[c + 1] = next[sp..];
        }
    }

    /// <summary>
    /// **通用**固定列宽表格解析（适合不折行的后端：flatpak / snap …）。
    /// <para>
    /// 表头定位：行内出现 <paramref name="aliasToCanonical"/> 的别名、且覆盖全部
    /// <paramref name="requiredCanonical"/> 规范名，才算表头行。首列为空的行丢弃（残行）。
    /// </para>
    /// <para>⚠️ 会**自动换行折叠长名称**的后端（winget）⛔ 不用本方法 —— 那种续行合并是
    /// winget 固有启发式，见 <c>PackageOutputParser.ParseTable</c>。</para>
    /// </summary>
    public static List<Dictionary<string, string>> Parse(
        string? raw, IReadOnlyDictionary<string, string> aliasToCanonical, params string[] requiredCanonical)
    {
        var rows = new List<Dictionary<string, string>>();
        var regex = BuildAliasRegex(aliasToCanonical);
        var lines = Clean(raw).Split('\n');
        if (lines.Length == 0) return rows;

        var headerIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            if (IsNoiseLine(line)) continue;
            var labels = FindColumns(line, regex, aliasToCanonical).Select(c => c.Label).ToHashSet(StringComparer.Ordinal);
            if (requiredCanonical.All(r => labels.Contains(r)))
            {
                headerIndex = i;
                break;
            }
        }
        if (headerIndex < 0) return rows;

        var columns = FindColumns(lines[headerIndex], regex, aliasToCanonical);
        if (columns.Count == 0) return rows;
        var first = columns[0].Label;

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;
            if (IsNoiseLine(line)) continue;
            if (IsSeparator(line)) continue;

            var row = SliceRow(line, columns);
            if (!row.TryGetValue(first, out var head) || head.Length == 0) continue;   // 残行
            rows.Add(row);
        }
        return rows;
    }
}
