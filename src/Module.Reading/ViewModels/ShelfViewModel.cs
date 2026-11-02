using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

/// <summary>书架列表的一行。</summary>
public sealed class ShelfRow
{
    public ShelfRow(ShelfBook book, string sourceName, ReadingProgress? progress)
    {
        Book = book;
        SourceName = sourceName;
        Progress = progress;

        if (progress is not null)
        {
            Book.LastChapterName = progress.ChapterName;
            Book.LastReadAt = progress.UpdatedAt;
        }
    }

    public ShelfBook Book { get; }

    public ReadingProgress? Progress { get; }

    public string Name => Book.Name;

    public string Author => Book.AuthorText;

    public string SourceName { get; }

    public string Added => Book.AddedText;

    /// <summary>分组（空 ⇒ 未分组）。</summary>
    public string Group => Book.GroupText;

    /// <summary>读到哪一章 / 还没开始。</summary>
    public string ProgressText => Book.ProgressText;

    public string ReadTime => Book.ReadTimeText;

    /// <summary>「更新」列（未检查 / 有 N 章更新 / 已是最新 / 更新失败）。</summary>
    public string Update => Book.UpdateText;

    /// <summary>「更新」列的悬停提示（失败原因 / 最新章节 / 检查时间）。</summary>
    public string UpdateTip => Book.UpdateTip;

    public string Tip => Book.BookUrl;
}

