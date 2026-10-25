using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;   // 🔴 SetTextAsync 是 IClipboard 的扩展方法，漏了这行会报 CS1061
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.UI.Controls;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 爬虫面板（<c>web-crawl</c>，+ ）：跑一个抓取脚本 → 目录节看条目 → 试下载 / 批量下载。
/// <para>
/// 🔴 四节：① 链接·目录（动态列 + 双击 url 下钻 + 行勾选 + 右键试下载/诊断）；
/// ② 试下载预览（单条跑 content 段，⛔ 不落最终产物）；③ 下载进度（批量 + 取消）；
/// ④ 运行记录（历史 run + 打开目录 / 日志 / 复制诊断）。
/// </para>
/// <para>
/// 🔴 下载 = <see cref="DownloadService"/> 内置（并发 / 限速 / 去重 / 续跑 / 取消），⛔ 不脚本化；
/// 脚本只负责「一页能取出什么」。「内容脚本 ▾」= 流水线的 content 槽位（调试集合页里配好的那个）。
/// </para>
/// <para>
/// 🔴 链式模式：一行都不勾 + 起始网址有值 ⇒ 从起始网址起 content + next 逐页跟进，
/// rows 出来的子条目入队，目录边跑边累积（① 节空时才往里灌，⛔ 不冲掉抓链接的结果）。
/// </para>
/// </summary>
public partial class CrawlPanelViewModel : ObservableObject, IStatusProvider, IToolbarSource
{
    private readonly RuleFileService _files = null!;
    private readonly RuleRunService _run = null!;
    private readonly RunRecordService _records = null!;
    private readonly DownloadService _dl = null!;
    private readonly FlowService _flows = null!;
    private readonly CrawlSection _links = null!;
    private readonly CrawlSection _preview = null!;
    private readonly CrawlSection _progress = null!;
    private readonly CrawlSection _history = null!;

    /// <summary>最近一次抓链接结果（复制诊断 / 运行记录用）。</summary>
    private CrawlResult _last = new();

    /// <summary>链式累积的默认表头（标题/网址/状态）是否已建。</summary>
    private bool _chainHeaderBuilt;

    private CancellationTokenSource? _dlCts;

    /// <summary>最近一次试下载的条目（② 节头 ⟳ 重试用）。</summary>
    private string _trialUrl = "";
    private string _trialTitle = "";

    [ObservableProperty] private string _startUrl = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private CategoryOption? _selectedScript;
    [ObservableProperty] private CategoryOption? _selectedContentScript;
    [ObservableProperty] private CategoryOption? _selectedFlow;
    [ObservableProperty] private string _sortKey = "";
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private CrawlEntry? _selectedEntry;
    [ObservableProperty] private string _footerSummary = "还没抓过。填好顶栏 → 点「🔗 抓链接」。";

    // ==================== 期②：试下载 / 下载进度状态 ====================

    [ObservableProperty] private TrialResult _trial = new();
    [ObservableProperty] private int _dlDone;
    [ObservableProperty] private int _dlTotal = -1;
    [ObservableProperty] private string _dlCurrent = "";

    /// <summary>③ 节里的逐行结局（✓/↷/✗ + 详情）。</summary>
    public ObservableCollection<DownloadRow> DownloadRows { get; } = new();

    // 🔴 XAML 的 IsVisible 只认 bool（⛔ 绑 .Count / .Length）⇒ 这两个是可见性开关
    public bool HasDownloadRows => DownloadRows.Count > 0;
    public bool HasDlCurrent => DlCurrent.Length > 0;

    /// <summary>进度条上限（链式 = 1：总数未知时走 IsIndeterminate，⛔ 不喂负数给 ProgressBar）。</summary>
    public double DlMax => DlTotal > 0 ? DlTotal : 1;

    /// <summary>进度条文案（链式 = 「N 条」；非链式 = 「N/M」）。</summary>
    public string DlProgressText => DlTotal > 0 ? DlDone + "/" + DlTotal : DlDone + " 条";

    /// <summary>进度条是否 indeterm（链式模式总数未知）。</summary>
    public bool DlIndeterminate => DlTotal < 0;

    partial void OnDlDoneChanged(int value) => OnPropertyChanged(nameof(DlProgressText));
    partial void OnDlCurrentChanged(string value) => OnPropertyChanged(nameof(HasDlCurrent));
    partial void OnDlTotalChanged(int value)
    {
        OnPropertyChanged(nameof(DlProgressText));
        OnPropertyChanged(nameof(DlIndeterminate));
        OnPropertyChanged(nameof(DlMax));
    }

