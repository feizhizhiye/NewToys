using System;
using System.Collections.Generic;
using System.Text.Json;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>脚本 / 命令输出解析出来的一行（属性名 → 值，保持出现顺序）。</summary>
public sealed class ParsedInfoRow
{
    /// <summary>行键（同一采集器内唯一）。</summary>
    public string RowKey { get; init; } = "";

    /// <summary>行状态（<see cref="SysInfoStatus"/>）。</summary>
    public string Status { get; init; } = SysInfoStatus.Ok;

    /// <summary>属性值（顺序 = 输出里出现顺序 → 列 schema 的生成依据）。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attrs { get; init; }
        = Array.Empty<KeyValuePair<string, string>>();
}

/// <summary>
/// 采集输出 → 行数组的宽松解析器（NT-068 §三.11 / §三.13）。
/// </summary>
/// <remarks>
/// <para>支持 4 种 <see cref="ScriptParseMode"/>：</para>
/// <list type="number">
///   <item><c>json</c>（默认）：<c>{ "rows": [ { "key": "...", "status": "ok", "attrs": { "项目": "...", "值": "..." } } ] }</c>；
///   兼容 NT-067 的旧形态 <c>values: [...]</c>（此时属性名退化成 <c>列1…列N</c>，⚠️ 建议改写成 <c>attrs</c>）；</item>
///   <item><c>line</c>：每行一个值 → 属性名固定 <see cref="LineAttr"/>；</item>
///   <item><c>csv</c>：首行 = 属性名，其后每行 = 一行数据（逗号分隔，支持双引号包裹）；</item>
///   <item><c>regex</c>：⚠️ 本期<b>未定义 pattern 字段</b>（卡面没定落点）⇒ 按 <c>line</c> 处理，等 pattern 落位后另立卡。</item>
/// </list>
/// <para>宽松规则：非数组 / 非对象的行丢弃；解析不了 ⇒ 整批失败（不产生任何行，不污染列 schema）。</para>
/// </remarks>
public static class SysInfoOutputParser
{
    /// <summary><c>line</c> 模式下的属性名（= 列 key）。</summary>
    public const string LineAttr = "值";

    /// <summary>旧 <c>values</c> 数组的属性名前缀（<c>列1</c> / <c>列2</c> …）。</summary>
    public const string LegacyAttrPrefix = "列";

    /// <summary>
    /// 解析一次输出。
    /// </summary>
    /// <param name="stdout">进程 stdout。</param>
    /// <param name="parseMode">取值见 <see cref="ScriptParseMode"/>（空 = json）。</param>
    /// <param name="rows">解析结果（失败时为空表）。</param>
    /// <param name="error">失败原因（人话）。</param>
    public static bool TryParse(string? stdout, string? parseMode, out List<ParsedInfoRow> rows, out string? error)
    {
        rows = new List<ParsedInfoRow>();
        error = null;

        var text = (stdout ?? "").Trim();
        if (text.Length == 0) { error = "没有任何输出"; return false; }

        return ScriptParseMode.Normalize(parseMode) switch
        {
            ScriptParseMode.Line => ParseLine(text, rows),
            // regex：本期按 line 走（见类注释）。
            ScriptParseMode.Regex => ParseLine(text, rows),
            ScriptParseMode.Csv => ParseCsv(text, rows, out error),
            _ => ParseJson(text, rows, out error),
        };
    }

    // ==================== json ====================

    private static bool ParseJson(string text, List<ParsedInfoRow> rows, out string? error)
    {
        error = null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch (JsonException ex) { error = "输出不是合法 JSON：" + ex.Message; return false; }

        using (doc)
        {
            var root = doc.RootElement;
            JsonElement array;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetProperty(root, out array, "rows") || array.ValueKind != JsonValueKind.Array)
                {
                    error = "顶层 JSON 里没有 rows 数组";
                    return false;
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else
            {
                error = "顶层 JSON 既不是对象也不是数组";
                return false;
            }

            var index = 0;
            foreach (var item in array.EnumerateArray())
            {
                index++;
                var parsed = ParseJsonRow(item, index);
                if (parsed != null) rows.Add(parsed);
            }

            if (rows.Count == 0) { error = "输出里没有可用的行"; return false; }
            return true;
        }
    }

