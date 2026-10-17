using System;
using System.Collections.Generic;
using System.Text.Json;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>脚本输出解析出来的一行（还没挂列标题 / 还没合并进列表）。</summary>
public sealed class SysInfoParsedRow
{
    /// <summary>行键（同 <c>ScriptId</c> 内唯一）。</summary>
    public string RowKey { get; init; } = "";

    /// <summary>行状态（<see cref="SysInfoStatus"/>）。</summary>
    public string Status { get; init; } = SysInfoStatus.Ok;

    /// <summary>按列位对齐的值（已补齐 / 已截断到列数）。</summary>
    public string[] Values { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 脚本输出 → 行数组的**宽松解析器**（NT-067 §三.2）。
/// <para>三种形态都认，命中即停：</para>
/// <list type="number">
///   <item><c>{ "rows": [ { "key": "...", "values": [...] } ] }</c> ← 推荐（随包脚本都用它）；</item>
///   <item><c>[ { "key": "...", "values": [...] } ]</c>（顶层数组 + 对象行）；</item>
///   <item><c>[ ["CPU","处理器","i7"] ]</c>（裸二维数组，无 key）。</item>
/// </list>
/// <para>
/// 宽松规则（硬）：非数组 / 非对象的行**丢弃**；<c>values</c> 不足补 <c>""</c>；超出列数的项**忽略**；
/// 顶层解析不了 ⇒ 整个脚本这批失败（不产生任何行）。⛔ 脚本里 ⛔ 不许往 stdout 写日志（引擎没有"注释行"概念）。
/// </para>
/// </summary>
public static class SysInfoOutputParser
{
    /// <summary>
    /// 解析一次 stdout。<paramref name="columnCount"/> = manifest 的列数（值的补齐 / 截断基准）。
    /// </summary>
    /// <returns>false ⇒ 顶层 JSON 不合法 / 形态不认识（<paramref name="error"/> 给人话）。</returns>
    public static bool TryParse(string? stdout, int columnCount, out List<SysInfoParsedRow> rows, out string? error)
    {
        rows = new List<SysInfoParsedRow>();
        error = null;

        var text = (stdout ?? "").Trim();
        if (text.Length == 0) { error = "没有任何输出"; return false; }

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
                var parsed = ParseRow(item, index, columnCount);
                if (parsed != null) rows.Add(parsed);
            }

            return true;
        }
    }

    /// <summary>解析一行；不认识的形态返回 null（丢弃）。</summary>
    private static SysInfoParsedRow? ParseRow(JsonElement item, int index, int columnCount)
    {
        string? key = null;
        var status = SysInfoStatus.Ok;
        JsonElement values;

        if (item.ValueKind == JsonValueKind.Array)
        {
            values = item;   // 裸二维数组：整行就是 values
        }
        else if (item.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetProperty(item, out values, "values") || values.ValueKind != JsonValueKind.Array)
                return null;   // 对象行但没 values ⇒ 丢弃（⛔ 不猜字段）

            if (TryGetProperty(item, out var keyEl, "key") && keyEl.ValueKind == JsonValueKind.String)
                key = keyEl.GetString();

            if (TryGetProperty(item, out var st, "status") && st.ValueKind == JsonValueKind.String)
                status = NormalizeStatus(st.GetString());
        }
        else
        {
            return null;   // 非数组 / 非对象 ⇒ 丢弃
        }

        var list = new List<string>(columnCount);
        foreach (var v in values.EnumerateArray())
        {
            if (list.Count >= columnCount) break;   // 超出列数 ⇒ 忽略
            list.Add(ValueText(v));
        }
        while (list.Count < columnCount) list.Add("");   // 不足 ⇒ 补空

        // RowKey：key 字段 → 第 1 列文本 → 行序号
        var rowKey = !string.IsNullOrWhiteSpace(key) ? key!.Trim() : "";
        if (rowKey.Length == 0 && list.Count > 0 && !string.IsNullOrWhiteSpace(list[0])) rowKey = list[0].Trim();
        if (rowKey.Length == 0) rowKey = "#" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new SysInfoParsedRow { RowKey = rowKey, Status = status, Values = list.ToArray() };
    }

    /// <summary>属性名大小写不敏感（脚本作者常写成 <c>Rows</c> / <c>Values</c>）。</summary>
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

    /// <summary>JSON 值 → 文本（数字原样、布尔小写、null 空串）。</summary>
    private static string ValueText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => v.GetRawText(),
    };

    /// <summary>只认 ok / warn / error，其他一律按 ok。</summary>
    private static string NormalizeStatus(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        SysInfoStatus.Warn => SysInfoStatus.Warn,
        SysInfoStatus.Error => SysInfoStatus.Error,
        _ => SysInfoStatus.Ok,
    };
}
