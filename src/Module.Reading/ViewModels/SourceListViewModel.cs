using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
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

    /// <summary>校验档（列表列 / 筛选）。</summary>
    public string Check => Source.CheckText;

    /// <summary>悬停提示：能力档的具体原因（规则缺什么 / 哪一段要 JS）。</summary>
    public string Tip => string.IsNullOrWhiteSpace(Source.CapabilityReason) ? Source.Url : Source.CapabilityReason;

    public bool IsEnabled => Source.Enabled;
}

/// <summary>
/// 「网络阅读 → 书源」页（<c>reading-sources</c>）：书源清单 + 导入导出 + 详情卡。
/// <para>
/// 状态列是**导入时静态判定**的三档（可用 / 需 WebView / 不完整），⛔ 不发请求 —— 用户先看清哪些源现在能跑。
/// </para>
/// </summary>
public partial class SourceListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private const string AllGroups = "全部分组";
    private const string AllTypes = "全部类型";
    private const string AllChecks = "全部校验";

    /// <summary>类型筛选项（与 <see cref="BookSource.TypeText"/> 一一对应）。</summary>
    public IReadOnlyList<string> TypeOptions { get; } = new[] { AllTypes, "文本", "图片", "音频", "视频" };

    /// <summary>校验档筛选项（与 <see cref="BookSource.CheckText"/> 一一对应）。</summary>
    public IReadOnlyList<string> CheckOptions { get; } = new[] { AllChecks, "未校验", "通过", "无结果", "失败", "跳过" };

    /// <summary>分组筛选项（<c>全部分组</c> + 库里实际出现过的分组，导入 / 改分组后重建）。</summary>
    public ObservableCollection<string> GroupOptions { get; } = new() { AllGroups };

    private readonly SourceStore _store;
    private readonly SourceImportService _import;
    private readonly ReadingSession _session;
    private readonly INavigationService? _nav;
    private readonly SourceDebugService? _debug;
    private readonly SourceCheckService? _check;

    private readonly List<SourceRow> _all = new();
    private IReadOnlyList<ToolbarItemDescriptor>? _toolbar;

    [ObservableProperty] private SourceRow? _selected;
    [ObservableProperty] private bool _isBusy;

    /// <summary>书源 JSON 编辑浮层是否展开。</summary>
    [ObservableProperty] private bool _isEditorOpen;

    /// <summary>编辑器里的原始 JSON。</summary>
    [ObservableProperty] private string _editorJson = "";

    /// <summary>编辑器底部状态（解析结果的提示）。</summary>
    [ObservableProperty] private string _editorStatus = "";

    /// <summary>正在编辑哪条书源（标题用）。</summary>
    [ObservableProperty] private string _editorTitle = "";

    /// <summary>书源试跑浮层是否展开。</summary>
    [ObservableProperty] private bool _isDebugOpen;

    /// <summary>试跑浮层标题（带书源名）。</summary>
    [ObservableProperty] private string _debugTitle = "";

    /// <summary>试跑用的关键词。</summary>
    [ObservableProperty] private string _debugKeyword = "";

    /// <summary>试跑过程中的阶段提示。</summary>
    [ObservableProperty] private string _debugPhase = "";

    /// <summary>试跑中的步骤结果（搜索 / 详情 / 目录 / 正文）。</summary>
    public ObservableCollection<DebugStep> DebugSteps { get; } = new();

    private CancellationTokenSource? _debugCts;

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

        BatchSelection.CollectionChanged += (_, _) => NotifyBatchChanged();
    }

    public SourceListViewModel(
        SourceStore store,
        SourceImportService import,
        ReadingSession session,
        INavigationService? nav = null,
        IColumnLayoutService? cols = null,
        IOptionsStore? opts = null,
        SourceDebugService? debug = null,
        SourceCheckService? check = null)
        : base(cols, opts)
    {
        _store = store;
        _import = import;
        _session = session;
        _nav = nav;
        _debug = debug;
        _check = check;

        BatchSelection.CollectionChanged += (_, _) => NotifyBatchChanged();

        LoadLayout();
        Reload();
    }

    // ==================== 筛选 / 校验（页内筛选条） ====================

    private string _typeFilter = AllTypes;
    private string _groupFilter = AllGroups;
    private string _checkFilter = AllChecks;

    /// <summary>类型筛选（文本 / 图片 / 音频 / 视频）。</summary>
    public string TypeFilter
    {
        get => _typeFilter;
        set
        {
            var text = string.IsNullOrWhiteSpace(value) ? AllTypes : value;
            if (_typeFilter == text) return;
            _typeFilter = text;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>分组筛选（库里出现过的分组 + 未分组）。</summary>
    public string GroupFilter
    {
        get => _groupFilter;
        set
        {
            var text = string.IsNullOrWhiteSpace(value) ? AllGroups : value;
            if (_groupFilter == text) return;
            _groupFilter = text;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>校验档筛选（未校验 / 通过 / 无结果 / 失败）。</summary>
    public string CheckFilter
    {
        get => _checkFilter;
        set
        {
            var text = string.IsNullOrWhiteSpace(value) ? AllChecks : value;
            if (_checkFilter == text) return;
            _checkFilter = text;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>筛选命中条数（筛选条右侧的「N / 共 M」）。</summary>
    public string FilterSummary => Items.Count == _all.Count
        ? $"共 {_all.Count} 条"
        : $"筛出 {Items.Count} / 共 {_all.Count} 条";

    // ==================== 多选（批量作用域） ====================

    /// <summary>
    /// 批量命令的作用域 = 列表当前选中的行（Ctrl / Shift 多选）。
    /// <para>与 <c>TableView.SelectedItems</c> 双向绑定：控件侧多选直接改它，反向「全选 / 反选」也只改它。</para>
    /// </summary>
    public ObservableCollection<SourceRow> BatchSelection { get; } = new();

    /// <summary>选中条数（工具条 / 右键菜单的可用态与文案都看它）。</summary>
    public int BatchCount => BatchSelection.Count;

    /// <summary>可以跑批量命令：有选中或至少有一条当前行。</summary>
    public bool CanAct => BatchCount > 0 || Selected is not null;

    /// <summary>当前批量作用域的文案（确认框 / 状态栏用）。</summary>
    public string ScopeText => BatchCount > 0 ? $"选中的 {BatchCount} 条" : "当前这一条";

    private void NotifyBatchChanged()
    {
        OnPropertyChanged(nameof(BatchCount));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(ScopeText));
    }

    /// <summary>列表重建后剔除已不在列表里的选中项（⛔ 对看不见的行执行批量）。</summary>
    private void PruneBatchSelection()
    {
        for (var i = BatchSelection.Count - 1; i >= 0; i--)
            if (!Items.Contains(BatchSelection[i])) BatchSelection.RemoveAt(i);
        NotifyBatchChanged();
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
        new("State", "状态", "90", sample: "可用"),
        new("Check", "校验", "90", sample: "通过"),
        new("LastUsed", "上次使用", "110", sample: "3 天前"),
    };

    public bool IsNameVisible => Vis("Name");
    public bool IsGroupVisible => Vis("Group");
    public bool IsTypeVisible => Vis("Type");
    public bool IsStateVisible => Vis("State");
    public bool IsCheckVisible => Vis("Check");
    public bool IsLastUsedVisible => Vis("LastUsed");

    protected override void RefreshView() => ApplyFilter();

    protected override void EnsureDetailSelection()
    {
        if (Selected is null && Items.Count > 0) Selected = Items[0];
    }

    private void ApplyFilter()
    {
        var key = (_searchQuery ?? "").Trim();

        IEnumerable<SourceRow> rows = _all;

        if (key.Length > 0)
            rows = rows.Where(r =>
                r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)
                || r.Group.Contains(key, StringComparison.OrdinalIgnoreCase)
                || r.State.Contains(key, StringComparison.OrdinalIgnoreCase)
                || r.Source.Url.Contains(key, StringComparison.OrdinalIgnoreCase));

        if (_typeFilter != AllTypes) rows = rows.Where(r => r.Type == _typeFilter);
        if (_groupFilter != AllGroups) rows = rows.Where(r => r.Group == _groupFilter);
        if (_checkFilter != AllChecks) rows = rows.Where(r => r.Check == _checkFilter);

        var keep = Selected?.Source.Url;

        Items.Clear();
        foreach (var row in ApplySort(rows)) Items.Add(row);

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(FilterSummary));
        PruneBatchSelection();

        if (keep is not null) Selected = Items.FirstOrDefault(r => r.Source.Url == keep);
        if (Selected is null && Items.Count > 0 && IsDetailVisible) Selected = Items[0];

        StatusText = BuildStatus();
    }

    /// <summary>按表头排序（未点过表头 ⇒ 保持库里的名称顺序）。</summary>
    private IEnumerable<SourceRow> ApplySort(IEnumerable<SourceRow> rows)
    {
        if (SortColumn.Length == 0) return rows;

        Func<SourceRow, string> selector = SortColumn switch
        {
            "Group" => r => r.Group,
            "Type" => r => r.Type,
            "State" => r => r.State,
            "Check" => r => r.Check,
            "LastUsed" => r => r.Source.LastUsedAt?.ToString("O") ?? "",
            _ => r => r.Name,
        };

        return SortDescending
            ? rows.OrderByDescending(selector, StringComparer.CurrentCulture)
            : rows.OrderBy(selector, StringComparer.CurrentCulture);
    }

    partial void OnSelectedChanged(SourceRow? value)
    {
        OnPropertyChanged("HasSelection");
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailName));
        OnPropertyChanged(nameof(DetailGroup));
        OnPropertyChanged(nameof(DetailType));
        OnPropertyChanged(nameof(DetailState));
        OnPropertyChanged(nameof(DetailCheck));
        OnPropertyChanged(nameof(DetailChecked));
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

        RefreshGroupOptions();

        ApplyFilter();
        StatusText = BuildStatus();
    }

    /// <summary>重建分组下拉（库里出现过的分组 + 未分组）；当前选中的分组消失了就退回「全部分组」。</summary>
    private void RefreshGroupOptions()
    {
        var groups = _all
            .Select(r => r.Group)
            .Distinct()
            .OrderBy(g => g, StringComparer.CurrentCulture)
            .ToList();

        var desired = new List<string> { AllGroups };
        desired.AddRange(groups);

        if (GroupOptions.SequenceEqual(desired)) return;

        GroupOptions.Clear();
        foreach (var group in desired) GroupOptions.Add(group);

        if (!GroupOptions.Contains(_groupFilter)) GroupFilter = AllGroups;
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
        var rows = ScopeRows();
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
        var rows = ScopeRows();
        if (rows.Count == 0) { StatusText = "先选中要启用的书源。"; return; }

        _store.SetEnabled(rows.Select(r => r.Source.Url), true);
        Reload();
        StatusText = $"已启用 {rows.Count} 条书源";
    }

    [RelayCommand]
    private void Disable()
    {
        var rows = ScopeRows();
        if (rows.Count == 0) { StatusText = "先选中要停用的书源。"; return; }

        _store.SetEnabled(rows.Select(r => r.Source.Url), false);
        Reload();
        StatusText = $"已停用 {rows.Count} 条书源";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var rows = ScopeRows();
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

    /// <summary>批量作用域：有选中就用全部选中行，否则退回当前行（右键单条操作仍可用）。</summary>
    private List<SourceRow> ScopeRows()
    {
        if (BatchSelection.Count > 0) return BatchSelection.ToList();
        return Selected is null ? new List<SourceRow>() : new List<SourceRow> { Selected };
    }

    // ==================== 多选 / 分组管理 ====================

    /// <summary>「☑ 全选当前结果」：选中筛选后列表里的全部行（⛔ 只作用于看得见的那些）。</summary>
    [RelayCommand]
    private void SelectAllVisible()
    {
        var all = BatchSelection.Count > 0 && BatchSelection.Count == Items.Count;

        BatchSelection.Clear();
        if (!all)
            foreach (var row in Items) BatchSelection.Add(row);

        NotifyBatchChanged();
        StatusText = all ? "已取消选择。" : $"已选中 {BatchSelection.Count} 条（当前筛选结果）。";
    }

    /// <summary>「☐ 取消选择」。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        BatchSelection.Clear();
        NotifyBatchChanged();
        StatusText = "已取消选择。";
    }

    /// <summary>「⇄ 反选」：当前筛选结果内反向选中。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        var inverted = Items.Where(r => !BatchSelection.Contains(r)).ToList();

        BatchSelection.Clear();
        foreach (var row in inverted) BatchSelection.Add(row);

        NotifyBatchChanged();
        StatusText = $"已反选，当前选中 {BatchSelection.Count} 条。";
    }

    /// <summary>
    /// 「📁 改分组」：把作用域内的书源改成输入的分组（留空 = 未分组）。
    /// <para>🔴 分组重命名 / 归并 / 清理都走这一条：先按分组筛出来，再改成目标名即可。</para>
    /// </summary>
    [RelayCommand]
    private async Task ChangeGroupAsync()
    {
        if (_store is null) return;

        var rows = ScopeRows();
        if (rows.Count == 0) { StatusText = "先选中要改分组的书源。"; return; }

        var current = rows.Select(r => r.Group).Distinct().Count() == 1 ? rows[0].Group : "";
        var input = await DialogHooks.InputAsync(
            $"把{ScopeText}书源改成哪个分组？（留空 = 未分组）", current == "未分组" ? "" : current).ConfigureAwait(true);

        if (input is null) return;   // 取消

        var group = input.Trim();
        var changed = _store.SetGroup(rows.Select(r => r.Source.Url), group);

        Selected = null;
        BatchSelection.Clear();
        Reload();
        StatusText = changed == 0
            ? "⚠ 改分组失败（书库不可用？）"
            : $"已把 {changed} 条书源改到「{(group.Length == 0 ? "未分组" : group)}」";
    }

    /// <summary>
    /// 「🗑 删除失效」：删掉所有校验失败 / 无结果的源（跨分组，先确认条数）。
    /// </summary>
    [RelayCommand]
    private async Task DeleteDeadAsync()
    {
        if (_store is null) return;

        var dead = _all.Where(r => r.Source.IsDead).ToList();
        if (dead.Count == 0)
        {
            StatusText = "没有失效书源 —— 先点「🔬 校验」跑一次才知道哪些该删。";
            return;
        }

        var failed = dead.Count(r => r.Source.CheckState == SourceCheckState.Failed);
        var confirmed = await DialogHooks.ConfirmAsync(
            $"删除 {dead.Count} 条失效书源？（校验失败 {failed} 条、无结果 {dead.Count - failed} 条）\n删除后要重新导入才能恢复。").ConfigureAwait(true);
        if (!confirmed) return;

        var deleted = _store.Delete(dead.Select(r => r.Source.Url));

        Selected = null;
        BatchSelection.Clear();
        Reload();
        StatusText = $"已删除 {deleted} 条失效书源";
    }

    // ==================== 编辑（原始 JSON）备用 ====================

    private static readonly System.Text.Json.JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    [RelayCommand]
    private void OpenEditor()
    {
        if (Selected is null) { StatusText = "先选中一条书源。"; return; }

        EditorTitle = $"编辑书源 · {Selected.Source.Name}";
        EditorJson = BuildJson(Selected.Source);
        EditorStatus = "改完点「保存」；JSON 不合法会被挡在这里，不会写进库。";
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CloseEditor() => IsEditorOpen = false;

    /// <summary>把书源摊成可编辑的美化 JSON：有原始文本就用原始文本（保住本程序不认识的字段）。</summary>
    private static string BuildJson(BookSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.RawJson))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(source.RawJson);
                return System.Text.Json.JsonSerializer.Serialize(document.RootElement, PrettyJson);
            }
            catch { /* 原始文本坏了 ⇒ 用已知字段重建 */ }
        }

        return System.Text.Json.JsonSerializer.Serialize(source, PrettyJson);
    }

    [RelayCommand]
    private void SaveEditor()
    {
        var store = _store;
        if (store is null) { EditorStatus = "⚠ 书库不可用。"; return; }

        var text = (EditorJson ?? "").Trim();
        if (text.Length == 0) { EditorStatus = "⚠ JSON 为空。"; return; }

        BookSource? parsed;
        try { parsed = System.Text.Json.JsonSerializer.Deserialize<BookSource>(text); }
        catch (Exception ex) { EditorStatus = "⚠ JSON 解析失败：" + ex.Message; return; }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Url))
        {
            EditorStatus = "⚠ 缺 bookSourceUrl（书源地址是主键），保存不了。";
            return;
        }

        parsed.RawJson = text;

        // 编辑同一条源时沿用原来的启用状态：JSON 里常常没写 enabled，不能因为改规则就把它悄悄停用
        if (Selected is not null && Selected.Source.Url == parsed.Url) parsed.Enabled = Selected.Source.Enabled;

        var written = store.Upsert(new[] { parsed });
        if (written == 0) { EditorStatus = "⚠ 写入失败（书库不可用？）"; return; }

        var renamed = Selected is not null && Selected.Source.Url != parsed.Url;
        IsEditorOpen = false;
        Reload();
        Selected = Items.FirstOrDefault(r => r.Source.Url == parsed.Url);
        StatusText = renamed
            ? $"已另存为「{parsed.Name}」—— 改了书源地址等于新增一条，原条目还在。"
            : $"已保存书源「{parsed.Name}」";
    }

    // ==================== 试跑（四段管线走一遍） ====================

    [RelayCommand]
    private void OpenDebug()
    {
        if (Selected is null) { StatusText = "先选中一条书源。"; return; }

        DebugSteps.Clear();
        DebugTitle = $"试跑书源 · {Selected.Source.Name}";
        DebugPhase = $"试跑「{Selected.Source.Name}」：输一个书名或作者，四段依次跑一遍。";
        IsDebugOpen = true;
    }

    [RelayCommand]
    private void CloseDebug()
    {
        try { _debugCts?.Cancel(); } catch { /* 已释放 */ }
        _debugCts?.Dispose();
        _debugCts = null;
        IsDebugOpen = false;
    }

    [RelayCommand]
    private async Task RunDebugAsync()
    {
        var debugService = _debug;
        var source = Selected?.Source;
        if (source is null) { DebugPhase = "⚠ 先选中一条书源。"; return; }
        if (debugService is null) { DebugPhase = "⚠ 调试服务未就绪。"; return; }

        var keyword = (DebugKeyword ?? "").Trim();
        if (keyword.Length == 0) { DebugPhase = "先输入一个关键词（书名 / 作者）。"; return; }

        try { _debugCts?.Cancel(); } catch { /* 上一轮已结束 */ }
        _debugCts?.Dispose();
        _debugCts = new CancellationTokenSource();
        var token = _debugCts.Token;

        DebugSteps.Clear();
        DebugPhase = "正在试跑…";

        try
        {
            var steps = await debugService.RunAsync(source, keyword, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) { DebugPhase = "已取消"; return; }

            foreach (var step in steps) DebugSteps.Add(step);

            var passed = steps.Count(s => s.Ok);
            DebugPhase = steps.Count == 0
                ? "没有跑出任何结果"
                : passed == steps.Count
                    ? $"{passed}/{steps.Count} 段全通 · 这个源现在能正常读"
                    : $"断了：{passed}/{steps.Count} 段通过（看标 ⛔ 的那一段）";
        }
        catch (OperationCanceledException) { DebugPhase = "已取消"; }
        catch (Exception ex) { DebugPhase = "⚠ 试跑异常：" + ex.Message; }
    }

    // ==================== 校验（批量跑搜索段） ====================

    /// <summary>校验范围选项（运行时按当前列表 / 选中 / 启用集合展开成具体条数）。</summary>
    public IReadOnlyList<string> CheckScopeOptions { get; } = new[] { "当前筛选结果", "选中项", "全部已启用" };

    /// <summary>校验浮层是否展开。</summary>
    [ObservableProperty] private bool _isCheckOpen;

    /// <summary>校验用的关键词（必填 —— 没有关键词就没法判「搜不搜得到」）。</summary>
    [ObservableProperty] private string _checkKeyword = "";

    /// <summary>校验范围（见 <see cref="CheckScopeOptions"/>）。</summary>
    [ObservableProperty] private string _checkScope = "当前筛选结果";

    /// <summary>校验进度 / 结论提示。</summary>
    [ObservableProperty] private string _checkPhase = "";

    /// <summary>正在校验（按钮可用态 + 关闭时先取消）。</summary>
    [ObservableProperty] private bool _isChecking;

    /// <summary>逐条结论（哪个源通过 / 断在哪）。</summary>
    public ObservableCollection<SourceCheckRow> CheckResults { get; } = new();

    private CancellationTokenSource? _checkCts;

    /// <summary>库里失效条数（校验浮层里「删除失效」的条数）。</summary>
    public string DeadText
    {
        get
        {
            var dead = _all.Count(r => r.Source.IsDead);
            return dead == 0 ? "没有失效书源" : $"失效 {dead} 条（失败 + 无结果）";
        }
    }

    [RelayCommand]
    private void OpenCheck()
    {
        if (_all.Count == 0) { StatusText = "还没有书源 —— 先导入再校验。"; return; }

        CheckResults.Clear();
        CheckPhase = "输一个书名或作者，点「开始校验」：每个源各跑一次搜索，结论直接写进「校验」列。";
        IsCheckOpen = true;
        OnPropertyChanged(nameof(DeadText));
    }

    [RelayCommand]
    private void CloseCheck()
    {
        try { _checkCts?.Cancel(); } catch { /* 已释放 */ }
        _checkCts?.Dispose();
        _checkCts = null;
        IsChecking = false;
        IsCheckOpen = false;
    }

    [RelayCommand]
    private async Task RunCheckAsync()
    {
        var service = _check;
        if (service is null) { CheckPhase = "⚠ 校验服务未就绪。"; return; }

        var keyword = (CheckKeyword ?? "").Trim();
        if (keyword.Length == 0) { CheckPhase = "先输一个关键词（书名 / 作者）。"; return; }

        var sources = CheckScope switch
        {
            "选中项" => BatchSelection.Select(r => r.Source).ToList(),
            "全部已启用" => _all.Where(r => r.IsEnabled).Select(r => r.Source).ToList(),
            _ => Items.Select(r => r.Source).ToList(),
        };

        if (sources.Count == 0) { CheckPhase = "这个范围里没有书源。"; return; }

        try { _checkCts?.Cancel(); } catch { /* 上一轮已结束 */ }
        _checkCts?.Dispose();
        _checkCts = new CancellationTokenSource();
        var token = _checkCts.Token;

        CheckResults.Clear();
        IsChecking = true;
        CheckPhase = $"正在校验 {sources.Count} 个源…";

        var passed = 0;
        var empty = 0;
        var failed = 0;

        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
                CheckPhase = $"正在校验… 已完成 {p.Done} / {p.Total}");

            await service.CheckManyAsync(sources, keyword, token, progress, row =>
            {
                switch (row.State)
                {
                    case SourceCheckState.Passed: passed++; break;
                    case SourceCheckState.NoResult: empty++; break;
                    default: failed++; break;
                }

                CheckResults.Add(row);
            }).ConfigureAwait(true);

            if (token.IsCancellationRequested) { CheckPhase = "已取消（已跑完的结论已存库）。"; return; }

            CheckPhase = $"校验完成 {sources.Count} 个源：通过 {passed} / 无结果 {empty} / 失败 {failed}。";
        }
        catch (OperationCanceledException) { CheckPhase = "已取消（已跑完的结论已存库）。"; }
        catch (Exception ex) { CheckPhase = "⚠ 校验异常：" + ex.Message; }
        finally
        {
            IsChecking = false;

            // 结论落库了 ⇒ 刷列表，「校验」列与失效计数跟着变
            Reload();
            OnPropertyChanged(nameof(DeadText));
        }
    }

    /// <summary>「只看失效」：把校验筛选切到失败（配合「☑ 全选」= 批量删掉）。</summary>
    [RelayCommand]
    private void FilterFailed()
    {
        CheckFilter = "失败";
        StatusText = ApplyFilterState("只显示校验失败的源");
    }

    private string ApplyFilterState(string prefix) => $"{prefix} · {FilterSummary}";

    private string BuildStatus()
    {
        if (_all.Count == 0) return "还没有书源 —— 点工具条「📥 导入书源」选一个 legado 书源 JSON。";

        var ready = _all.Count(r => r.Source.Capability == SourceCapability.Ready);
        var webView = _all.Count(r => r.Source.Capability == SourceCapability.NeedsWebView);
        var incomplete = _all.Count(r => r.Source.Capability == SourceCapability.Incomplete);
        var enabled = _all.Count(r => r.IsEnabled);
        var dead = _all.Count(r => r.Source.IsDead);

        var text = $"共 {_all.Count} 条 · 可用 {ready} / 需 WebView {webView} / 不完整 {incomplete} · 启用 {enabled}";
        if (dead > 0) text += $" · 失效 {dead}";
        if (Items.Count != _all.Count) text += $" · 当前筛出 {Items.Count}";
        return text;
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
            IsEnabledPath = "CanAct",
            ShowToolTipOnDisabled = true,
            ToolTip = "启用选中的书源",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "⏸ 停用",
            CommandPath = "DisableCommand",
            IsEnabledPath = "CanAct",
            ShowToolTipOnDisabled = true,
            ToolTip = "停用选中的书源",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "📤 导出",
            CommandPath = "ExportCommand",
            IsEnabledPath = "CanAct",
            ShowToolTipOnDisabled = true,
            ToolTip = "把选中书源导成 JSON（原样回转）",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "✎ 编辑",
            CommandPath = "OpenEditorCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "编辑书源的原始 JSON（不合法会被挡住，⛔ 不写库）",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "▶ 试跑",
            CommandPath = "OpenDebugCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "输一个关键词，把搜索 / 详情 / 目录 / 正文四段各跑一遍，看断在哪",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "🔬 校验",
            CommandPath = "OpenCheckCommand",
            ToolTip = "给一批书源各跑一次搜索，结论写进「校验」列；跑完可一键删失效源",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "🗑 删除",
            Classes = "danger-btn",
            CommandPath = "DeleteCommand",
            IsEnabledPath = "CanAct",
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

    /// <summary>校验档 + 具体结论（通过几条 / 失败原因）。</summary>
    public string DetailCheck => Selected?.Source.CheckText ?? "";

    /// <summary>上次校验时间。</summary>
    public string DetailChecked => Selected?.Source.CheckedText ?? "";

    public string DetailReason => Selected?.Source.CapabilityReason ?? "";

    public string DetailLastUsed => Selected is null ? "" : SourceTimeText.Of(Selected.Source.LastUsedAt);

    public string DetailSearchUrl => Selected?.Source.SearchUrl ?? "";

    public string DetailUrl => Selected?.Source.Url ?? "";

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧选中一条书源，或点工具条「📥 导入书源」。导入后每条源会标出「可用 / 需 WebView / 不完整」。",
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
                    new DetailField { Label = "校验", ValuePath = nameof(DetailCheck), Kind = FieldKind.Text },
                    new DetailField { Label = "上次校验", ValuePath = nameof(DetailChecked), Kind = FieldKind.Text },
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
                        Hint = "判定只看搜索 / 目录 / 正文三段的规则：缺关键字段 = 不完整；用到 WebView = 需 WebView；含 @js: 或 <js> 仍算可用（内置 JS 引擎）。",
                    },
                }
            }
        }
    };
}