/// <summary>
/// 「网络阅读 → 书架」页（<c>reading-shelf</c>）：搜索结果里点「加入书架」存下来的书。
/// <para>双击 / 右键「继续阅读」把书交给搜索页（那里有完整的目录 + 正文），⛔ 本页不重复实现阅读器。</para>
/// </summary>
public partial class ShelfViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly ShelfStore _store;
    private readonly SourceStore _sources;
    private readonly ProgressStore _progress;
    private readonly ReadingSession _session;
    private readonly ShelfUpdateService? _update;
    private readonly INavigationService? _nav;

    private readonly List<ShelfRow> _all = new();
    private IReadOnlyList<ToolbarItemDescriptor>? _toolbar;

    [ObservableProperty] private ShelfRow? _selected;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public ShelfViewModel() : base(null, null)
    {
        _store = null!;
        _sources = null!;
        _progress = null!;
        _session = new ReadingSession();
        _nav = null;
    }

    public ShelfViewModel(
        ShelfStore store,
        SourceStore sources,
        ProgressStore progress,
        ReadingSession session,
        INavigationService? nav = null,
        IColumnLayoutService? cols = null,
        IOptionsStore? opts = null,
        ShelfUpdateService? update = null)
        : base(cols, opts)
    {
        _store = store;
        _sources = sources;
        _progress = progress;
        _session = session;
        _nav = nav;
        _update = update;

        LoadLayout();
        Reload();
    }

    public ObservableCollection<ShelfRow> Items { get; } = new();

    /// <summary>页面标签关闭：中断还在跑的批量更新（单例服务不会被它拖住）。</summary>
    public override void Dispose()
    {
        try { _updateCts?.Cancel(); } catch { /* 已释放 */ }
        _updateCts?.Dispose();
        _updateCts = null;
        base.Dispose();
    }

    public bool HasItems => Items.Count > 0;

    public bool HasSelection => Selected is not null;

    public bool ShowDetailBody => Selected is not null;

    protected override string PageKey => "reading-shelf";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "书名", "240", sample: "剑来"),
        new("Author", "作者", "140", sample: "烽火戏诸侯"),
        new("Group", "分组", "120", sample: "未分组"),
        new("ProgressText", "读到", "220", sample: "读到 第 12 章 风雪夜归人"),
        new("Update", "更新", "120", sample: "有 3 章更新"),
        new("SourceName", "来源", "160", sample: "某某小说网（优）"),
        new("Added", "加入时间", "110", sample: "2026-11-01"),
    };

    public bool IsNameVisible => Vis("Name");
    public bool IsAuthorVisible => Vis("Author");
    public bool IsGroupVisible => Vis("Group");
    public bool IsProgressVisible => Vis("ProgressText");
    public bool IsUpdateVisible => Vis("Update");
    public bool IsSourceNameVisible => Vis("SourceName");
    public bool IsAddedVisible => Vis("Added");

    protected override void RefreshView() => ApplyFilter();

    protected override void EnsureDetailSelection()
    {
        if (Selected is null && Items.Count > 0) Selected = Items[0];
    }

    private const string AllGroups = "全部分组";
    private const string AllUpdates = "全部状态";

    /// <summary>更新状态筛选项（与 <see cref="ShelfBook.UpdateText"/> 对齐 + 一个「有更新」归并档）。</summary>
    public IReadOnlyList<string> UpdateOptions { get; } = new[] { AllUpdates, "有更新", "已是最新", "未检查", "更新失败" };

    /// <summary>分组筛选项（<c>全部分组</c> + 书架里实际出现过的分组）。</summary>
    public ObservableCollection<string> GroupOptions { get; } = new() { AllGroups };

    private string _groupFilter = AllGroups;
    private string _updateFilter = AllUpdates;

    /// <summary>分组筛选。</summary>
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

    /// <summary>更新状态筛选（有更新 / 已是最新 / 未检查 / 更新失败）。</summary>
    public string UpdateFilter
    {
        get => _updateFilter;
        set
        {
            var text = string.IsNullOrWhiteSpace(value) ? AllUpdates : value;
            if (_updateFilter == text) return;
            _updateFilter = text;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>筛选命中条数（筛选条右侧的「N / 共 M」）。</summary>
    /// <remarks>基类同名成员统计的是「生效筛选器个数」；本页筛选全在页面内（分组 / 更新状态 / 只看有更新），
    /// 要看的是命中本数 ⇒ 有意隐藏。</remarks>
    public new string FilterSummary => Items.Count == _all.Count
        ? $"共 {_all.Count} 本"
        : $"筛出 {Items.Count} / 共 {_all.Count} 本";

    private void ApplyFilter()
    {
        var key = (_searchQuery ?? "").Trim();

        IEnumerable<ShelfRow> rows = _all;

        if (key.Length > 0)
            rows = rows.Where(r => r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)
                                   || r.Author.Contains(key, StringComparison.OrdinalIgnoreCase));

        if (_groupFilter != AllGroups) rows = rows.Where(r => r.Group == _groupFilter);
        if (_updateFilter != AllUpdates) rows = rows.Where(r => MatchesUpdate(r, _updateFilter));

        var keep = Selected?.Book.BookUrl;

        Items.Clear();
        foreach (var row in rows) Items.Add(row);

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(FilterSummary));

        if (keep is not null) Selected = Items.FirstOrDefault(r => r.Book.BookUrl == keep);
        if (Selected is null && Items.Count > 0 && IsDetailVisible) Selected = Items[0];
    }

    /// <summary>「有更新」是个归并档（只有 <c>new_count &gt; 0</c> 才算），其余按列文案精确匹配。</summary>
    private static bool MatchesUpdate(ShelfRow row, string filter) => filter switch
    {
        "有更新" => row.Book.NewCount > 0 && string.IsNullOrWhiteSpace(row.Book.UpdateNote),
        "已是最新" => row.Update == "已是最新",
        "未检查" => row.Update == "未检查",
        "更新失败" => row.Update == "⚠ 更新失败",
        _ => true,
    };

    /// <summary>重建分组下拉；当前选中的分组没了就退回「全部分组」。</summary>
    private void RefreshGroupOptions()
    {
        var desired = new List<string> { AllGroups };
        desired.AddRange(_all.Select(r => r.Group).Distinct().OrderBy(g => g, StringComparer.CurrentCulture));

        if (GroupOptions.SequenceEqual(desired)) return;

        GroupOptions.Clear();
        foreach (var group in desired) GroupOptions.Add(group);

        if (!GroupOptions.Contains(_groupFilter)) GroupFilter = AllGroups;
    }

    private string _searchQuery = "";

    /// <summary>工具条搜索框：按书名 / 作者过滤。</summary>
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

    partial void OnSelectedChanged(ShelfRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailName));
        OnPropertyChanged(nameof(DetailAuthor));
        OnPropertyChanged(nameof(DetailGroup));
        OnPropertyChanged(nameof(DetailProgress));
        OnPropertyChanged(nameof(DetailSource));
        OnPropertyChanged(nameof(DetailAdded));
        OnPropertyChanged(nameof(DetailLatest));
        OnPropertyChanged(nameof(DetailUpdate));
        OnPropertyChanged(nameof(DetailUpdateNote));
        OnPropertyChanged(nameof(DetailUrl));
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void Reload()
    {
        _all.Clear();
        if (_store is not null)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_sources is not null)
                foreach (var source in _sources.LoadAll()) names[source.Url] = source.Name;

            // 一次取全部书的最新进度（⛔ 不逐书查库）
            var progresses = _progress?.LoadLatest();

            foreach (var book in _store.LoadAll())
            {
                var name = names.TryGetValue(book.SourceUrl, out var found) ? found : "（源已删除）";
                ReadingProgress? progress = null;
                progresses?.TryGetValue(book.BookUrl, out progress);
                _all.Add(new ShelfRow(book, name, progress));
            }
        }

        RefreshGroupOptions();
        ApplyFilter();

        if (_all.Count == 0)
        {
            StatusText = "书架是空的 —— 到「网上搜索」搜到书后，右键「＋ 加入书架」。";
            return;
        }

        var updated = _all.Count(r => r.Book.NewCount > 0 && string.IsNullOrWhiteSpace(r.Book.UpdateNote));
        StatusText = updated > 0
            ? $"书架里 {_all.Count} 本 · 有新章节 {updated} 本"
            : $"书架里 {_all.Count} 本";
    }

    // ==================== 批量更新（回源站抓详情 + 目录） ====================

    /// <summary>正在批量更新（按钮可用态 + 关闭页面时先取消）。</summary>
    [ObservableProperty] private bool _isUpdating;

    /// <summary>更新进度 / 结算文案。</summary>
    [ObservableProperty] private string _updatePhase = "";

    /// <summary>不在更新中（工具条按钮的可用态；Avalonia 绑定不支逻辑运算符，⛔ 别在 XAML 里写 And）。</summary>
    public bool IsNotUpdating => !IsUpdating;

    partial void OnIsUpdatingChanged(bool value) => OnPropertyChanged(nameof(IsNotUpdating));

    private CancellationTokenSource? _updateCts;

    /// <summary>
    /// 「🔄 批量更新」：把**当前筛选结果**里的书逐本回源站抓详情 + 目录，比章数得出「多了几章」。
    /// <para>🔴 首次检查只建基线；单本失败不中断整批，失败原因进「更新」列。</para>
    /// </summary>
    [RelayCommand]
    private async Task BatchUpdateAsync()
    {
        var service = _update;
        if (service is null) { StatusText = "⚠ 更新服务未就绪。"; return; }

        var books = Items.Select(r => r.Book).ToList();
        if (books.Count == 0) { StatusText = "没有要更新的书。"; return; }

        await RunUpdateAsync(service, books).ConfigureAwait(true);
    }

    /// <summary>右键「🔄 更新这本」：只跑当前选中那本。</summary>
    [RelayCommand]
    private async Task UpdateOneAsync()
    {
        var service = _update;
        if (service is null) { StatusText = "⚠ 更新服务未就绪。"; return; }
        if (Selected is null) { StatusText = "先选中一本。"; return; }

        await RunUpdateAsync(service, new List<ShelfBook> { Selected.Book }).ConfigureAwait(true);
    }

    private async Task RunUpdateAsync(ShelfUpdateService service, List<ShelfBook> books)
    {
        try { _updateCts?.Cancel(); } catch { /* 上一轮已结束 */ }
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();
        var token = _updateCts.Token;

        IsUpdating = true;
        UpdatePhase = $"正在检查 {books.Count} 本…";

        var fresh = 0;
        var totalNew = 0;
        var failed = 0;

        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                UpdatePhase = $"正在检查… {p.Done} / {p.Total} 本";
                StatusText = UpdatePhase;
            });

            await service.UpdateManyAsync(books, token, progress, result =>
            {
                if (result.Ok)
                {
                    if (result.NewCount > 0) fresh++;
                    totalNew += result.NewCount;
                }
                else failed++;
            }).ConfigureAwait(true);

            if (token.IsCancellationRequested) { UpdatePhase = "已取消（已完成的那几本已写库）。"; return; }

            UpdatePhase = fresh == 0
                ? $"检查完 {books.Count} 本：都没新章节{(failed > 0 ? $"（{failed} 本失败）" : "")}"
                : $"检查完 {books.Count} 本：{fresh} 本有新章节（共 {totalNew} 章）{(failed > 0 ? $"，{failed} 本失败" : "")}";
        }
        catch (OperationCanceledException) { UpdatePhase = "已取消（已完成的那几本已写库）。"; }
        catch (Exception ex) { UpdatePhase = "⚠ 更新异常：" + ex.Message; }
        finally
        {
            IsUpdating = false;
            Reload();
            StatusText = UpdatePhase;
        }
    }

    /// <summary>「🔔 只看有更新」：筛选切到「有更新」（配合「批量更新」前后看结果）。</summary>
    [RelayCommand]
    private void FilterWithUpdates()
    {
        UpdateFilter = "有更新";
        StatusText = $"只看有新章节的 · {FilterSummary}";
    }

    [RelayCommand]
    private void Read()
    {
        if (Selected is null) { StatusText = "先选中一本。"; return; }

        _session.PendingBook = new PendingBook(Selected.Book.SourceUrl, Selected.Book.BookUrl, Selected.Name);
        _nav?.NavigateTo("reading-read");
    }

    /// <summary>改分组（空白 ⇒ 移回「未分组」）。</summary>
    [RelayCommand]
    private async Task SetGroupAsync()
    {
        if (Selected is null) { StatusText = "先选中一本。"; return; }

        var input = await DialogHooks.InputAsync($"《{Selected.Name}》分到哪个组？", Selected.Book.GroupText).ConfigureAwait(true);
        if (input is null) return;

        var group = input.Trim();
        var changed = _store.SetGroup(new[] { Selected.Book.BookUrl }, group);
        Reload();
        StatusText = changed > 0
            ? (group.Length == 0 ? "已归到「未分组」" : $"已分到「{group}」")
            : "⚠ 分组没改成（书库不可用）";
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (Selected is null) { StatusText = "先选中一本。"; return; }

        var ok = await DialogHooks.ConfirmAsync($"把《{Selected.Name}》移出书架？（只从列表移除，不影响源站）").ConfigureAwait(true);
        if (!ok) return;

        _store.Remove(new[] { Selected.Book.BookUrl });
        Selected = null;
        Reload();
        StatusText = "已移出书架";
    }

    // ==================== 工具条 ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => _toolbar ??= new List<ToolbarItemDescriptor>
    {
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "🔄 刷新书架",
            CommandPath = "ReloadCommand",
            ToolTip = "只重新读本地书架（不发网络请求）",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "🆕 批量更新",
            CommandPath = "BatchUpdateCommand",
            IsEnabledPath = "IsNotUpdating",
            ShowToolTipOnDisabled = true,
            ToolTip = "回源站逐本抓详情 + 目录，标出「有 N 章更新」（作用域 = 当前筛选结果）",
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Placeholder = "搜索书名 / 作者",
            ValuePath = "SearchQuery",
            Width = 220,
        },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "📖 继续阅读",
            Classes = "primary",
            CommandPath = "ReadCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "打开阅读页，从上次读到的章节接着看",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "📁 分组",
            CommandPath = "SetGroupCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "给这本书设一个分组（留空 = 未分组）",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "🗑 移出书架",
            Classes = "danger-btn",
            CommandPath = "RemoveCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "只从书架移除，不影响源站",
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

    public string DetailName => Selected?.Name ?? "";

    public string DetailAuthor => Selected?.Author ?? "";

    public string DetailSource => Selected?.SourceName ?? "";

    public string DetailAdded => Selected?.Added ?? "";

    public string DetailGroup => Selected?.Group ?? "";

    public string DetailProgress => Selected is null ? "" : $"{Selected.ProgressText}（{Selected.ReadTime}）";

    public string DetailLatest => Selected is null
        ? ""
        : (string.IsNullOrWhiteSpace(Selected.Book.LatestChapter) ? "（还没检查过）" : Selected.Book.LatestChapter);

    public string DetailUpdate => Selected?.Book.UpdateText ?? "";

    public string DetailUpdateNote => Selected?.Book.UpdateTip ?? "";

    public string DetailUrl => Selected?.Book.BookUrl ?? "";

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "书架是空的。在「网上搜索」里搜到书后，右键「＋ 加入书架」。",
        Sections =
        {
            new DetailSection
            {
                Header = "这本书",
                Fields =
                {
                    new DetailField { Label = "书名", ValuePath = nameof(DetailName), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "作者", ValuePath = nameof(DetailAuthor), Kind = FieldKind.Text },
                    new DetailField { Label = "分组", ValuePath = nameof(DetailGroup), Kind = FieldKind.Text },
                    new DetailField { Label = "进度", ValuePath = nameof(DetailProgress), Kind = FieldKind.Text },
                    new DetailField { Label = "来源", ValuePath = nameof(DetailSource), Kind = FieldKind.Text },
                    new DetailField { Label = "更新", ValuePath = nameof(DetailUpdate), Kind = FieldKind.Text },
                    new DetailField { Label = "最新章节", ValuePath = nameof(DetailLatest), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "检查结果", ValuePath = nameof(DetailUpdateNote), Kind = FieldKind.Text, MultiLine = true, Rows = 40, Selectable = true },
                    new DetailField { Label = "加入时间", ValuePath = nameof(DetailAdded), Kind = FieldKind.Text },
                    new DetailField { Label = "详情地址", ValuePath = nameof(DetailUrl), Kind = FieldKind.Text, Selectable = true, Mono = true, MultiLine = true, Rows = 44 },
                }
            },
        }
    };
}
