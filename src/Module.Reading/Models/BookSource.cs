using System;
using System.Text.Json.Serialization;

namespace NewToys.Reading.Models;

/// <summary>
/// 书源的静态能力档（导入时判定，⛔ 不发请求）。
/// <para>顺序即「能用程度」的降序，界面与筛选都按它排序。</para>
/// </summary>
public enum SourceCapability
{
    /// <summary>三段规则齐全且桌面端能跑（含 <c>@js:</c> 片段）。</summary>
    Ready = 0,

    /// <summary>依赖 WebView / 唤起浏览器 —— 桌面端没有对应能力。</summary>
    NeedsWebView = 1,

    /// <summary>关键规则缺失（搜索地址 / 书单 / 目录 / 正文 任一为空）。</summary>
    Incomplete = 2,

    /// <summary>非文本源（图片 / 音频 / 视频）—— 本软件只做文本，这类源不参与搜索与发现。</summary>
    Unsupported = 3,
}

/// <summary>
/// 书源校验档（跑一次真实搜索后的结论，落库，用于筛「失效源」批量清理）。
/// <para>与静态的 <see cref="SourceCapability"/> 是两回事：那个只看规则字段，这个看真实请求结果。</para>
/// </summary>
public enum SourceCheckState
{
    /// <summary>没校验过（新导入 / 重新导入后保持原值）。</summary>
    Unchecked = 0,

    /// <summary>搜到了书。</summary>
    Passed = 1,

    /// <summary>请求通了但一条都没搜到（站点改版 / 关键词不合 / 需要登录）。</summary>
    NoResult = 2,

    /// <summary>请求失败（超时 / 404 / 规则抛错 / 需 WebView）。</summary>
    Failed = 3,

    /// <summary>跳过（没有搜索接口，只能「发现」浏览）—— ⛔ 不算失效，⛔ 不能被「删除失效」带走。</summary>
    Skipped = 4,
}

/// <summary>legado 书源（一条 = 一个站点）。（WHY: RD-002）</summary>
public sealed class BookSource
{
    [JsonPropertyName("bookSourceUrl")]
    public string Url { get; set; } = "";

    [JsonPropertyName("bookSourceName")]
    public string Name { get; set; } = "";

    [JsonPropertyName("bookSourceGroup")]
    public string? Group { get; set; }

    /// <summary>0 文本 / 1 图片 / 2 音频 / 3 视频。</summary>
    [JsonPropertyName("bookSourceType")]
    public int Type { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("searchUrl")]
    public string? SearchUrl { get; set; }

    /// <summary>发现页分类表：<c>分类名::地址&amp;&amp;分类名2::地址2</c>（可能含 <c>{{page}}</c>）。</summary>
    [JsonPropertyName("exploreUrl")]
    public string? ExploreUrl { get; set; }

    [JsonPropertyName("ruleSearch")]
    public SearchRule? Search { get; set; }

    /// <summary>发现页的列表规则（字段与搜索相同）。</summary>
    [JsonPropertyName("ruleExplore")]
    public SearchRule? Explore { get; set; }

    [JsonPropertyName("ruleBookInfo")]
    public BookInfoRule? Info { get; set; }

    [JsonPropertyName("ruleToc")]
    public TocRule? Toc { get; set; }

    [JsonPropertyName("ruleContent")]
    public ContentRule? Content { get; set; }

    /// <summary>导入时的原始 JSON 对象（不落 <c>rules_json</c> 之外的列）。</summary>
    [JsonIgnore]
    public string RawJson { get; set; } = "";

    /// <summary>上次使用的 UTC 时间（列表 / 详情卡显示）。</summary>
    [JsonIgnore]
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>静态判定结果（导入 / 读取时算一次，⛔ 不落库）。</summary>
    [JsonIgnore]
    public SourceCapability Capability { get; set; }

    /// <summary>校验结论（跑过搜索才有值，落库）。</summary>
    [JsonIgnore]
    public SourceCheckState CheckState { get; set; }

