using System.Collections.Generic;
using System.Linq;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 试跑推荐的一条结果：候选脚本对<b>同一份 HTML</b> 求值后的
/// 条数 / 字段完整度 / 错误。
/// <para>
/// 🔴 排序判据：<b>条数 &gt; 0</b> 优先，其次按「必填字段非空率」降序（同一份 HTML ⇒ 可比）。
/// 条数取 <c>url</c> 字段的命中数（段间契约要求入口 / 中间段输出含 url），没有 url 时取最大字段命中数。
/// </para>
/// </summary>
public class CandidateResult
{
    /// <summary>脚本名（= 文件名去 <c>.py</c>）。</summary>
    public string Name { get; set; } = "";

    /// <summary>条数（<c>url</c> 字段命中数；无 url 时取最大命中数）。</summary>
    public int Count { get; set; }

    /// <summary>字段非空率 0~1（各字段 <c>min(count, Count)/Count</c> 的平均）。</summary>
    public double Fill { get; set; }

    /// <summary>Python 侧错误（加载 / 求值异常）—— 🔴 必须回显，⛔ 不静默显示 0 条。</summary>
    public string Error { get; set; } = "";

    /// <summary>分节命中数：<c>[(字段名, 命中数, 错误)]</c>。</summary>
    public List<(string Key, int Count, string Error)> Fields { get; } = new();

    public bool HasError => Error.Length > 0;

    public string CountText => Count > 0 ? Count + " 条" : "0 条";

    public string FillText => (Fill * 100).ToString("0") + "%";

    /// <summary>分节命中数文本（「url 12 · title 12 · cover 0」）。</summary>
    public string FieldsText => Fields.Count == 0
        ? "—"
        : string.Join(" · ", Fields.Select(f => $"{f.Key} {f.Count}{(f.Error.Length > 0 ? " ⚠" : "")}"));

    /// <summary>排序：有错垫底 → 条数 &gt; 0 优先 → 完整度降序 → 名字。</summary>
    internal static int Compare(CandidateResult a, CandidateResult b)
    {
        if (a.HasError != b.HasError) return a.HasError ? 1 : -1;
        if ((a.Count > 0) != (b.Count > 0)) return a.Count > 0 ? -1 : 1;
        if (a.Count != b.Count) return b.Count.CompareTo(a.Count);
        var d = b.Fill.CompareTo(a.Fill);
        return d != 0 ? d : string.CompareOrdinal(a.Name, b.Name);
    }
}