    // ==================== 工具条（Shell 统一承载） ====================

    public IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Kind = ToolbarItemKind.Combo,
            Width = 150,
            ItemsPath = nameof(ScriptOptions),
            DisplayPath = nameof(CategoryOption.Label),
            SelectedItemPath = nameof(SelectedScript),
            ToolTip = "入口脚本：抓起始网址 → 出目录条目（rules 目录下的 .py）"
        },
        new()
        {
            Kind = ToolbarItemKind.Combo,
            Width = 150,
            ItemsPath = nameof(ContentOptions),
            DisplayPath = nameof(CategoryOption.Label),
            SelectedItemPath = nameof(SelectedContentScript),
            ToolTip = "content 段脚本：对条目跑它出正文 / 文件（= 调试集合里 content 槽位那个）"
        },
        new()
        {
            Kind = ToolbarItemKind.Combo,
            Width = 110,
            ItemsPath = nameof(FlowOptions),
            DisplayPath = nameof(CategoryOption.Label),
            SelectedItemPath = nameof(SelectedFlow),
            ToolTip = "选一条流水线 = 自动填好入口 / content 脚本（在「调试集合」页配）"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 220,
            Placeholder = "起始网址…",
            ValuePath = nameof(StartUrl),
            EnterCommandPath = nameof(FetchLinksCommand),
            ToolTip = "入口页 / 订阅源地址；回车 = 抓链接；不勾选行直接下载 = 链式模式"
        },
        new()
        {
            Text = "🔗 抓链接",
            Classes = "primary",
            CommandPath = nameof(FetchLinksCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "抓起始网址 → 跑入口脚本 → 目录节出条目（可双击 url 行逐级下钻）"
        },
        new()
        {
            Text = "🧪 试下载",
            Classes = "ghost",
            CommandPath = nameof(TryDownloadCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "对选中行跑 content 段：预览正文 / 文件 / 页数，⛔ 不落最终产物"
        },
        new()
        {
            Text = "⬇ 下载",
            Classes = "primary",
            CommandPath = nameof(StartDownloadCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "下载勾选的行；一行都不勾 = 链式模式（从起始网址 content + next 边跑边累积）"
        },
        new()
        {
            Text = "⏹ 取消",
            Classes = "ghost",
            CommandPath = nameof(CancelDownloadCommand),
            IsEnabledPath = nameof(IsBusy),
            ToolTip = "取消本次下载（已下完的文件保留）"
        },
        new()
        {
            Text = "📋 复制诊断",
            Classes = "ghost",
            CommandPath = nameof(CopyDiagCommand),
            ToolTip = "把 脚本 / 网址 / 每列命中与错误 / 前 5 行 打包进剪贴板（给 AI 复盘）"
        }
    };

    public event EventHandler? ToolbarChanged;
    public event EventHandler? StatusChanged;

    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 构造 ====================

    public CrawlPanelViewModel() { }

    public CrawlPanelViewModel(RuleFileService files, RuleRunService run, RunRecordService records,
        DownloadService download, FlowService flows)
    {
        _files = files;
        _run = run;
        _records = records;
        _dl = download;
        _flows = flows;

        // ③ 节空态开关（集合变化不走属性通知）
        DownloadRows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDownloadRows));

        foreach (var m in _files.Scan()
                     .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ScriptOptions.Add(new CategoryOption { Label = m.Name, Value = m.Name });
            ContentOptions.Add(new CategoryOption { Label = m.Name, Value = m.Name });
        }
        if (ScriptOptions.Count > 0) SelectedScript = ScriptOptions[0];
        if (ContentOptions.Count > 0) SelectedContentScript = ContentOptions[0];

        FlowOptions.Add(new CategoryOption { Label = "（手动选脚本）", Value = "" });
        foreach (var name in _flows.List()) FlowOptions.Add(new CategoryOption { Label = name, Value = name });
        SelectedFlow = FlowOptions[0];

        // 三节（节头仿信息面板：状态点 / 标题 / 计数 / ⟳ / 折叠）
        _links = new CrawlSection("links", "① 链接 · 目录",
            "还没抓过。选入口脚本 → 填起始网址 → 点「🔗 抓链接」。", isLinks: true);
        _links.RefreshCommand = RefreshSectionCommand;
        _links.ToggleCommand = ToggleSectionCommand;
        Sections.Add(_links);
        _preview = new CrawlSection("preview", "② 试下载预览",
            "选中一行 →「🧪 试下载」：跑 content 段预览正文 / 文件 / 页数，⛔ 不落最终产物。");
        _preview.RefreshCommand = RefreshSectionCommand;
        _preview.ToggleCommand = ToggleSectionCommand;
        Sections.Add(_preview);
        _progress = new CrawlSection("progress", "③ 下载进度",
            "勾选几行 →「⬇ 下载」；一行都不勾 = 链式模式。产物落 downloads\\<脚本>\\<条目>\\。");
        _progress.RefreshCommand = RefreshSectionCommand;
        _progress.ToggleCommand = ToggleSectionCommand;
        Sections.Add(_progress);
        _history = new CrawlSection("history", "④ 运行记录",
            "这个脚本还没跑过 —— 跑一次「🔗 抓链接」就会在这里留一条（每脚本只留最近 10 条）。");
        _history.RefreshCommand = RefreshSectionCommand;
        _history.ToggleCommand = ToggleSectionCommand;
        Sections.Add(_history);
        RefreshHistory();

        StatusText = ScriptOptions.Count == 0
            ? "rules 目录里还没有脚本 —— 去「抓取规则」页新建，或等首次运行播种预设。"
            : "选脚本 → 填起始网址 →「🔗 抓链接」→ 勾行 →「⬇ 下载」。";
    }

    /// <summary>「入口脚本 ▾」数据源（Label = 规则集名）。</summary>
    public ObservableCollection<CategoryOption> ScriptOptions { get; } = new();

    /// <summary>「内容脚本 ▾」数据源（= 流水线的 content 槽位）。</summary>
    public ObservableCollection<CategoryOption> ContentOptions { get; } = new();

    /// <summary>「流水线 ▾」数据源（首项 = 手动选脚本）。</summary>
    public ObservableCollection<CategoryOption> FlowOptions { get; } = new();

    /// <summary>三节（① 链接·目录 ② 试下载预览 ③ 下载进度）。</summary>
    public ObservableCollection<CrawlSection> Sections { get; } = new();

    // ==================== 抓链接 ====================

    /// <summary>
    /// 🔗 抓链接：抓起始网址 → 跑入口脚本 → 目录节出行表。
    /// 🔴 落一条运行记录（⛔ 失败不打断）；结果同时回写底栏摘要。
    /// </summary>
    [RelayCommand]
    private async Task FetchLinksAsync()
    {
        if (IsBusy) return;

        var script = SelectedScript?.Value ?? "";
        if (script.Length == 0) { StatusText = "先在「脚本 ▾」里选一个入口脚本。"; return; }

        var url = (StartUrl ?? "").Trim();
        if (url.Length == 0) { StatusText = "先填起始网址。"; return; }

        IsBusy = true;
        _links.IsBusy = true;
        var sw = Stopwatch.StartNew();
        try
        {
            StatusText = "正在抓取 " + url + " ……";
            var (_, enc) = _files.ReadMeta(script);
            var (ok, _, error) = await _run.FetchIntoAsync(url, enc, _run.CrawlHtmlPath).ConfigureAwait(true);
            if (!ok)
            {
                StatusText = "⚠ " + error;
                _links.Status = CrawlSection.Error;
                return;
            }

            StatusText = "正在跑「" + script + "」……";
            var result = await _run.RunRowsAsync(script, _run.CrawlHtmlPath, url).ConfigureAwait(true);
            sw.Stop();
            _last = result;

            ApplyResult(result);

            try
            {
                _records.SaveFields(script, url, result.Columns.Select(c => new RunField
                {
                    Key = c.Key,
                    Expr = c.Expr,
                    Count = c.Count,
                    Error = c.Error,
                    Preview = c.Preview
                }).ToList(), _run.CrawlHtmlPath);
            }
            catch { /* 运行记录是附属产物，失败只当没记 */ }

            var bad = result.Columns.Count(c => c.HasError);
            FooterSummary = $"条数 {result.Total} · 字段 {result.Columns.Count} · 错误 {bad} · 耗时 {sw.Elapsed.TotalSeconds:F1}s";
            _links.Status = bad > 0 ? CrawlSection.Warn : CrawlSection.Ok;
            _links.CountText = result.Total + " 条";

            StatusText = bad > 0
                ? $"「{script}」跑完：{result.Total} 条（{bad} 列出错 —— 看节体或「📋 复制诊断」）。"
                : $"「{script}」跑完：{result.Total} 条。勾选行 →「⬇ 下载」，或双击 url 行下钻。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 运行失败：" + FirstLine(ex.Message);
            _links.Status = CrawlSection.Error;
        }
        finally
        {
            IsBusy = false;
            _links.IsBusy = false;
            RefreshHistory();
        }
    }

    /// <summary>把结果灌进目录节（列 / 表头 / 规格串 / 行；空态文案按情况换）。</summary>
    private void ApplyResult(CrawlResult result)
    {
        _links.Entries.Clear();
        _links.HeaderCells.Clear();
        _links.ColumnSpecs = "";
        _chainHeaderBuilt = false;

        foreach (var col in result.Columns)
        {
            var header = new SysInfoHeaderCell(col.Key, col.Key, "", true) { SortCommand = SortByCommand };
            _links.HeaderCells.Add(header);
        }

        _links.ColumnSpecs = CrawlSpecs.Build(result.Columns, result.Rows);

        foreach (var row in result.Rows)
        {
            var cells = result.Columns
                .Select(c => new SysInfoCell(c.Key, c.Key, row.TryGetValue(c.Key, out var v) ? v : "", true))
                .ToList();
            var url = row.TryGetValue("url", out var u) ? u : "";
            var title = row.TryGetValue("title", out var t) ? t : "";
            _links.Entries.Add(HookEntry(new CrawlEntry(cells, url, title)
            {
                // 🔴 行右键命令挂在行对象上（ContextMenu 弹出树取不到 VM）
                TryDownloadCommand = TryDownloadCommand,
                CopyDiagCommand = CopyDiagCommand
            }));
        }
        _links.NotifyEntriesChanged();
        OnPropertyChanged(nameof(LinksAllSelected));

        _links.EmptyText = result.Columns.Count == 0
            ? "这个脚本没有 RULES（或没解析出列）—— 表达式写在 RULES 列表里。"
            : "跑完了，但一条都没解析出来（可能要换起始网址，或表达式该调）。";

        UpdateSortArrows();
    }

    /// <summary>目录节表头的全选框（勾 = 全选；行勾选变化会回推这个属性）。</summary>
    public bool LinksAllSelected
    {
        get => _links.Entries.Count > 0 && _links.Entries.All(e => e.IsSelected);
        set
        {
            foreach (var e in _links.Entries) e.IsSelected = value;
            OnPropertyChanged();
        }
    }

    // ==================== 排序（客户端字符串 / 数字排序） ====================

    /// <summary>点表头 = 按这一列排序（再点 = 反向）。</summary>
    [RelayCommand]
    private void SortBy(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (SortKey == key) SortDescending = !SortDescending;
        else { SortKey = key; SortDescending = false; }

        ReorderEntries();
        UpdateSortArrows();
    }

    private void ReorderEntries()
    {
        if (SortKey.Length == 0 || _links.Entries.Count == 0) return;

        var key = SortKey;
        var desc = SortDescending;
        var items = _links.Entries.ToList();
        items.Sort((a, b) =>
        {
            var va = TextOf(a, key);
            var vb = TextOf(b, key);
            var cmp = BothNumeric(va, vb, out var na, out var nb)
                ? na.CompareTo(nb)
                : string.Compare(va, vb, StringComparison.CurrentCultureIgnoreCase);
            return desc ? -cmp : cmp;
        });

        _links.Entries.Clear();
        foreach (var it in items) _links.Entries.Add(it);
    }

    /// <summary>行勾选变化 → 回推表头全选框（<see cref="LinksAllSelected"/> 是计算属性，自己不会发通知）。</summary>
    private CrawlEntry HookEntry(CrawlEntry entry)
    {
        entry.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(CrawlEntry.IsSelected)) OnPropertyChanged(nameof(LinksAllSelected));
        };
        return entry;
    }

    private static string TextOf(CrawlEntry entry, string key)
        => entry.Cells.FirstOrDefault(c => c.Attr == key)?.Text ?? "";

    /// <summary>
    /// 两列都能按数值解析时才按数值比（否则按字符串）。
    /// 🔴 先给 out 赋初值：<c>&amp;&amp;</c> 短路会让第二个 <c>TryParse</c> 不执行 ⇒ CS0177。
    /// </summary>
    private static bool BothNumeric(string a, string b, out double na, out double nb)
    {
        na = 0;
        nb = 0;
        return double.TryParse(a, out na) && double.TryParse(b, out nb);
    }

    private void UpdateSortArrows()
    {
        foreach (var h in _links.HeaderCells)
            h.Text = h.Title + (h.Attr == SortKey ? (SortDescending ? " ▼" : " ▲") : "");
    }

    // ==================== 下钻 / 流水线 ====================

    /// <summary>双击 url 行 = 下钻：用这一行的地址再抓一次（链式，用户驱动）。</summary>
    [RelayCommand]
    private void DrillDown(CrawlEntry? entry)
    {
        if (entry is null) return;
        if (entry.Url.Length == 0) { StatusText = "这一行没有 url 列，没法下钻。"; return; }

        StartUrl = entry.Url;
        _ = FetchLinksAsync();
    }

    /// <summary>选流水线 = 按它的槽位填 入口 / content 脚本（ Toc 空 ⇒ 提示链式模式）。</summary>
    partial void OnSelectedFlowChanged(CategoryOption? value)
    {
        if (value is null || value.Value.Length == 0) return;
        try
        {
            var flow = _flows.Load(value.Value);
            if (flow.Entry.Length > 0)
                SelectedScript = ScriptOptions.FirstOrDefault(o => o.Value == flow.Entry) ?? SelectedScript;
            if (flow.Content.Length > 0)
                SelectedContentScript = ContentOptions.FirstOrDefault(o => o.Value == flow.Content) ?? SelectedContentScript;

            StatusText = flow.Toc.Length == 0
                ? $"流水线「{flow.Name}」没配目录段 ⇒ 链式模式：清空勾选，直接「⬇ 下载」从起始网址边跑边累积。"
                : $"已按流水线「{flow.Name}」填好入口 / content 脚本。勾行 →「⬇ 下载」。";
        }
        catch (Exception ex) { StatusText = "⚠ 读流水线失败：" + FirstLine(ex.Message); }
    }

    // ==================== 🧪 试下载 ====================

    /// <summary>
    /// 对一行跑 content 段（含 next 跟进）→ ② 节出预览。⛔ 不落最终产物（临时副本在 trials\）。
    /// </summary>
    [RelayCommand]
    private async Task TryDownload(CrawlEntry? entry)
    {
        if (IsBusy) return;

        var script = SelectedContentScript?.Value ?? "";
        if (script.Length == 0)
        {
            StatusText = "先在「内容脚本 ▾」里选 content 段脚本（产出正文 / 文件的那个）。";
            return;
        }

        var row = entry ?? SelectedEntry
                       ?? _links.Entries.FirstOrDefault(e => e.IsSelected)
                       ?? _links.Entries.FirstOrDefault();
        if (row is null) { StatusText = "没有可试下载的行 —— 先「🔗 抓链接」或在起始网址框填个地址。"; return; }
        if (row.Url.Length == 0) { StatusText = "这一行没有 url 列，没法试下载。"; return; }

        await RunTrialAsync(script, row.Url, row.Title).ConfigureAwait(true);
    }

    /// <summary>
    /// 试下载的执行体（🔴 与批量下载共用 <see cref="DownloadService"/> 的 content 管线）。
    /// ⛔ 不落最终产物（临时副本在 <c>trials\</c>）。
    /// </summary>
    private async Task RunTrialAsync(string script, string url, string title)
    {
        // 记住这一条：② 节头 ⟳ 重跑要用（行可能在重排 / 重新抓取后换对象）
        _trialUrl = url;
        _trialTitle = title;

        IsBusy = true;
        _preview.IsBusy = true;
        try
        {
            var name = title.Length > 0 ? title : url;
            StatusText = "🧪 试下载：「" + name + "」……";
            var t = await _dl.TrialAsync(script, url, title, CancellationToken.None).ConfigureAwait(true);

            Trial = t;
            _preview.IsBusy = false;
            _preview.CountText = t.Error.Length > 0 ? "出错" : t.Pages + " 页 · " + t.Seconds.ToString("F1") + "s";
            _preview.Status = t.Error.Length > 0 ? CrawlSection.Error : CrawlSection.Ok;
            StatusText = t.Error.Length > 0
                ? "⚠ 试下载失败：" + t.Error
                : "🧪 试下载完成（" + t.KindText + "，⛔ 未落最终产物）。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 试下载失败：" + FirstLine(ex.Message);
            _preview.Status = CrawlSection.Error;
            _preview.IsBusy = false;
        }
        finally
        {
            IsBusy = false;
            _preview.IsBusy = false;
        }
    }

    // ==================== ⬇ 批量下载 ====================

    /// <summary>
    /// 下载勾选的行；一行都不勾 = 链式模式（起始网址起，content + next 边跑边累积）。
    /// 产物落 <c>downloads\&lt;脚本&gt;\&lt;条目&gt;\</c>（⛔ 不进备份）。
    /// </summary>
    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        if (IsBusy) return;

        var script = SelectedContentScript?.Value ?? "";
        if (script.Length == 0)
        {
            StatusText = "先在「内容脚本 ▾」里选 content 段脚本。";
            return;
        }

        var req = new DownloadRequest { Script = script };
        var checkedRows = _links.Entries.Where(e => e.IsSelected && e.Url.Length > 0).ToList();
        if (checkedRows.Count > 0)
        {
            foreach (var e in checkedRows) req.Entries.Add((e.Url, e.Title.Length > 0 ? e.Title : e.Url));
        }
        else
        {
            req.StartUrl = (StartUrl ?? "").Trim();
            if (req.StartUrl.Length == 0)
            {
                StatusText = "勾选几行再下载；或一行不勾 + 填起始网址 = 链式模式。";
                return;
            }
        }

        _dlCts = new CancellationTokenSource();
        IsBusy = true;
        _progress.IsBusy = true;
        _progress.Status = CrawlSection.Ok;
        DownloadRows.Clear();
        DlTotal = req.IsChain ? -1 : req.Entries.Count;
        DlDone = 0;
        DlCurrent = "";
        var sw = Stopwatch.StartNew();
        try
        {
            StatusText = req.IsChain
                ? "链式下载：从起始网址 content + next 逐页跟进……"
                : "开始下载 " + req.Entries.Count + " 条（并发 " + Math.Clamp(_files.Scan().FirstOrDefault(m => m.Name == script)?.Concurrency ?? 1, 1, 8) + "）……";

            var outcome = await _dl.DownloadAsync(req, new Progress<DownloadEvent>(OnDlEvent), _dlCts.Token)
                .ConfigureAwait(true);
            sw.Stop();

            _progress.IsBusy = false;
            _progress.Status = outcome.Failed > 0 ? CrawlSection.Warn : CrawlSection.Ok;
            _progress.CountText = $"✓ {outcome.Ok} · ↷ {outcome.Skipped} · ✗ {outcome.Failed}";
            FooterSummary = $"下载 {outcome.Ok + outcome.Skipped + outcome.Failed} 条"
                          + $"（✓{outcome.Ok} ↷{outcome.Skipped} ✗{outcome.Failed}）· 耗时 {sw.Elapsed.TotalSeconds:F1}s"
                          + (outcome.StopReason.Length > 0 ? " · " + outcome.StopReason : "");
            StatusText = outcome.Cancelled
                ? "已取消下载（已下完的文件保留）。"
                : outcome.StopReason.Length > 0
                    ? "⚠ " + outcome.StopReason
                    : "下载完成 → " + _dl.DownloadsDir;
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 下载失败：" + FirstLine(ex.Message);
            _progress.Status = CrawlSection.Error;
            _progress.IsBusy = false;
        }
        finally
        {
            IsBusy = false;
            _dlCts?.Dispose();
            _dlCts = null;
        }
    }

    /// <summary>⏹ 取消下载。</summary>
    [RelayCommand]
    private void CancelDownload()
    {
        if (_dlCts is null || IsBusy == false) return;
        _dlCts.Cancel();
        StatusText = "正在取消……（已下完的文件保留）";
    }

    /// <summary>下载事件 → ③ 节逐行 + 进度；链式模式顺带把 ① 节边跑边累积。</summary>
    private void OnDlEvent(DownloadEvent ev)
    {
        if (ev.Phase == "start")
        {
            DlTotal = ev.Total;
            return;
        }

        var row = DownloadRows.FirstOrDefault(r => string.Equals(r.Name, ev.Name, StringComparison.Ordinal));
        if (row is null)
        {
            row = new DownloadRow(ev.Name);
            DownloadRows.Add(row);
        }
        row.Status = ev.Status;
        row.Detail = ev.Detail;
        DlDone = ev.Done;
        DlCurrent = ev.Name + " · " + ev.Detail;
        _progress.CountText = ev.Total > 0 ? ev.Done + "/" + ev.Total : ev.Done + " 条";

        // 链式模式：目录边跑边累积（🔴 ① 节空时才灌，⛔ 不冲掉抓链接的结果）
        if (DlTotal < 0 && _links.Entries.Count == 0)
        {
            EnsureChainHeader();
            _links.Entries.Add(HookEntry(new CrawlEntry(new List<SysInfoCell>
            {
                new("title", "标题", ev.Name, true),
                new("url", "网址", ev.Url, true),
                new("status", "状态", row.StatusText + " " + ev.Detail, true)
            }, ev.Url, ev.Name)));
            _links.NotifyEntriesChanged();
            OnPropertyChanged(nameof(LinksAllSelected));
        }
    }

    /// <summary>链式累积的默认三列（标题 / 网址 / 状态）。</summary>
    private void EnsureChainHeader()
    {
        if (_chainHeaderBuilt) return;
        _links.HeaderCells.Clear();
        foreach (var (key, title, width) in new[]
                 {
                     ("title", "标题", "200"),
                     ("url", "网址", "300"),
                     ("status", "状态", "160")
                 })
        {
            _links.HeaderCells.Add(new SysInfoHeaderCell(key, title, "", true) { SortCommand = SortByCommand });
            _links.ColumnSpecs += (_links.ColumnSpecs.Length > 0 ? "," : "") + width;
        }
        _chainHeaderBuilt = true;
        _links.EmptyText = "链式下载进行中：每完成一条就往这里累积一行。";
    }

    // ==================== ④ 运行记录 ====================

    /// <summary>当前「脚本 ▾」选中脚本的最近运行记录（新 → 旧）。</summary>
    public ObservableCollection<RunHistoryRow> HistoryRows { get; } = new();

    /// <summary>🔴 XAML 的 IsVisible 只认 bool（⛔ 绑 .Count）。</summary>
    public bool HasHistoryRows => HistoryRows.Count > 0;

    /// <summary>④ 节空态文案（节体 DataContext 是页面 VM ⇒ 从节对象转一道）。</summary>
    public string HistoryEmptyText => _history?.Hint ?? "";

    /// <summary>
    /// 读磁盘刷新 ④ 节（⛔ 不跑脚本）：取当前脚本最近 <see cref="RunRecordService.KeepRuns"/> 条 run。
    /// </summary>
    private void RefreshHistory()
    {
        HistoryRows.Clear();
        var script = SelectedScript?.Value ?? "";
        if (script.Length == 0)
        {
            _history.CountText = "—";
            OnPropertyChanged(nameof(HasHistoryRows));
            return;
        }

        foreach (var rec in _records.History(script))
        {
            HistoryRows.Add(new RunHistoryRow(rec)
            {
                // 🔴 右键命令挂在行对象上（ContextMenu 在弹出树，取不到页面 VM）
                OpenDirCommand = OpenRunDirCommand,
                OpenLogCommand = OpenRunLogCommand,
                CopyDiagCommand = CopyRunDiagCommand
            });
        }

        _history.CountText = HistoryRows.Count > 0 ? HistoryRows.Count + " 条" : "—";
        OnPropertyChanged(nameof(HasHistoryRows));
    }

    /// <summary>换了入口脚本 ⇒ 换一批历史。</summary>
    partial void OnSelectedScriptChanged(CategoryOption? value) => RefreshHistory();

    /// <summary>📂 打开这次运行的目录（产物：result.json / page.html / diag.txt）。</summary>
    [RelayCommand]
    private void OpenRunDir(RunHistoryRow? row)
    {
        if (row is null || !Directory.Exists(row.Dir)) { StatusText = "这次运行的目录已经不在了。"; return; }
        try { Process.Start(new ProcessStartInfo(row.Dir) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = "⚠ 打开目录失败：" + FirstLine(ex.Message); }
    }

    /// <summary>📄 打开这次运行的日志（diag.txt，没有就记事本打开不了 ⇒ 直接提示）。</summary>
    [RelayCommand]
    private void OpenRunLog(RunHistoryRow? row)
    {
        if (row is null) return;
        if (!File.Exists(row.LogPath)) { StatusText = "这次运行没有留下 diag.txt。"; return; }
        try { Process.Start(new ProcessStartInfo(row.LogPath) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = "⚠ 打开日志失败：" + FirstLine(ex.Message); }
    }

    /// <summary>📋 复制这次运行的诊断（diag.txt 原文；没文件就拼一条摘要）。</summary>
    [RelayCommand]
    private async Task CopyRunDiagAsync(RunHistoryRow? row)
    {
        if (row is null) return;

        var text = File.Exists(row.LogPath)
            ? File.ReadAllText(row.LogPath)
            : $"脚本：{SelectedScript?.Label ?? "—"}\n网址：{row.Url}\n时间：{row.AtText}\n{row.Summary}\n目录：{row.Dir}";

        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }

        try
        {
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制这次运行的诊断。";
        }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    // ==================== 节头 ====================

    /// <summary>节头 ⟳ = 重跑（目录节 = 重新抓取；试下载节 = 对当前行再试一次）。</summary>
    [RelayCommand]
    private void RefreshSection(CrawlSection? section)
    {
        if (section is { IsLinks: true }) { _ = FetchLinksAsync(); return; }

        // ② 节 ⟳ = 对最后试过的那条再跑一次（🔴 行对象可能已换，按 url + title 重来）
        if (section is { IsPreview: true } && _trialUrl.Length > 0)
        {
            var script = SelectedContentScript?.Value ?? "";
            if (script.Length == 0) { StatusText = "先在「内容脚本 ▾」里选 content 段脚本。"; return; }
            _ = RunTrialAsync(script, _trialUrl, _trialTitle);
        }

        // ④ 节 ⟳ = 重新读磁盘（历史是文件，⛔ 不重跑脚本）
        if (section is { IsHistory: true }) { RefreshHistory(); StatusText = "已刷新运行记录。"; }
    }

    /// <summary>节头折叠 / 展开。</summary>
    [RelayCommand]
    private void ToggleSection(CrawlSection? section)
    {
        if (section is not null) section.IsExpanded = !section.IsExpanded;
    }

    // ==================== 诊断复制 ====================

    /// <summary>
    /// 📋 复制诊断：脚本 / 网址 / HTML 大小 / 每列命中与错误 / 前 5 行（给 AI / 给自己复盘）。
    /// </summary>
    [RelayCommand]
    private async Task CopyDiagAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# NewToys 爬虫面板诊断");
        sb.AppendLine("入口脚本：" + (SelectedScript?.Label ?? "—"));
        sb.AppendLine("content 脚本：" + (SelectedContentScript?.Label ?? "—"));
        sb.AppendLine("流水线：" + (SelectedFlow?.Value.Length > 0 ? SelectedFlow.Value : "（手动）"));
        sb.AppendLine("网址：" + (StartUrl.Length == 0 ? "—" : StartUrl));
        sb.AppendLine("HTML：" + (File.Exists(_run.CrawlHtmlPath)
            ? new FileInfo(_run.CrawlHtmlPath).Length + " 字节"
            : "（没抓）"));
        sb.AppendLine();

        sb.AppendLine("## 列");
        foreach (var c in _last.Columns)
            sb.AppendLine($"  {c.Key} · {c.Expr} · {c.Count} 条" + (c.HasError ? " · 错误：" + c.Error : ""));

        sb.AppendLine();
        sb.AppendLine("## 前 5 行");
        foreach (var row in _last.Rows.Take(5))
            sb.AppendLine("  " + string.Join(" | ",
                _last.Columns.Select(c => c.Key + "=" + (row.TryGetValue(c.Key, out var v) ? v : ""))));

        if (Trial.Diag.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 试下载诊断");
            sb.AppendLine(Trial.Diag);
        }

        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }

        try
        {
            await win.Clipboard.SetTextAsync(sb.ToString().TrimEnd());
            StatusText = "已复制诊断（脚本 / 网址 / 每列命中与错误 / 前 5 行）。";
        }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    private static string FirstLine(string? text)
    {
        var t = (text ?? "").Trim();
        var nl = t.IndexOf('\n');
        return nl > 0 ? t[..nl] : t;
    }
}