    /// <summary>校验说明（命中几条 / 失败原因）。</summary>
    [JsonIgnore]
    public string CheckNote { get; set; } = "";

    /// <summary>上次校验的 UTC 时间。</summary>
    [JsonIgnore]
    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>判定为「需 WebView / 不完整」或「含 JS」时的具体说明（界面提示用）。</summary>
    [JsonIgnore]
    public string CapabilityReason { get; set; } = "";

    /// <summary>类型中文名（列表列用）。</summary>
    [JsonIgnore]
    public string TypeText => Type switch
    {
        1 => "图片",
        2 => "音频",
        3 => "视频",
        _ => "文本",
    };

    /// <summary>状态文字（列表列 / 详情卡用）。</summary>
    [JsonIgnore]
    public string CapabilityText => Capability switch
    {
        SourceCapability.Ready => "可用",
        SourceCapability.NeedsWebView => "需 WebView",
        SourceCapability.Unsupported => "非文本源",
        _ => "不完整",
    };

    /// <summary>分组（空 ⇒ <c>未分组</c>，列表与筛选都按它归一）。</summary>
    [JsonIgnore]
    public string GroupText => string.IsNullOrWhiteSpace(Group) ? "未分组" : Group!.Trim();

    /// <summary>校验状态文字（列表列 / 筛选 / 详情卡）。</summary>
    [JsonIgnore]
    public string CheckText => CheckState switch
    {
        SourceCheckState.Passed => "通过",
        SourceCheckState.NoResult => "无结果",
        SourceCheckState.Failed => "失败",
        SourceCheckState.Skipped => "跳过",
        _ => "未校验",
    };

    /// <summary>是否已失效（校验失败或无结果 —— 「删除失效」按它取）。</summary>
    [JsonIgnore]
    public bool IsDead => CheckState is SourceCheckState.Failed or SourceCheckState.NoResult;

    /// <summary>上次校验时间（没校验过 ⇒ 空串）。</summary>
    [JsonIgnore]
    public string CheckedText => CheckedAt is null ? "" : SourceTimeText.Of(CheckedAt);
}

/// <summary>搜索规则（<c>ruleSearch</c>）。</summary>
public sealed class SearchRule
{
    [JsonPropertyName("bookList")]
    public string? BookList { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("lastChapter")]
    public string? LastChapter { get; set; }

    [JsonPropertyName("updateTime")]
    public string? UpdateTime { get; set; }

    [JsonPropertyName("bookUrl")]
    public string? BookUrl { get; set; }

    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("intro")]
    public string? Intro { get; set; }

    [JsonPropertyName("wordCount")]
    public string? WordCount { get; set; }
}

/// <summary>详情规则（<c>ruleBookInfo</c>）。</summary>
public sealed class BookInfoRule
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("intro")]
    public string? Intro { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("lastChapter")]
    public string? LastChapter { get; set; }

    [JsonPropertyName("tocUrl")]
    public string? TocUrl { get; set; }
}

/// <summary>目录规则（<c>ruleToc</c>）。</summary>
public sealed class TocRule
{
    [JsonPropertyName("chapterList")]
    public string? ChapterList { get; set; }

    [JsonPropertyName("chapterName")]
    public string? ChapterName { get; set; }

    [JsonPropertyName("chapterUrl")]
    public string? ChapterUrl { get; set; }

    /// <summary>目录翻页（空 ⇒ 单页目录）。</summary>
    [JsonPropertyName("nextTocUrl")]
    public string? NextTocUrl { get; set; }
}

/// <summary>正文规则（<c>ruleContent</c>）。</summary>
public sealed class ContentRule
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>正文续页链接（多页拼接）。</summary>
    [JsonPropertyName("nextContentUrl")]
    public string? NextContentUrl { get; set; }

    /// <summary>按行书写的正则替换（<c>正则##替换</c>）。</summary>
    [JsonPropertyName("replaceRule")]
    public string? ReplaceRule { get; set; }
}
