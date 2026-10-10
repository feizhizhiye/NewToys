using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public partial class ScrapeViewModel : ListPageViewModelBase
{
    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly ScrapeService _svc = null!;
    private readonly WebTransferService _transfer = null!;
    private readonly List<ScrapeTask> _all = new();

    public IReadOnlyList<string> RuleTypes { get; } = new List<string> { "Css", "Regex" };

    public ObservableCollection<ScrapeTask> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "就绪";
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
        new("Name", "名称", "2*"),
        new("Url", "起始网址", "3*"),
        new("Rule", "规则", "2*"),
        new("LastRun", "最近运行", "1.4*"),
        new("Detail", "", "36", sortable: false),
    };

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

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
        finally { IsBusy = false; ApplyView(); }
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

    partial void OnSelectedChanged(ScrapeTask? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnSearchTextChanged(string value) => ApplyView();
}
