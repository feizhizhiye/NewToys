using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.WebTools.Models;

/// <summary>
/// 期②下载的字段求值结果（NT-087）：<see cref="RuleRunService"/> 的 <c>mode=step</c> 回包。
/// <para>
/// 🔴 与 <see cref="CrawlResult"/> 的分工：那个按索引合成行表（面板列表用），这个回**全量值**（下载落盘用，⛔ 不截断）。
/// </para>
/// </summary>
public sealed class StepResult
{
    /// <summary>字段名 → 全部值（求值成功的）。</summary>
    public Dictionary<string, List<string>> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>字段名 → Python 侧异常（⛔ 不静默：诊断 / 界面都要显示）。</summary>
    public Dictionary<string, string> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Get(string key) => Fields.TryGetValue(key, out var v) ? v : new List<string>();

    public string? ErrorOf(string key) => Errors.TryGetValue(key, out var e) ? e : null;

    public bool HasError => Errors.Count > 0;
}

/// <summary>产物形态（优先级链：content 返回 ＞ <c># artifact:</c> ＞ 分类缺省猜测 ＞ none）。</summary>
public enum ArtifactKind
{
    None,
    Text,
    Files,
    Rows
}

/// <summary>
/// 一个条目解析出的产物计划（NT-087）：下载器按它落盘。
/// </summary>
public sealed class ArtifactPlan
{
    public ArtifactKind Kind { get; set; } = ArtifactKind.None;

    /// <summary><see cref="ArtifactKind.Text"/>：正文（多值已用空行拼好）。</summary>
    public string Text { get; set; } = "";

    /// <summary><see cref="ArtifactKind.Files"/>：要下载的文件（url 已 urljoin）。</summary>
    public List<(string url, string name)> Files { get; } = new();

    /// <summary><see cref="ArtifactKind.Rows"/>：递归当链接的子条目（⛔ 本身不下盘）。</summary>
    public List<(string url, string title)> Rows { get; } = new();

    /// <summary>判定依据（诊断复制用：「content 返回 JSON.files」/「# artifact: files」/「分类 images」/「缺省文本」）。</summary>
    public string Reason { get; set; } = "";
}

/// <summary>🧪 试下载的预览结果（② 节绑定；⛔ 不落最终产物，临时落 <c>trials\</c>）。</summary>
public sealed partial class TrialResult : ObservableObject
{
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _kindText = "";
    [ObservableProperty] private string _textPreview = "";
    [ObservableProperty] private int _pages;
    [ObservableProperty] private double _seconds;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _diag = "";

    /// <summary>文件清单（<see cref="ArtifactKind.Files"/> 时；行 = 「名称 · 地址」）。</summary>
    public ObservableCollection<string> FileLines { get; } = new();

    public bool Ok => HasResult && Error.Length == 0;

    // 🔴 XAML 的 IsVisible 只认 bool（⛔ 绑 string.Length / Count —— 默认转换器不收）⇒ 这三个是可见性开关
    public bool HasError => Error.Length > 0;
    public bool HasText => TextPreview.Length > 0;
    public bool HasFiles => FileLines.Count > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnTextPreviewChanged(string value) => OnPropertyChanged(nameof(HasText));
    partial void OnHasResultChanged(bool value) => OnPropertyChanged(nameof(Ok));

    /// <summary>增删 <see cref="FileLines"/> 后调用（集合变化不走属性通知）。</summary>
    public void NotifyFileLines() => OnPropertyChanged(nameof(HasFiles));
}

/// <summary>③ 下载进度节里的一行（一个条目的结局）。</summary>
public sealed partial class DownloadRow : ObservableObject
{
    public DownloadRow(string name) => Name = name;

    public string Name { get; }

    /// <summary>ok / skip / fail / run。</summary>
    [ObservableProperty] private string _status = "run";

    [ObservableProperty] private string _detail = "";

    public string StatusText => Status switch
    {
        "ok" => "✓",
        "skip" => "↷",
        "fail" => "✗",
        _ => "…"
    };

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusText));
}

/// <summary>下载进度事件（<see cref="Services.DownloadService"/> → VM）。</summary>
public sealed class DownloadEvent
{
    /// <summary>start / item / stop。</summary>
    public string Phase { get; set; } = "item";

    public string Name { get; set; } = "";

    /// <summary>条目地址（链式模式往目录节累积行用）。</summary>
    public string Url { get; set; } = "";

    public string Status { get; set; } = "run";
    public string Detail { get; set; } = "";

    /// <summary>已完成条目数。</summary>
    public int Done { get; set; }

    /// <summary>总条目数（链式模式 = -1，边跑边累积）。</summary>
    public int Total { get; set; } = -1;
}

/// <summary>一次批量下载的结局汇总。</summary>
public sealed class DownloadOutcome
{
    public int Ok { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public bool Cancelled { get; set; }
    public string StopReason { get; set; } = "";
}

/// <summary>下载请求（NT-087）：勾选的行（非链式）或起始网址（链式，<see cref="Entries"/> 空）。</summary>
public sealed class DownloadRequest
{
    /// <summary>content 段脚本名。</summary>
    public string Script { get; set; } = "";

    /// <summary>非链式：勾选的目录行（url + title）。</summary>
    public List<(string url, string title)> Entries { get; } = new();

    /// <summary>链式：起始网址（<see cref="Entries"/> 空时生效）。</summary>
    public string StartUrl { get; set; } = "";

    public bool IsChain => Entries.Count == 0;
}
