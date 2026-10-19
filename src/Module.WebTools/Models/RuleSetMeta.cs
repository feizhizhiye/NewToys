using System;

namespace NewToys.WebTools.Models;

/// <summary>
/// 规则目录里的一行（NT-084）。**不含规则内容** —— 内容在 <c>.py</c> 文件里，
/// 这里只放列表要展示的元信息 + 启用状态 / 最近运行（这两项落 `web_docs` 的 `rule-meta` 文档）。
/// <para>
/// 🔴 类名刻意<b>不</b>叫 <c>ScrapeTask</c>（旧模型已删）—— 见 NT-084 命名表，避免与旧实现混淆。
/// </para>
/// </summary>
public class RuleSetMeta
{
    /// <summary>规则集名（= 文件名去掉 <c>.py</c>，同时是目录里的唯一键）。</summary>
    public string Name { get; set; } = "";

    /// <summary>文件绝对路径。</summary>
    public string FilePath { get; set; } = "";

    /// <summary>站点根地址（来自文件头 <c># base:</c>；空 = 未填）。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>页面编码：<c>utf8</c> / <c>gbk</c>（来自文件头 <c># encoding:</c>）。</summary>
    public string Encoding { get; set; } = "utf8";

    /// <summary>是否参与运行（目录里的启用勾选）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>最近一次运行时间（本地时间）。</summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>最近一次运行的命中条数 / 输出行数。</summary>
    public int LastCount { get; set; }

    /// <summary>文件字节数。</summary>
    public long SizeBytes { get; set; }

    /// <summary>文件最后修改时间。</summary>
    public DateTime? ModifiedAt { get; set; }

    // ==================== 列表 / 详情卡直接绑的展示串（描述符不支持格式化） ====================

    /// <summary>启用列文本。</summary>
    public string EnabledText => Enabled ? "✓ 启用" : "— 停用";

    /// <summary>最近运行列文本。</summary>
    public string LastRunText => LastRunAt is { } t && t != default ? t.ToString("yyyy-MM-dd HH:mm") : "—";

    /// <summary>大小展示（B / KB）。</summary>
    public string SizeText => SizeBytes < 1024 ? SizeBytes + " B" : (SizeBytes / 1024.0).ToString("0.0") + " KB";

    /// <summary>详情卡里的「最近运行」摘要。</summary>
    public string LastRunSummary => LastRunAt is null ? "还没运行过" : $"最近运行：{LastCount} 条 · {LastRunText}";

    /// <summary>详情卡里的「文件」摘要。</summary>
    public string FileSummary => SizeText + " · 修改于 " + (ModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—");
}
