using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NewToys.WebTools.Models;

/// <summary>
/// 一次「▶ 跑这段」的运行记录（NT-086 批 G）—— 落 <c>runs\&lt;脚本&gt;\&lt;时间戳&gt;_&lt;URL哈希&gt;\result.json</c>。
/// </summary>
public sealed class RunRecord
{
    /// <summary>跑的脚本名（= 规则文件名去扩展名）。</summary>
    public string Script { get; set; } = "";

    /// <summary>起始网址。</summary>
    public string Url { get; set; } = "";

    /// <summary>运行方式（<c>run</c> = 跑这段；试跑推荐 ⛔ 不记）。</summary>
    public string Mode { get; set; } = "run";

    /// <summary>运行时刻（本机时间）。</summary>
    public DateTime At { get; set; }

    /// <summary>当次 HTML 字符数。</summary>
    public int HtmlChars { get; set; }

    /// <summary>条目数（url 字段命中优先，段间契约：入口 / 中间段必须含 url）。</summary>
    public int Total { get; set; }

    /// <summary>每字段的命中 / 错误 / 前 3 条预览。</summary>
    public List<RunField> Fields { get; set; } = new();

    /// <summary>落盘目录（打开 / 导出用；⛔ 不写进 json）。</summary>
    [JsonIgnore] public string Dir { get; set; } = "";
}

/// <summary>运行记录里的一个字段（NT-086 批 G）。</summary>
public sealed class RunField
{
    public string Key { get; set; } = "";
    public string Expr { get; set; } = "";
    public int Count { get; set; }
    public string Error { get; set; } = "";

    /// <summary>前 3 条预览（与编辑器右栏同口径，单条截断 60 字符）。</summary>
    public List<string> Preview { get; set; } = new();
}
