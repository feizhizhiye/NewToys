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
public partial class ScrapeViewModel : ObservableObject
{
    private const string PageKey = "web-scrape";
    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly ScrapeService _svc = null!;
    private readonly IColumnLayoutService? _cols;
    private readonly WebTransferService _transfer = null!;
    private readonly List<ScrapeTask> _all = new();
    private string _sortColumn = "Name";
    private bool _sortDescending;

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

    [ObservableProperty] private bool _isNameVisible = true;
    [ObservableProperty] private bool _isUrlVisible = true;
    [ObservableProperty] private bool _isRuleVisible = true;
    [ObservableProperty] private bool _isLastRunVisible = true;

    public IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2*"),
        new("Url", "起始网址", "3*"),
        new("Rule", "规则", "2*"),
        new("LastRun", "最近运行", "1.4*"),
        new("Detail", "", "36", sortable: false),
    };

    public string VisibleColumnSpecs
    {
        get
        {
            var parts = new string[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
                parts[i] = IsVisibleFor(Columns[i].Key) ? Columns[i].Width : "0";
            return string.Join(",", parts);
        }
    }

    private bool IsVisibleFor(string key) => key switch
    {
        "Name" => IsNameVisible, "Url" => IsUrlVisible, "Rule" => IsRuleVisible, "LastRun" => IsLastRunVisible, _ => true
    };

    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (key == null) return;
        switch (key)
        {
            case "Name": IsNameVisible = !IsNameVisible; break;
            case "Url": IsUrlVisible = !IsUrlVisible; break;
            case "Rule": IsRuleVisible = !IsRuleVisible; break;
            case "LastRun": IsLastRunVisible = !IsLastRunVisible; break;
            default: return;
        }
        SaveColumnVisibility();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    private void SaveColumnVisibility()
    {
        _cols?.SavePage(PageKey, new Dictionary<string, bool>
        {
            ["Name"] = IsNameVisible, ["Url"] = IsUrlVisible, ["Rule"] = IsRuleVisible, ["LastRun"] = IsLastRunVisible
        });
    }

    private void LoadColumnVisibility()
    {
        var saved = _cols?.LoadPage(PageKey);
        if (saved == null) return;
        if (saved.TryGetValue("Name", out var n)) IsNameVisible = n;
        if (saved.TryGetValue("Url", out var u)) IsUrlVisible = u;
        if (saved.TryGetValue("Rule", out var r)) IsRuleVisible = r;
        if (saved.TryGetValue("LastRun", out var l)) IsLastRunVisible = l;
    }

    [ObservableProperty] private bool _isDetailVisible;

    [RelayCommand]
    private void ToggleDetail()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible && Selected == null && Items.Count > 0) Selected = Items[0];
        SaveDetailFlag();
    }

    [RelayCommand]
    private void ShowDetail()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
        IsDetailVisible = true;
        SaveDetailFlag();
    }

    private void SaveDetailFlag() => _cols?.SaveFlag(PageKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    public ScrapeViewModel() { }

    public ScrapeViewModel(ScrapeService svc, IColumnLayoutService cols, WebTransferService transfer)
    {
        _svc = svc;
        _cols = cols;
        _transfer = transfer;
        LoadColumnVisibility();
        IsDetailVisible = cols.LoadFlag(PageKey, IColumnLayoutService.FlagDetailPanel);
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

    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase)) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }
        ApplyView();
    }

    private void ApplyView()
    {
        IEnumerable<ScrapeTask> q = _all;
        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || t.StartUrl.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || t.Rule.Contains(text, StringComparison.OrdinalIgnoreCase));

        q = _sortDescending
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
        var arrow = _sortDescending ? " ▼" : " ▲";
        NameHeader = "名称" + (_sortColumn == "Name" ? arrow : "");
    }

    partial void OnSelectedChanged(ScrapeTask? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnSearchTextChanged(string value) => ApplyView();
}
