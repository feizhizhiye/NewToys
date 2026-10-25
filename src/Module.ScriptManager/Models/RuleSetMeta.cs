using System;
using System.Collections.Generic;
using System.Linq;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 规则目录里的一行。**不含规则内容** —— 内容在 <c>.py</c> 文件里，
/// 这里只放列表要展示的元信息 + 启用状态 / 最近运行（这两项落 `web_docs` 的 `rule-meta` 文档）。
/// <para>
/// 🔴 类名刻意<b>不</b>叫 <c>ScrapeTask</c>（旧模型已删）—— 见 命名表，避免与旧实现混淆。
/// </para>
/// <para>
/// 文件头（<c># key: value</c>）扩充为完整脚本头配置 —— 分类 / 说明 / 角色 /
/// 适用站点 / 产物形态 / UA 池 / 请求头 / 并发 / 限速 / 超时 / 上限。C# 侧**只读这些键**，
/// ⛔ 不解析 Python 语法。
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

    // ==================== 脚本头配置（# key: value，只读展示） ====================

    /// <summary>说明（<c># desc:</c>）—— 目录页「说明」列 + 详情卡。</summary>
    public string Desc { get; set; } = "";

    /// <summary>
    /// 分类（<c># category:</c>）—— 先五档 <c>book / image / video / doc / generic</c>，
    /// **未知值原样保存**（⛔ 不报错、⛔ 不归并）。只做展示分组与筛选，⛔ 不驱动 UI 行为。
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// 脚本角色（<c># role:</c>）：<c>entry / pick / toc / content / pipeline</c>；
    /// **缺省（空）按 <c>pipeline</c>**（整站脚本）。调试集合的槽位下拉按它过滤候选。
    /// </summary>
    public string Role { get; set; } = "";

    /// <summary>适用站点（<c># works:</c>）—— 详情卡显示。</summary>
    public string Works { get; set; } = "";

    /// <summary>
    /// 产物形态（<c># artifact:</c>）：<c>files / text / none</c>；空 = 未声明（按分类缺省猜测）。
    /// </summary>
    public string Artifact { get; set; } = "";

    /// <summary>UA 池（<c># ua:</c> 多行，按出现顺序）；空 = 用设置页缺省 UA。</summary>
    public IReadOnlyList<string> UaPool { get; set; } = Array.Empty<string>();

    /// <summary>额外请求头（<c># header:</c> 多行，原样透传）。</summary>
    public IReadOnlyList<string> Headers { get; set; } = Array.Empty<string>();

    /// <summary>并发（<c># concurrency:</c>；脏值回落 1）。</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>请求间隔毫秒（<c># delay-ms:</c>；脏值回落 0）。</summary>
    public int DelayMs { get; set; }

    /// <summary>单次请求超时**秒**（<c># timeout:</c>；0 / 脏值回落 = 用设置页缺省超时）。</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>分页上限（<c># max-pages:</c>；0 = 不限制；脏值回落 0）。</summary>
    public int MaxPages { get; set; }

    /// <summary>条目上限（<c># max-items:</c>；0 = 不限制；脏值回落 0）。</summary>
    public int MaxItems { get; set; }

    /// <summary>
    /// 脚本头里的**脏值提示**（如 <c># timeout:</c> 写了 <c>abc</c>）—— 已按缺省值回落，
    /// 由目录页汇总到状态栏（⚠ 前缀），⛔ 不打断加载。
    /// </summary>
    public List<string> HeaderWarnings { get; } = new();

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

    /// <summary>说明列 / 详情卡文本（空 = 「—」）。</summary>
    public string DescText => Desc.Length == 0 ? "—" : Desc;

    /// <summary>
    /// 分类展示：五档已知值给中文标签，**未知值原样**，空 = 「—」。
    /// </summary>
    public string CategoryText => Category.Length == 0 ? "—" : CategoryLabel(Category);

    /// <summary>角色展示（缺省按 <c>pipeline</c>）。</summary>
    public string RoleText => Role.Length == 0 ? "pipeline" : Role;

    /// <summary>适用站点文本（空 = 「—」）。</summary>
    public string WorksText => Works.Length == 0 ? "—" : Works;

    /// <summary>五档已知分类的中文标签（<see cref="CategoryText"/> 与工具条下拉共用）。</summary>
    public static string CategoryLabel(string category) => category.ToLowerInvariant() switch
    {
        "book" => "图书",
        "image" => "图片",
        "video" => "视频",
        "doc" => "文档",
        "generic" => "通用",
        _ => category
    };

    /// <summary>
    /// UA 池轮换起点：池非空 = 第一个（运行侧按请求轮换）；空 = 用设置页缺省 UA。
    /// </summary>
    public string? FirstUa => UaPool.Count > 0 ? UaPool[0] : null;
}
