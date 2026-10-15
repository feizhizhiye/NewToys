using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 网页抓取 / 爬虫管理器（列表 + 详情）。
/// <para>
/// 每条任务 = 多配置抓取（起始网址 / 规则 / UA / JS 整合脚本）。运行后把抓取结果经 JS 整合脚本产出最终输出。
/// 列表管理多个任务；右键「运行 / 发送到书签」。列显隐 / 详情开合偏好落 <c>config\columnlayout.json</c>。
/// </para>
/// </summary>
public partial class ScrapeViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条（NT-055 / 需求 §4.8.5） ====================
    // 顺序 = 优先级顺序（越靠前越不会被折进「⋯」）；Shell 的 ToolbarHost 统一渲染。
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "➕ 新建任务", Classes = "ghost", CommandPath = nameof(NewTaskCommand) },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索名称 / 网址 / 规则…", ValuePath = nameof(SearchText) },
        // v3.60：原 Label「右键列表项：…」删除（工具条⛔ 不放说明文字）—— 右键能力本来就写在各右键菜单项上。
        new() { Text = "▶ 运行全部", CommandPath = nameof(RunAllCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Text = "🔄 重读任务", CommandPath = nameof(RefreshCommand), ToolTip = "重新读一遍本地的抓取任务配置（⛔ 不开始抓取）" }
    };

    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly ScrapeService _svc = null!;
    private readonly WebTransferService _transfer = null!;
    private readonly List<ScrapeTask> _all = new();

    public IReadOnlyList<string> RuleTypes { get; } = new List<string> { "Css", "Regex" };

    public ObservableCollection<ScrapeTask> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;
    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private ScrapeTask? _selected;
    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _urlHeader = "起始网址";
    [ObservableProperty] private string _ruleHeader = "规则";
    [ObservableProperty] private string _lastRunHeader = "最近运行";

    // ============ 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase（v1.35 收口） ============
    //
    // XAML 只能绑具名属性 → 每列给一个直通属性（真正的状态在基类里，落 config\columnlayout.json）。

    public bool IsNameVisible => Vis("Name");
    public bool IsUrlVisible => Vis("Url");
    public bool IsRuleVisible => Vis("Rule");
    public bool IsLastRunVisible => Vis("LastRun");

    protected override string PageKey => "web-scrape";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；最后一列 Detail 是「▤」按钮的固定窄列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // v3.22 §4.8.1：每列给一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）
        new("Name", "名称", "2*", sample: "示例抓取任务"),
        new("Url", "起始网址", "3*", sample: "https://example.com/list"),
        new("Rule", "规则", "2*", sample: "Css · .item > a"),
        new("LastRun", "最近运行", "1.4*", sample: "2026-10-13 15:00 · 18 条"),
    };

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>详情卡的数据源：有选中项时显示它（v3.60 §4.8.1：⛔ 没选中不填示例值）。</summary>
    public ScrapeTask? DetailItem => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
    // ==================== 详情面板 + 声明式详情卡（NT-054 的 L2 迁移页，需求 §4.5.1） ====================

    /// <summary>「运行结果」块是否要显示（原 XAML 用 <c>ObjectConverters.IsNotNull</c>，描述符只认布尔路径）。</summary>
    public bool HasLastOutput => !string.IsNullOrEmpty(DetailItem?.LastOutput);

    /// <summary>「最近运行：N 条」（原 XAML 走 <c>StringFormat</c> 带后缀，描述符不支持格式化 ⇒ 这里先拼好）。</summary>
    public string LastRunSummary => DetailItem is null ? "" : $"最近运行：{DetailItem.LastCount} 条";

    /// <summary>运行时间展示串（同上）。</summary>
    public string LastRunAtDisplay => DetailItem?.LastRunAt is { } t && t != default ? t.ToString("yyyy-MM-dd HH:mm") : "";

    private void NotifyLastInfo()
    {
        OnPropertyChanged(nameof(HasLastOutput));
        OnPropertyChanged(nameof(LastRunSummary));
        OnPropertyChanged(nameof(LastRunAtDisplay));
    }

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    /// <summary>工具条末位的详情开关 → 走本命令（⛔ 基类 <c>ToggleDetailCommand</c> 不通知 Shell，右列不会跟着开合）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的页内自绘卡片对应：任务名 / 起始网址 / 规则类型 / 规则 / UA / 整合脚本 + 运行结果块 + 三个卡内动作。</para>
    /// <para>⚠️ 迁移降级：原页内卡是 <b>420px 宽</b>（要放规则 / 脚本），Shell 右列固定 340px。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一个任务",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "任务名",
                        ValuePath = "DetailItem.Name",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "起始网址",
                        ValuePath = "DetailItem.StartUrl",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "抓取规则类型",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(RuleTypes),
                        ValuePath = "DetailItem.RuleType"
                    },
                    new DetailField
                    {
                        Label = "抓取规则（CSS 选择器如 div.news a；或正则）",
                        ValuePath = "DetailItem.Rule",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true
                    },
                    new DetailField
                    {
                        Label = "User-Agent（留空用默认）",
                        ValuePath = "DetailItem.UserAgent",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "整合脚本（JavaScript）：变量 items 为抓取到的字符串数组，返回最终文本",
                        ValuePath = "DetailItem.JsScript",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        MultiLine = true,
                        Rows = 90,
                        Placeholder = "留空 = 直接拼接。示例：items.map(s=>s.trim()).filter(Boolean).join('\\n')"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(LastRunSummary),
                        VisibleWhen = nameof(HasLastOutput)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Prefix = "运行时间：",
                        ValuePath = nameof(LastRunAtDisplay),
                        VisibleWhen = nameof(HasLastOutput)
                    },
                    new DetailField
                    {
                        Label = "输出",
                        ValuePath = "DetailItem.LastOutput",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true,
                        VisibleWhen = nameof(HasLastOutput)
                    }
                }
            }
        },
        Actions =
        {
            // 原来这三个按钮是页内卡里的 WrapPanel；声明式卡里进 Actions 区。
            new DetailAction
            {
                Text = "▶ 运行",
                Classes = "primary",
                ToolTip = "按当前配置跑一次抓取",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = RunCommand
            },
            new DetailAction
            {
                Text = "💾 保存",
                Classes = "ghost",
                ToolTip = "把当前配置写回任务清单",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = SaveCommand
            },
            new DetailAction
            {
                Text = "🗑 删除",
                Classes = "ghost",
                ToolTip = "删除这个抓取任务",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = DeleteCommand
            }
        }
    };

    public ScrapeViewModel() { }

    public ScrapeViewModel(ScrapeService svc, IColumnLayoutService cols, WebTransferService transfer)
        : base(cols)
    {
        _svc = svc;
        _transfer = transfer;
        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        Refresh();

        // 跨页「发送到此页」：消费待处理 URL，建一条以它为起始网址的任务并打开详情。
        var pending = _transfer.TakeUrl();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            var draft = new ScrapeTask { Name = "来自其他页", StartUrl = pending };
            _all.Add(draft);
            Items.Add(draft);
            Selected = draft;
            IsDetailVisible = true;
            SaveDetailFlag();
            StatusText = "已从其他页收到网址，配置规则后点「运行」。";
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        _all.Clear();
        foreach (var t in _svc.Load()) _all.Add(t);
        ApplyView();
        StatusText = $"共 {Items.Count} 个抓取任务";
    }

    [RelayCommand]
    private void NewTask()
    {
        var t = new ScrapeTask { Name = "新任务", StartUrl = "https://", Rule = "a" };
        _all.Add(t);
        Items.Add(t);
        Selected = t;
        IsDetailVisible = true;
        SaveDetailFlag();
        StatusText = "已新建任务，配置后点「运行」。";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected == null) return;
        if (string.IsNullOrWhiteSpace(Selected.StartUrl)) { StatusText = "起始网址不能为空。"; return; }
        await PersistAsync();
        StatusText = $"已保存「{Selected.Name}」。";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected == null) return;
        _all.Remove(Selected);
        Items.Remove(Selected);
        await PersistAsync();
        Selected = null;
        ApplyView();
        StatusText = "已删除任务。";
    }

    private async Task PersistAsync() => await Task.Run(() => _svc.Save(_all.ToList()));

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Selected == null) return;
        await RunOneAsync(Selected);
        ApplyView();
        NotifyLastInfo();
    }

    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (Items.Count == 0) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在运行 {Items.Count} 个抓取任务……";
            var sem = new System.Threading.SemaphoreSlim(4);
            await Task.WhenAll(Items.Select(async t =>
            {
                await sem.WaitAsync();
                try { await RunOneAsync(t); }
                finally { sem.Release(); }
            }));
            StatusText = "全部任务运行完毕。";
        }
        finally { IsBusy = false; ApplyView(); NotifyLastInfo(); }
    }

    private async Task RunOneAsync(ScrapeTask t)
    {
        var (ok, output, error, count) = await _svc.RunAsync(t).ConfigureAwait(false);
        t.LastOutput = ok ? output : "错误：" + error;
        t.LastCount = count;
        t.LastRunAt = DateTime.UtcNow;
    }

    [RelayCommand]
    private void SendToBookmark()
    {
        if (Selected == null) return;
        _transfer.PushUrl(Selected.StartUrl);
        StatusText = "已暂存起始网址，切到书签页即可保存。";
    }

    private void ApplyView()
    {
        IEnumerable<ScrapeTask> q = _all;
        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || t.StartUrl.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || t.Rule.Contains(text, StringComparison.OrdinalIgnoreCase));

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        q = SortDescending
            ? q.OrderByDescending(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            : q.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);

        var ordered = q.ToList();
        Items.Clear();
        foreach (var t in ordered) Items.Add(t);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
    }

    partial void OnSelectedChanged(ScrapeTask? value)
    {
        OnPropertyChanged(nameof(DetailItem));
        NotifyLastInfo();       // 切选中项 → 「运行结果」块跟着换
        OnPropertyChanged(nameof(HasSelection));
    }
    partial void OnSearchTextChanged(string value) => ApplyView();
}
