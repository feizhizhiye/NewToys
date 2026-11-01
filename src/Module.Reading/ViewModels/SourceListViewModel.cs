using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Reading.Models;
using NewToys.Reading.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Reading.ViewModels;

/// <summary>书源列表的一行（把 <see cref="BookSource"/> 摊平成列要显示的字段）。</summary>
public sealed class SourceRow
{
    public SourceRow(BookSource source) => Source = source;

    public BookSource Source { get; }

    public string Name => Source.Name;

    public string Group => Source.GroupText;

    public string Type => Source.TypeText;

    public string State => Source.CapabilityText;

    public string LastUsed => SourceTimeText.Of(Source.LastUsedAt);

    public string EnabledText => Source.Enabled ? "已启用" : "已停用";

    /// <summary>悬停提示：能力档的具体原因（规则缺什么 / 哪一段要 JS）。</summary>
    public string Tip => string.IsNullOrWhiteSpace(Source.CapabilityReason) ? Source.Url : Source.CapabilityReason;

    public bool IsEnabled => Source.Enabled;
}

/// <summary>
/// 「阅读 → 书源」页（<c>reading-sources</c>）：书源清单 + 导入导出 + 详情卡。
/// <para>
/// 状态列是**导入时静态判定**的三档（可用 / 需 JS / 不完整），⛔ 不发请求 —— 用户先看清哪些源现在能跑。
/// </para>
/// </summary>
public partial class SourceListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly SourceStore _store;
    private readonly SourceImportService _import;
    private readonly ReadingSession _session;
    private readonly INavigationService? _nav;

    private readonly List<SourceRow> _all = new();
    private IReadOnlyList<ToolbarItemDescriptor>? _toolbar;

    [ObservableProperty] private SourceRow? _selected;
    [ObservableProperty] private bool _isBusy;

    private string _searchQuery = "";

    /// <summary>工具条搜索框：按 名称 / 分组 / 状态 / 地址 过滤（即时生效，⛔ 不落盘）。</summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            var text = value ?? "";
            if (_searchQuery == text) return;
            _searchQuery = text;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public SourceListViewModel() : base(null, null)
    {
        _store = null!;
        _import = null!;
        _session = new ReadingSession();
        _nav = null;
    }

    public SourceListViewModel(
        SourceStore store,
        SourceImportService import,
        ReadingSession session,
        INavigationService? nav = null,
        IColumnLayoutService? cols = null,
        IOptionsStore? opts = null)
        : base(cols, opts)
    {
        _store = store;
        _import = import;
        _session = session;
        _nav = nav;

        LoadLayout();
        Reload();
    }

    // ==================== 列表 ====================

    public ObservableCollection<SourceRow> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    public bool HasSelection => Selected is not null;

    public bool ShowDetailBody => Selected is not null;

    public bool IsNotBusy => !IsBusy;

    protected override string PageKey => "reading-sources";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "书源", "240", sample: "某某小说网（优）"),
        new("Group", "分组", "150", sample: "综合；小说"),
        new("Type", "类型", "70", sample: "文本"),
        new("State", "状态", "90", sample: "需 JS"),
        new("LastUsed", "上次使用", "110", sample: "3 天前"),
    };

    public bool IsNameVisible => Vis("Name");
    public bool IsGroupVisible => Vis("Group");
    public bool IsTypeVisible => Vis("Type");
    public bool IsStateVisible => Vis("State");
    public bool IsLastUsedVisible => Vis("LastUsed");

    protected override void RefreshView() => ApplyFilter();

    protected override void EnsureDetailSelection()
    {
        if (Selected is null && Items.Count > 0) Selected = Items[0];
    }

    private void ApplyFilter()
    {
        var key = (_searchQuery ?? "").Trim();

        var rows = key.Length == 0
            ? _all
            : _all.Where(r =>
                r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)
                || r.Group.Contains(key, StringComparison.OrdinalIgnoreCase)
                || r.State.Contains(key, StringComparison.OrdinalIgnoreCase)
                || r.Source.Url.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();

        var keep = Selected?.Source.Url;

        Items.Clear();
        foreach (var row in rows) Items.Add(row);

        OnPropertyChanged(nameof(HasItems));
        if (keep is not null) Selected = Items.FirstOrDefault(r => r.Source.Url == keep);
        if (Selected is null && Items.Count > 0 && IsDetailVisible) Selected = Items[0];
    }

    partial void OnSelectedChanged(SourceRow? value)
    {
        OnPropertyChanged("HasSelection");
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailName));
        OnPropertyChanged(nameof(DetailGroup));
        OnPropertyChanged(nameof(DetailType));
        OnPropertyChanged(nameof(DetailState));
        OnPropertyChanged(nameof(DetailReason));
        OnPropertyChanged(nameof(DetailLastUsed));
        OnPropertyChanged(nameof(DetailSearchUrl));
        OnPropertyChanged(nameof(DetailUrl));
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged("IsNotBusy");
        NotifyToolbarChanged();
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void Reload()
    {
        _all.Clear();
        if (_store is not null)
        {
            foreach (var source in _store.LoadAll()) _all.Add(new SourceRow(source));
        }

        ApplyFilter();
        StatusText = BuildStatus();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_import is null) return;

        var files = await DialogHooks.PickFilesAsync("选择 legado 书源 JSON", null, false, "*.json").ConfigureAwait(true);
        if (files.Count == 0) return;

        IsBusy = true;
        try
        {
            var result = _import.ImportFile(files[0]);
            Reload();
            StatusText = result.HasErrors ? "⚠ " + result.Summary : result.Summary;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var rows = SelectedRows();
        if (rows.Count == 0) { StatusText = "先选中要导出的书源。"; return; }

        var path = await DialogHooks.SaveFileAsync("导出书源", null, "book-sources.json", "*.json").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.WriteAllText(path!, _import.Export(rows.Select(r => r.Source)), new System.Text.UTF8Encoding(true));
            StatusText = $"已导出 {rows.Count} 条到 {path}";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 导出失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void Enable()
    {
        var rows = SelectedRows();
        if (rows.Count == 0) { StatusText = "先选中要启用的书源。"; return; }

        _store.SetEnabled(rows.Select(r => r.Source.Url), true);
        Reload();
        StatusText = $"已启用 {rows.Count} 条书源";
    }

    [RelayCommand]
    private void Disable()
    {
        var rows = SelectedRows();
        if (rows.Count == 0) { StatusText = "先选中要停用的书源。"; return; }

        _store.SetEnabled(rows.Select(r => r.Source.Url), false);
        Reload();
        StatusText = $"已停用 {rows.Count} 条书源";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var rows = SelectedRows();
        if (rows.Count == 0) { StatusText = "先选中要删除的书源。"; return; }

        var confirmed = await DialogHooks.ConfirmAsync($"确定删除选中的 {rows.Count} 条书源？删除后要重新导入才能恢复。").ConfigureAwait(true);
        if (!confirmed) return;

        var deleted = _store.Delete(rows.Select(r => r.Source.Url));
        Selected = null;
        Reload();
        StatusText = $"已删除 {deleted} 条书源";
    }

    [RelayCommand]
    private void OpenSearch()
    {
        if (Selected is null) { StatusText = "先选中一个书源。"; return; }

        _session.PendingSourceUrl = Selected.Source.Url;
        _nav?.NavigateTo("reading-search");
    }

    private List<SourceRow> SelectedRows()
        => Selected is null ? new List<SourceRow>() : new List<SourceRow> { Selected };

    private string BuildStatus()
    {
        if (_all.Count == 0) return "还没有书源 —— 点工具条「📥 导入书源」选一个 legado 书源 JSON。";

        var ready = _all.Count(r => r.Source.Capability == SourceCapability.Ready);
        var js = _all.Count(r => r.Source.Capability == SourceCapability.NeedsJs);
        var incomplete = _all.Count(r => r.Source.Capability == SourceCapability.Incomplete);
        var enabled = _all.Count(r => r.IsEnabled);

        return $"共 {_all.Count} 条 · 可用 {ready} / 需 JS {js} / 不完整 {incomplete} · 启用 {enabled}";
    }

    // ==================== 工具条 ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => _toolbar ??= new List<ToolbarItemDescriptor>
    {
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "📥 导入书源",
            Classes = "primary",
            CommandPath = "ImportCommand",
            ToolTip = "导入 legado 书源 JSON（顶层数组或 {bookSources:[…]} 都认）",
            IsEnabledPath = "IsNotBusy",
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Placeholder = "搜索书源名称 / 分组 / 地址",
            ValuePath = "SearchQuery",
            Width = 240,
        },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "✅ 启用",
            CommandPath = "EnableCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "启用选中的书源",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "⏸ 停用",
            CommandPath = "DisableCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "停用选中的书源",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "📤 导出选中",
            CommandPath = "ExportCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "把选中书源导成 JSON（原样回转）",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "🗑 删除",
            Classes = "danger-btn",
            CommandPath = "DeleteCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "从库里删掉选中书源",
        },
    };

    // ==================== 详情面板 + 详情卡 ====================

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

    // ==================== 详情卡绑定代理 ====================

    public string DetailName => Selected?.Source.Name ?? "";

    public string DetailGroup => Selected?.Source.GroupText ?? "";

    public string DetailType => Selected?.Source.TypeText ?? "";

    public string DetailState => Selected is null ? "" : $"{Selected.Source.CapabilityText} · {Selected.EnabledText}";

    public string DetailReason => Selected?.Source.CapabilityReason ?? "";

    public string DetailLastUsed => Selected is null ? "" : SourceTimeText.Of(Selected.Source.LastUsedAt);

    public string DetailSearchUrl => Selected?.Source.SearchUrl ?? "";

    public string DetailUrl => Selected?.Source.Url ?? "";

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧选中一条书源，或点工具条「📥 导入书源」。导入后每条源会标出「可用 / 需 JS / 不完整」。",
        Sections =
        {
            new DetailSection
            {
                Header = "书源",
                Fields =
                {
                    new DetailField { Label = "名称", ValuePath = nameof(DetailName), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "分组", ValuePath = nameof(DetailGroup), Kind = FieldKind.Text },
                    new DetailField { Label = "类型", ValuePath = nameof(DetailType), Kind = FieldKind.Text },
                    new DetailField { Label = "状态", ValuePath = nameof(DetailState), Kind = FieldKind.Text },
                    new DetailField { Label = "上次使用", ValuePath = nameof(DetailLastUsed), Kind = FieldKind.Text },
                }
            },
            new DetailSection
            {
                Header = "地址",
                Fields =
                {
                    new DetailField { Label = "书源地址", ValuePath = nameof(DetailUrl), Kind = FieldKind.Text, Selectable = true, Mono = true },
                    new DetailField { Label = "搜索地址", ValuePath = nameof(DetailSearchUrl), Kind = FieldKind.Text, Selectable = true, Mono = true, MultiLine = true, Rows = 48 },
                }
            },
            new DetailSection
            {
                Header = "为什么是这个状态",
                Fields =
                {
                    new DetailField { Label = "判定", ValuePath = nameof(DetailReason), Kind = FieldKind.Text, MultiLine = true, Rows = 44, Selectable = true },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "判定只看搜索 / 目录 / 正文三段的规则：缺关键字段 = 不完整；含 @js: 或 <js> = 需 JS（等后续批次的 JS 兼容层）。",
                    },
                }
            }
        }
    };
}
