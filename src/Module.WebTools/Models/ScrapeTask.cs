using System;

namespace NewToys.WebTools.Models;

/// <summary>一个「网页抓取」配置（一条爬虫任务）。</summary>
public class ScrapeTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>任务名（列表里显示）。</summary>
    public string Name { get; set; } = "";
    /// <summary>起始网址。</summary>
    public string StartUrl { get; set; } = "";
    /// <summary>抓取规则类型：<c>Css</c>（AngleSharp 选择器）/ <c>Regex</c>（正则）。</summary>
    public string RuleType { get; set; } = "Css";
    /// <summary>抓取规则：CSS 选择器（如 <c>div.news a</c>）或正则表达式。</summary>
    public string Rule { get; set; } = "";
    /// <summary>自定义 User-Agent（空 = 用默认）。</summary>
    public string UserAgent { get; set; } = "";
    /// <summary>整合脚本（Jint / JavaScript）：变量 <c>items</c> 为抓取到的字符串数组，脚本应返回一个字符串作为输出。</summary>
    public string JsScript { get; set; } = "";
    /// <summary>最近一次抓取输出；未运行为 null。</summary>
    public string? LastOutput { get; set; }
    public DateTime? LastRunAt { get; set; }
    /// <summary>最近一次抓取到的条目数。</summary>
    public int LastCount { get; set; }
}
