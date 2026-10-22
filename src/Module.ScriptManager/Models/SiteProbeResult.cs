using System;
using System.Collections.Generic;
using System.Linq;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 站点探测的**一个候选路径**的命中结果（NT-086 批 E）。
/// </summary>
public sealed class SiteProbeHit
{
    /// <summary>预设 id。</summary>
    public string PresetId { get; set; } = "";

    /// <summary>预设标题（状态栏展示用）。</summary>
    public string PresetTitle { get; set; } = "";

    /// <summary>探测路径（站点根相对，如 <c>/feed</c>）。</summary>
    public string Path { get; set; } = "";

    /// <summary>完整 URL。</summary>
    public string Url { get; set; } = "";

    /// <summary>用该预设解析出的条目数（0 = 没解析出东西）。</summary>
    public int Count { get; set; }

    /// <summary>抓取 / 解析阶段的错误（没有为空）。</summary>
    public string Error { get; set; } = "";

    /// <summary>🔴 「能解析出条目」才算命中（⛔ 不看 HTTP 200）。</summary>
    public bool Ok => Count > 0;
}

/// <summary>
/// 站点探测的汇总（NT-086 批 E）：每候选一命中 + 推荐。
/// <para>
/// 🔴 **只推荐不代选**：<see cref="Best"/> 只是建议，⛔ 不自动创建 / 不自动选中任何预设。
/// </para>
/// </summary>
public sealed class SiteProbeResult
{
    /// <summary>运行时错误（Python 不可用 / 网址无法识别等；空 = 探测正常完成）。</summary>
    public string Error { get; set; } = "";

    /// <summary>全部候选的命中（含没命中的；按预设表顺序 + 路径顺序）。</summary>
    public List<SiteProbeHit> Hits { get; } = new();

    /// <summary>命中的候选（按条数降序；平手保持预设表顺序 ⇒ 稳定）。</summary>
    public IEnumerable<SiteProbeHit> Matches => Hits.Where(h => h.Ok).OrderByDescending(h => h.Count);

    /// <summary>推荐（条数最高；全没命中 = null）。</summary>
    public SiteProbeHit? Best => Matches.FirstOrDefault();
}
