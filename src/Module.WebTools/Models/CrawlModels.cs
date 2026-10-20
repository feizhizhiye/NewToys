using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.UI.Controls;

namespace NewToys.WebTools.Models;

/// <summary>爬虫面板结果里的一列（NT-086 批 D）。</summary>
public sealed class CrawlColumn
{
    public string Key { get; set; } = "";
    public string Expr { get; set; } = "";

    /// <summary>该列命中数（Python 侧按字段求值的长度）。</summary>
    public int Count { get; set; }

    /// <summary>Python 侧错误（⛔ 不静默：节头 / 诊断都要显示）。</summary>
    public string Error { get; set; } = "";

    /// <summary>该列全部值（按行序；行表合成用）。</summary>
    public List<string> Values { get; } = new();

    public bool HasError => Error.Length > 0;

    /// <summary>前 3 条值（诊断 / 运行记录预览用）。</summary>
    public List<string> Preview => Values.Take(3).ToList();
}

/// <summary>一次「🔗 抓链接」的结果（NT-086 批 D）：列元数据 + 行表。</summary>
public sealed class CrawlResult
{
    public List<CrawlColumn> Columns { get; } = new();

    /// <summary>行表：每行 = 列 key → 值（缺字段补空串）。</summary>
    public List<Dictionary<string, string>> Rows { get; } = new();

    /// <summary>Python 侧合成的行数（已截到 1000）。</summary>
    public int Total { get; set; }
}

/// <summary>目录节里的一行（NT-086 批 D）：<see cref="DynamicColumnGrid"/> 的 Cells + 下钻要用的 url。</summary>
public sealed partial class CrawlEntry : ObservableObject
{
    public CrawlEntry(IReadOnlyList<SysInfoCell> cells, string url, string title)
    {
        Cells = cells;
        Url = url;
        Title = title;
    }

    /// <summary>各列的值（顺序 = 列 schema 顺序）。</summary>
    public IReadOnlyList<SysInfoCell> Cells { get; }

    /// <summary>这一行的 url（下钻 / 试下载用；没有 = 空串）。</summary>
    public string Url { get; }

    /// <summary>这一行的 title（没有 url 时的展示兜底）。</summary>
    public string Title { get; }

    /// <summary>行首勾选框（NT-087：「⬇ 下载」只下勾选的行）。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>行右键「🧪 试下载」（🔴 每项自带命令：菜单在弹出树里取不到页面 VM 的命令）。</summary>
    public ICommand? TryDownloadCommand { get; set; }

    /// <summary>行右键「📋 复制诊断」（同上）。</summary>
    public ICommand? CopyDiagCommand { get; set; }
}

/// <summary>
/// 爬虫面板里的**一节**（NT-086 批 D；节头仿信息面板：状态点 / 标题 / 计数 / ⟳ / 折叠）。
/// <para>🔴 状态常量⛔ 不复用 <c>Module.ScriptManager</c> 的 <c>SysInfoStatus</c>（模块禁互引），本地一份。</para>
/// </summary>
public sealed partial class CrawlSection : ObservableObject
{
    public const string Ok = "ok";
    public const string Warn = "warn";
    public const string Error = "error";
    public const string Idle = "idle";

    public CrawlSection(string key, string title, string hint, bool isLinks = false)
    {
        Key = key;
        Title = title;
        Hint = hint;
        IsLinks = isLinks;
        IsPreview = key == "preview";
        IsProgress = key == "progress";
    }

    public string Key { get; }
    public string Title { get; }

    /// <summary>空态文案（节体在没数据时显示）。</summary>
    public string Hint { get; }

    /// <summary>是否为「链接·目录」节（期①唯一有数据 / 可跑的节）。</summary>
    public bool IsLinks { get; }

    /// <summary>是否为「② 试下载预览」节（NT-087）。</summary>
    public bool IsPreview { get; }

    /// <summary>是否为「③ 下载进度」节（NT-087）。</summary>
    public bool IsProgress { get; }

    [ObservableProperty] private string _status = Idle;
    [ObservableProperty] private string _countText = "—";
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _emptyText = "还没抓过。填好顶栏的脚本与起始网址，点「🔗 抓链接」。";

    /// <summary>节头状态点的着色条件（XAML <c>Classes.ok=…</c> 用）。</summary>
    public bool IsOk => Status == Ok;
    public bool IsWarn => Status == Warn;
    public bool IsError => Status == Error;

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsOk));
        OnPropertyChanged(nameof(IsWarn));
        OnPropertyChanged(nameof(IsError));
    }

    /// <summary>节头 ⟳（🔴 自带命令：与表头格 / 行右键同一套解法）。</summary>
    public ICommand? RefreshCommand { get; set; }

    /// <summary>节头折叠 / 展开（同上）。</summary>
    public ICommand? ToggleCommand { get; set; }

    /// <summary>目录节的条目（⛔ ②③ 节不用）。</summary>
    public ObservableCollection<CrawlEntry> Entries { get; } = new();

    /// <summary>目录节的表头（每列一个，自带排序命令）。</summary>
    public ObservableCollection<SysInfoHeaderCell> HeaderCells { get; } = new();

    /// <summary>目录节的列宽规格串（喂 <see cref="DynamicColumnGrid.ColumnSpecs"/>）。</summary>
    [ObservableProperty] private string _columnSpecs = "";

    public bool HasEntries => Entries.Count > 0;

    /// <summary>增删 <see cref="Entries"/> 后调用（ObservableCollection 不产生属性通知）。</summary>
    public void NotifyEntriesChanged() => OnPropertyChanged(nameof(HasEntries));
}

/// <summary>
/// 爬虫面板的列宽算法（NT-086 批 D）。
/// 🔴 与 <c>Module.ScriptManager</c> 的 <c>InfoRowFactory.Specs()</c> **故意分叉**（NT-085 约定：
/// 两份用法稳定后再评估合并）；这里按内容估值：中文记 2 宽，钳在 [90, 360]。
/// </summary>
public static class CrawlSpecs
{
    /// <summary>列宽规格串（逗号分隔；空列表 = 空串）。</summary>
    public static string Build(IReadOnlyList<CrawlColumn> columns, IReadOnlyList<Dictionary<string, string>> rows)
    {
        if (columns.Count == 0) return "";

        var parts = new List<string>(columns.Count);
        foreach (var col in columns)
        {
            var widest = DisplayWidth(col.Key);
            foreach (var row in rows.Take(50))
            {
                if (row.TryGetValue(col.Key, out var v))
                    widest = System.Math.Max(widest, DisplayWidth(v));
            }
            var width = System.Math.Clamp(24 + widest * 12, 90, 360);
            parts.Add(width.ToString());
        }
        return string.Join(",", parts);
    }

    /// <summary>展示宽度（CJK 记 2，其余记 1；⛔ 不引 EastAsianWidth 库，近似够用）。</summary>
    public static int DisplayWidth(string? text)
    {
        var w = 0;
        foreach (var ch in text ?? "")
            w += ch > 0x2E7F ? 2 : 1;
        return w;
    }
}
