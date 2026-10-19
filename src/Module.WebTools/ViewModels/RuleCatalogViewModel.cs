using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 抓取规则 · **规则目录**（`web-scrape`，NT-084 批 A/B）。
/// <para>
/// 🔴 本页只管**目录**：列出 <c>&lt;DataDir&gt;\web-tools\rules\*.py</c>，做新建 / 删除 / 启停 / 重读。
/// 规则的编辑与调试在**另一页**（`web-rule-editor`「规则编辑器」，批 C）—— 本页 ⛔ 不放命令列表与 HTML 面板。
/// </para>
/// <para>
/// 🔴 类名刻意不叫 <c>ScrapeViewModel</c>（旧实现已删）；页 Key 仍是 <c>web-scrape</c>（落 columnlayout，⛔ 不能改）。
/// </para>
/// </summary>
public partial class RuleCatalogViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条（NT-055） ====================
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "➕ 新建规则集", Classes = "ghost", CommandPath = nameof(NewRuleSetCommand) },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索名称 / 域名…", ValuePath = nameof(SearchText) },
        new() { Text = "✓ 切换启用", CommandPath = nameof(ToggleEnabledCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Text = "🔄 重读规则", CommandPath = nameof(RefreshCommand), ToolTip = "重新扫一遍规则目录（⛔ 不运行任何规则集）" }
    };

    private readonly RuleCatalogService _catalog = null!;
    private readonly RuleFileService _files = null!;
    private readonly WebTransferService _transfer = null!;
    private readonly INavigationService? _nav;
    private readonly List<RuleSetMeta> _all = new();

    public ObservableCollection<RuleSetMeta> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private RuleSetMeta? _selected;
    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _baseUrlHeader = "域名";
    [ObservableProperty] private string _enabledHeader = "状态";
    [ObservableProperty] private string _lastRunHeader = "最近运行";

    // 列显隐（真正的状态在基类，落 config\columnlayout.json）
    public bool IsNameVisible => Vis("Name");
    public bool IsBaseUrlVisible => Vis("BaseUrl");
    public bool IsEnabledVisible => Vis("Enabled");
    public bool IsLastRunVisible => Vis("LastRun");

    protected override string PageKey => "web-scrape";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2*", sample: "示例站"),
        new("BaseUrl", "域名", "3*", sample: "https://example.com"),
        new("Enabled", "状态", "1*", sample: "✓ 启用"),
        new("LastRun", "最近运行", "1.4*", sample: "2026-10-19 21:00"),
    };

    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>详情卡数据源。</summary>
    public RuleSetMeta? DetailItem => Selected;

    /// <summary>详情卡正文是否显示。</summary>
    public bool ShowDetailBody => HasSelection;

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;
    public event EventHandler? DetailChanged;

    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    // ==================== 详情卡（声明式，NT-054） ====================

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧选中一条规则集",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField { Kind = FieldKind.Hint, Prefix = "名称：", ValuePath = "DetailItem.Name" },
                    new DetailField { Kind = FieldKind.Hint, Prefix = "域名：", ValuePath = "DetailItem.BaseUrl" },
                    new DetailField { Kind = FieldKind.Hint, Prefix = "编码：", ValuePath = "DetailItem.Encoding" },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(RunSummary), VisibleWhen = nameof(ShowDetailBody) },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(FileSummary), VisibleWhen = nameof(ShowDetailBody) },
                    new DetailField
                    {
                        Label = "文件路径",
                        ValuePath = "DetailItem.FilePath",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "✏ 编辑规则",
                Classes = "primary",
                ToolTip = "打开规则编辑器（命令列表 + HTML 源码面板）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = EditRuleSetCommand
            },
            new DetailAction
            {
                Text = "📝 打开文件",
                Classes = "ghost",
                ToolTip = "用记事本打开 .py 规则文件（⛔ 不用「运行方式」，避免双击就执行）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = OpenFileCommand
            },
            new DetailAction
            {
                Text = "📂 打开目录",
                Classes = "ghost",
                ToolTip = "在资源管理器里打开规则目录",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = OpenFolderCommand
            },
            new DetailAction
            {
                Text = "🗑 删除",
                Classes = "ghost",
                ToolTip = "删除这条规则集的文件（不可恢复）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = DeleteCommand
            }
        }
    };

    /// <summary>详情卡「最近运行」行。</summary>
    public string RunSummary => DetailItem?.LastRunSummary ?? "";

    /// <summary>详情卡「文件」行。</summary>
    public string FileSummary => DetailItem?.FileSummary ?? "";

    private void NotifyDetail()
    {
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(FileSummary));
    }

    // ==================== 构造 ====================

    public RuleCatalogViewModel() { }

    public RuleCatalogViewModel(RuleCatalogService catalog, RuleFileService files,
        IColumnLayoutService cols, WebTransferService transfer, INavigationService? nav = null)
        : base(cols)
    {
        _catalog = catalog;
        _files = files;
        _transfer = transfer;
        _nav = nav;
        LoadLayout();
        Refresh();

        // 跨页「🕷 发送到网页抓取」：消费待处理 URL，建一条以它命名的规则集（⛔ 不弹窗）。
        var pending = _transfer.TakeUrl();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            var name = SuggestNameFromUrl(pending);
            if (_files.Create(name, pending))
            {
                Refresh();
                Selected = Items.FirstOrDefault(i => i.Name == name);
                StatusText = $"已从其他页收到网址，建好规则集「{name}」，去编辑器补命令。";
                return;
            }
        }

        StatusText = $"共 {Items.Count} 条规则集";
    }

    /// <summary>从 URL 猜一个规则集名（域名去掉 www）；失败退化为「新规则集」。</summary>
    private static string SuggestNameFromUrl(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
            return RuleFileService.SafeName(string.IsNullOrWhiteSpace(host) ? "新规则集" : host);
        }
        catch { return "新规则集"; }
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void Refresh()
    {
        _all.Clear();
        foreach (var m in _catalog.Load()) _all.Add(m);
        ApplyView();
    }

    [RelayCommand]
    private void NewRuleSet()
    {
        var name = "新规则集";
        for (var i = 2; _files.Exists(name); i++) name = "新规则集" + i;

        if (!_files.Create(name, "https://"))
        {
            StatusText = "新建失败（文件已存在或目录不可写）。";
            return;
        }

        Refresh();
        Selected = Items.FirstOrDefault(i => i.Name == name);
        IsDetailVisible = true;
        SaveDetailFlag();
        StatusText = "已新建规则集，点「📝 打开文件」填域名与命令。";
    }

    [RelayCommand]
    private void ToggleEnabled()
    {
        if (Selected == null) { StatusText = "先选中一条规则集。"; return; }

        Selected.Enabled = !Selected.Enabled;
        PersistMeta();
        ApplyView();
        StatusText = Selected.Enabled ? $"已启用「{Selected.Name}」。" : $"已停用「{Selected.Name}」。";
    }

    /// <summary>跳「规则编辑器」并带上当前规则集（经 <see cref="WebTransferService"/> 传参）。</summary>
    [RelayCommand]
    private void EditRuleSet()
    {
        if (Selected == null) return;
        _transfer.PushRuleSet(Selected.Name);
        _nav?.NavigateTo("web-rule-editor");
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (Selected == null) return;
        try
        {
            // 🔴 明确用记事本：`ShellExecute` 打开 .py 会**执行脚本**，不是编辑
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Selected.FilePath + "\"")
            {
                UseShellExecute = false
            });
            StatusText = "已用记事本打开，改完回来点「🔄 重读规则」。";
        }
        catch (Exception ex) { StatusText = "⚠ 打不开文件：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_files.RulesDir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "⚠ 打不开目录：" + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected == null) return;

        var name = Selected.Name;
        _catalog.Delete(name);
        _all.RemoveAll(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        Selected = null;
        ApplyView();
        await Task.CompletedTask;
        StatusText = $"已删除「{name}」。";
    }

    private void PersistMeta() => _catalog.Save(_all);

    // ==================== 列表 ====================

    private void ApplyView()
    {
        IEnumerable<RuleSetMeta> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || t.BaseUrl.Contains(text, StringComparison.OrdinalIgnoreCase));

        q = ApplyFilters(q);

        q = SortDescending
            ? q.OrderByDescending(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            : q.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);

        Items.Clear();
        foreach (var m in q.ToList()) Items.Add(m);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders() => NameHeader = "名称" + SortArrow("Name");

    partial void OnSelectedChanged(RuleSetMeta? value)
    {
        OnPropertyChanged(nameof(DetailItem));
        NotifyDetail();
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnSearchTextChanged(string value) => ApplyView();
}
