using System;
using System.Collections.Generic;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 流水线（flow）配置（NT-086 批 C）—— 五段槽位各挂一个脚本名，落
/// <c>&lt;DataDir&gt;\web-tools\flows\&lt;名&gt;.json</c>（⛔ 不开 DB 表）。
/// <para>
/// 🔴 <b>段位</b>：<c>probe</c>（内置）→ <c>entry</c> → <c>pick</c> → <c>toc</c> → <c>content</c> → <c>save</c>（内置）。
/// 只有 <c>entry / pick / toc / content</c> 需要用户选脚本：<c>pick</c> 默认人工点选、
/// <c>toc</c> 留空 = <b>链式模式</b>（<c>content</c> + next 逐页跟进，目录边跑边累积）。
/// </para>
/// <para>
/// 🔴 产物形态优先级链：<c>content</c> 返回 ＞ <c># artifact:</c> ＞ 分类缺省猜测 ＞ none。
/// 分类只做展示分组，⛔ 不驱动 UI 行为。
/// </para>
/// </summary>
public class FlowConfig
{
    /// <summary>流水线名（= 文件名去掉 <c>.json</c>）。</summary>
    public string Name { get; set; } = "";

    /// <summary>入口段脚本（输出 rows，<b>必须含 url</b>）。</summary>
    public string Entry { get; set; } = "";

    /// <summary>条目筛选 / 详情段脚本（可选；本期默认人工点选）。</summary>
    public string Pick { get; set; } = "";

    /// <summary>目录段脚本（可空 = 链式模式）。</summary>
    public string Toc { get; set; } = "";

    /// <summary>正文 / 资源段脚本（终段，出 artifact）。</summary>
    public string Content { get; set; } = "";

    /// <summary>起始网址模板（空 = 用 entry 脚本文件头的 <c># base:</c>）。</summary>
    public string UrlTemplate { get; set; } = "";

    /// <summary>备注（写给用户自己看的：站点 / 版式 / 注意事项）。</summary>
    public string Note { get; set; } = "";

    /// <summary>最近保存时间（本地时间）。</summary>
    public DateTime UpdatedAt { get; set; }

    // ==================== 段位元数据（UI 与候选过滤共用） ====================

    /// <summary>需要用户选脚本的段位（顺序即流水线顺序；<c>probe</c> / <c>save</c> 是内置的，不在其中）。</summary>
    public static readonly string[] StageKeys = { "entry", "pick", "toc", "content" };

    /// <summary>段位中文标签（与 <see cref="StageKeys"/> 一一对应）。</summary>
    public static readonly string[] StageLabels = { "入口 / 搜索", "条目筛选", "目录", "正文 / 资源" };

    /// <summary>段位说明（工具提示用；与 <see cref="StageKeys"/> 一一对应）。</summary>
    public static readonly string[] StageHints =
    {
        "搜到一批条目 → rows（必须含 url，urljoin 的 base = 当前页 URL）",
        "点选其中一条 / 取详情字段（可选，本期默认人工点选）",
        "取章节目录 → rows（留空 = 链式模式，靠 content 的 next 逐页跟进）",
        "取正文或资源链接 → text / files（终段）"
    };

    /// <summary>
    /// 段位 → 配置文件里的属性名（保存 / 载入时按名读写，⛔ 不反射整类）。
    /// </summary>
    public static string SlotProperty(string stageKey) => stageKey switch
    {
        "entry" => nameof(Entry),
        "pick" => nameof(Pick),
        "toc" => nameof(Toc),
        "content" => nameof(Content),
        _ => ""
    };

    /// <summary>读某个段位当前挂的脚本名（空 = 留空 / 跳过）。</summary>
    public string Slot(string stageKey) => stageKey switch
    {
        "entry" => Entry,
        "pick" => Pick,
        "toc" => Toc,
        "content" => Content,
        _ => ""
    };

    /// <summary>写某个段位的脚本名（<paramref name="stageKey"/> 未知则忽略）。</summary>
    public void SetSlot(string stageKey, string scriptName)
    {
        switch (stageKey)
        {
            case "entry": Entry = scriptName; break;
            case "pick": Pick = scriptName; break;
            case "toc": Toc = scriptName; break;
            case "content": Content = scriptName; break;
        }
    }

    /// <summary>已填的段位数（<c>content</c> 是否填了决定「能不能出产物」）。</summary>
    public int FilledCount
    {
        get
        {
            var n = 0;
            foreach (var k in StageKeys) if (Slot(k).Length > 0) n++;
            return n;
        }
    }

    /// <summary>摘要（状态栏用）：「已配 3/4 段 · 更新于 …」。</summary>
    public string Summary => $"已配 {FilledCount}/{StageKeys.Length} 段 · 更新于 "
                           + (UpdatedAt == default ? "—" : UpdatedAt.ToString("yyyy-MM-dd HH:mm"));
}