    private static ParsedInfoRow? ParseJsonRow(JsonElement item, int index)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        string? key = null;
        var status = SysInfoStatus.Ok;
        var attrs = new List<KeyValuePair<string, string>>();

        if (TryGetProperty(item, out var attrsEl, "attrs") && attrsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in attrsEl.EnumerateObject())
                attrs.Add(new KeyValuePair<string, string>(p.Name, ValueText(p.Value)));
        }
        else if (TryGetProperty(item, out var valuesEl, "values") && valuesEl.ValueKind == JsonValueKind.Array)
        {
            // NT-067 旧形态：属性名退化成 列1 / 列2 …
            var n = 0;
            foreach (var v in valuesEl.EnumerateArray())
            {
                n++;
                attrs.Add(new KeyValuePair<string, string>(LegacyAttrPrefix + n, ValueText(v)));
            }
        }
        else
        {
            return null;   // 既没 attrs 也没 values ⇒ 丢弃（⛔ 不猜字段）
        }

        if (TryGetProperty(item, out var keyEl, "key") && keyEl.ValueKind == JsonValueKind.String)
            key = keyEl.GetString();

        if (TryGetProperty(item, out var st, "status") && st.ValueKind == JsonValueKind.String)
            status = SysInfoStatus.NormalizeRow(st.GetString());

        var rowKey = (key ?? "").Trim();
        if (rowKey.Length == 0 && attrs.Count > 0 && attrs[0].Value.Trim().Length > 0)
            rowKey = attrs[0].Value.Trim();
        if (rowKey.Length == 0) rowKey = "#" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new ParsedInfoRow { RowKey = rowKey, Status = status, Attrs = attrs };
    }

    // ==================== line ====================

    /// <summary>每行一个值：行键 = 行号，属性名 = <see cref="LineAttr"/>。</summary>
    private static bool ParseLine(string text, List<ParsedInfoRow> rows)
    {
        var n = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            n++;
            rows.Add(new ParsedInfoRow
            {
                RowKey = "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Attrs = new[] { new KeyValuePair<string, string>(LineAttr, line) },
            });
        }
        return rows.Count > 0;
    }

    // ==================== csv ====================

    /// <summary>首行 = 属性名，其余每行 = 一条记录（行键 = <c>#序号</c>）。</summary>
    private static bool ParseCsv(string text, List<ParsedInfoRow> rows, out string? error)
    {
        error = null;

        var lines = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length > 0) lines.Add(line);
        }
        if (lines.Count < 2) { error = "csv 至少要有标题行 + 一行数据"; return false; }

        var headers = SplitCsvLine(lines[0]);
        if (headers.Count == 0) { error = "csv 标题行为空"; return false; }

        for (var i = 1; i < lines.Count; i++)
        {
            var cells = SplitCsvLine(lines[i]);
            if (cells.Count == 0) continue;

            var attrs = new List<KeyValuePair<string, string>>(headers.Count);
            for (var c = 0; c < headers.Count; c++)
                attrs.Add(new KeyValuePair<string, string>(headers[c], c < cells.Count ? cells[c] : ""));

            rows.Add(new ParsedInfoRow
            {
                RowKey = "#" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Attrs = attrs,
            });
        }

        if (rows.Count == 0) { error = "csv 里没有数据行"; return false; }
        return true;
    }

    /// <summary>按逗号拆一格（支持 <c>"a,b"</c> 双引号包裹、<c>""</c> 转义）。</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = false;
                }
                else sb.Append(ch);
                continue;
            }

            if (ch == '"') { quoted = true; continue; }
            if (ch == ',') { list.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        list.Add(sb.ToString().Trim());
        return list;
    }

    // ==================== 基础设施 ====================

    /// <summary>属性名大小写不敏感（脚本作者常写成 <c>Rows</c> / <c>Values</c> / <c>Attrs</c>）。</summary>
    private static bool TryGetProperty(JsonElement obj, out JsonElement value, string name)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>JSON 值 → 文本（字符串原样、数字/布尔原文、null 空串）。</summary>
    private static string ValueText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => v.GetRawText(),
    };
}
