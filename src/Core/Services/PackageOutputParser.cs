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
/// ⚠️ <b>表头是本地化的</b>（v1.33 修正的认知）：中文 Windows 上 <c>winget list</c> 打印的是
/// <c>名称 / ID / 版本 / 可用</c>，<c>source list</c> 是 <c>名称 / 参数 / 显式</c>，
/// <c>pin list</c> 是 <c>… / 包钉类型</c>。旧版只认英文标签 → 找不到表头 → 一行都解析不出来
/// （表现：列表页**空白**）。现在所有标签都按「英文 + 中文别名」映射到**规范名**再切片。
/// </para>
/// <para>
/// 会被滤掉的行：① ANSI 颜色转义；② 进度条 / 转轮（含 <c>\r</c>、<c>\b</c> 与 <c>█ ▒ ░ \) | / -</c> 填充）；
/// ③ 分隔线（整行只有 <c>-</c>）；④ 表头之上的提示行（"已配置的源需要更新"之类）。
/// </para>
/// </summary>
internal static class PackageOutputParser
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    /// <summary>
    /// 表格列标签：**规范名 + 该列在输出里可能出现的写法**（英文 + 简体中文）。
    /// <para>
    /// 顺序无关（切列时按实际起始下标排序），但同一规范名只取首次命中；
    /// 别名里最长的要放在前面（如 <c>Last Updated</c> 先于任何短词），避免被短别名截断。
    /// </para>
    /// </summary>
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

    /// <summary>在表头行里定位「列标签」用的正则（长别名优先，避免短词先命中）。</summary>
    private static readonly Regex LabelRegex = BuildLabelRegex();

    /// <summary>
    /// 认识但**不装进模型**的 <c>winget show</c> 字段。
    /// <para>
    /// 这些行必须显式「终结上一个字段」，否则会被当成上一条多行值的续行 ——
    /// 例如 <c>发布服务器 URL: …</c> 会粘进「发布者」里（v1.33 前就是这个毛病）。
    /// </para>
    /// </summary>
    private static readonly HashSet<string> IgnoredLabels = new(StringComparer.Ordinal)
    {
        "发布服务器 URL", "发布服务器支持 URL", "作者", "绰号", "版权", "版权所有",
        "许可证 URL", "安装程序类型", "安装程序 URL", "安装程序 SHA256", "安装程序区域设置",
        "发布日期", "协议", "文档", "安装范围", "最低操作系统版本", "标签", "依赖项",
        "Copyright", "Copyright Url", "Author", "Installer Type", "Installer Url",
        "Installer SHA256", "Release Date", "License Url", "Agreements", "Documentations",
    };

    private static Regex BuildLabelRegex()
    {
        // 别名按长度倒序拼装：正则的交替是「先匹配先赢」，短别名放前面会截断长别名
        // （如 "Available" 被 "Av"… 之类误伤，或 "Last Updated" 被 "Last" 抢先）。
        var aliases = LabelDefs
            .SelectMany(d => d.Aliases)
            .OrderByDescending(a => a.Length)
            .Select(Regex.Escape);
        return new Regex(string.Join("|", aliases), RegexOptions.Compiled);
    }

    /// <summary>别名 → 规范名（表头行里所有出现的列标签）。</summary>
    private static string? ToCanonical(string alias)
    {
        foreach (var (canonical, list) in LabelDefs)
            if (list.Contains(alias, StringComparer.Ordinal)) return canonical;
        return null;
    }

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
    /// 判定"这一行是表头"所需的**规范列名**（全部命中才算）。
    /// 例：<c>list</c> / <c>search</c> 用 <c>Name+Id</c>；<c>source list</c> 用 <c>Name+Argument</c>。
    /// （规范名与输出语言无关，调用方不必关心中文还是英文表头。）
    /// </param>
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

        // ---- 3. 逐行切片 ----
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;
            if (IsNoiseLine(line)) continue;
            if (IsSeparator(line)) continue;        // ---- 分隔线

            // 🔴 按**显示宽度**切片（不是字符下标）：中文名一行里每个汉字占 2 列，
            //    按字符下标切会把后面几列整体左移 ⇒ 版本列里出现 Id / 名称的字母（"e 8000…" "wn"）。
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

            // 整行只剩第一个单元格（长名称换行那种残行）→ 丢弃
            if (!row.TryGetValue("Name", out var name) || name.Length == 0) continue;

            // 🔴 名称被**自动换行**时，Id 会跟着折到下一行 ⇒ 本行 Id 只剩尾巴
            //    （`Microsoft Edge` 折行后本行 Id = `e`，完整 Id `Microsoft.Edge` 在下一行）。
            //    这种残缺 Id 装不了 / 卸不了、也匹配不上已安装表。判据三条同时成立才算续行：
            //    ① 本行 Id 非空、是**最后有值的一列**且整行顶到行尾；② 下一行在 id 列位置有内容，版本列为空；
            //    ③ 两段合起来才像一条 Id —— 本行段不含 `.`、下一段含 `.`（= 被切断的那条），
            //       或本行段**不足 4 字符**（`e` / `edg`）且两段直接相连。⛔ 不做"任意两段拼接"。
            //    ⚠️ 判成续行时**保留本行的 Name**（不要用续行的空 Name 覆盖），只把 Id 拼全。
            //    ⚠️ 续行的 Version 列若也被切断（`1…` / `e15…`）就丢掉 —— 这种版本串没有意义。
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

    /// <summary>
    /// 单元格**溢出回填**：列宽是固定的，内容超长时后端不会截断，而是**顶满后挤进下一列**
    /// （例：Id <c>MSIX\…8wekyb3d8bbwe</c> 比 Id 列宽 1 ⇒ 版本列变成 <c>e 8000.836.2153.0</c>）。
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
    internal static int DisplayWidth(string s)
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
    private static string SliceByWidth(string s, int startW, int endW)
    {
        var a = IndexOfWidth(s, startW);
        var b = endW == int.MaxValue ? s.Length : IndexOfWidth(s, endW);
        return b <= a ? "" : s.Substring(a, b - a);
    }

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

    /// <summary>按列切片规则取某一行的某列文本（同样按**显示宽度**切）。</summary>
    private static bool TryGetColumnAt(string line, List<(string Label, int StartW)> columns, string label, out string value)
    {
        value = "";
        var idx = columns.FindIndex(c => c.Label == label);
        if (idx < 0) return false;

        var startW = columns[idx].StartW;
        var endW = idx + 1 < columns.Count ? columns[idx + 1].StartW : int.MaxValue;
        if (startW >= DisplayWidth(line)) return true;

        value = SliceByWidth(line, startW, endW).Trim();
        return true;
    }

    /// <summary>
    /// 把一行里的列标签解析成「规范名 + 起始**显示列**」，按出现顺序排列。
    /// <para>🔴 存的是显示列而不是字符下标：表头是 ASCII，但数据行里有中文 ⇒ 两者不对齐。</para>
    /// </summary>
    private static List<(string Label, int StartW)> FindLabels(string line)
    {
        return LabelRegex.Matches(line)
            .Select(m => (Label: ToCanonical(m.Value), StartW: DisplayWidth(line[..m.Index])))
            .Where(c => c.Label != null)
            .Select(c => (Label: c.Label!, c.StartW))
            .GroupBy(c => c.Label)
            .Select(g => g.First())                 // 同一规范名只取首次出现
            .OrderBy(c => c.StartW)
            .ToList();
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
