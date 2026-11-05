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

/// <summary>爬虫面板（web-crawl，+ ）：跑一个抓取脚本 → 目录节看条目 → 试下载 / 批量下载。（WHY: SM-060）</summary>
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
            Text = "下载", IconKey = "IconArrowDown",
            Classes = "primary",
            CommandPath = nameof(StartDownloadCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "下载勾选的行；一行都不勾 = 链式模式（从起始网址 content + next 边跑边累积）"
        },
        new()
        {
            Text = "取消", IconKey = "IconStop",
            Classes = "ghost",
            CommandPath = nameof(CancelDownloadCommand),
            IsEnabledPath = nameof(IsBusy),
            ToolTip = "取消本次下载（已下完的文件保留）"
        },
        new()
        {
            Text = "复制", IconKey = "IconCopy",
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
            "勾选几行 →「下载」；一行都不勾 = 链式模式。产物落 downloads\\<脚本>\\<条目>\\。");
        _progress.RefreshCommand = RefreshSectionCommand;
        _progress.ToggleCommand = ToggleSectionCommand;
        Sections.Add(_progress);
        _history = new CrawlSection("history", "④ 运行记录",
            "这个脚本还没跑过 —— 跑一次「🔗 抓链接」就会在这里留一条（每脚本只留最近 10 条）。");
        _history.RefreshCommand = RefreshSectionCommand;
        _history.ToggleCommand = ToggleSectionCommand;
        Sections.Add(_history);

        // 🔴 必须在四个节都建好之后再设 SelectedScript：它的 setter 立刻调 RefreshHistory()，
        //    而 _history 是 null! 字段、此处才有值 ⇒ 提前赋值就是 NullReferenceException，
        //    VM 构造崩掉 ⇒ DataContext 为 null ⇒ 整页空白且无任何提示。
        if (ScriptOptions.Count > 0) SelectedScript = ScriptOptions[0];
        if (ContentOptions.Count > 0) SelectedContentScript = ContentOptions[0];
        RefreshHistory();

        StatusText = ScriptOptions.Count == 0
            ? "rules 目录里还没有脚本 —— 去「抓取规则」页新建，或等首次运行播种预设。"
            : "选脚本 → 填起始网址 →「🔗 抓链接」→ 勾行 →「下载」。";
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

}
