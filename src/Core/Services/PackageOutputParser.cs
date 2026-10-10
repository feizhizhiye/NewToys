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

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < columns.Count; c++)
            {
                var start = columns[c].Start;
                var end = c + 1 < columns.Count ? columns[c + 1].Start : line.Length;
                if (start >= line.Length) { row[columns[c].Label] = ""; continue; }
                if (end > line.Length) end = line.Length;
                row[columns[c].Label] = line.Substring(start, end - start).Trim();
            }

            // 整行只剩第一个单元格（长名称换行那种残行）→ 丢弃
            if (!row.TryGetValue("Name", out var name) || name.Length == 0) continue;
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>把一行里的列标签解析成「规范名 + 起始下标」，按出现顺序排列。</summary>
    private static List<(string Label, int Start)> FindLabels(string line)
    {
        return LabelRegex.Matches(line)
            .Select(m => (Label: ToCanonical(m.Value), Start: m.Index))
            .Where(c => c.Label != null)
            .Select(c => (Label: c.Label!, c.Start))
            .GroupBy(c => c.Label)
            .Select(g => g.First())                 // 同一规范名只取首次出现
            .OrderBy(c => c.Start)
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
