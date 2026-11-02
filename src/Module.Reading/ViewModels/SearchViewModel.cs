using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Reading.Models;
using NewToys.Reading.Services;
using NewToys.Shared.Services;

namespace NewToys.Reading.ViewModels;

/// <summary>搜索页里的一个书源勾选项。</summary>
public partial class SourcePick : ObservableObject
{
    public SourcePick(BookSource source, bool selected)
    {
        Source = source;
        IsSelected = selected;
    }

    public BookSource Source { get; }

    public string Name => Source.Name;

    public string Hint => Source.GroupText;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>勾选变了（页面据此刷新「已选 N / M」）。</summary>
    public event EventHandler? SelectionChanged;
}

/// <summary>
/// 「网络阅读 → 网上搜索」页（<c>reading-search</c>）：左边书源多选，右边搜索结果。
/// <para>点一本书 = 进「阅读」页读正文（本页 ⛔ 不内嵌目录与正文，与手机端一致）。</para>
/// </summary>
public partial class SearchViewModel : ObservableObject, IStatusProvider, IDisposable
{
    private readonly SourceStore _store;
    private readonly CrawlService _crawl;
    private readonly ShelfStore _shelf;
    private readonly ReadingSession _session;
    private readonly CoverLoader _covers;
    private readonly INavigationService? _nav;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyPhase = "";
    [ObservableProperty] private SearchItem? _selectedResult;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _sourceFilter = "";

    /// <summary>书源多选浮层是否展开（默认收起，点工具条「📚 书源」才弹）。</summary>
    [ObservableProperty] private bool _showSources;

    /// <summary>书籍卡是否展开（点一条结果打开；遮罩 / 关闭按钮收起）。</summary>
    [ObservableProperty] private bool _isCardOpen;
    [ObservableProperty] private SearchItem? _cardBook;
    [ObservableProperty] private Bitmap? _cardCover;

    /// <summary>设计期兜底构造。</summary>
    public SearchViewModel() : this(null!, null!, null!, new ReadingSession(), null!) { }

    public SearchViewModel(SourceStore store, CrawlService crawl, ShelfStore shelf, ReadingSession session,
        CoverLoader covers, INavigationService? nav = null)
    {
        _store = store;
        _crawl = crawl;
        _shelf = shelf;
        _session = session;
        _covers = covers;
        _nav = nav;
        ReloadSources();
    }

    /// <summary>全部可用书源（多选框列表）。</summary>
    public ObservableCollection<SourcePick> Sources { get; } = new();

    /// <summary>过滤后的书源（界面绑这个）。</summary>
    public ObservableCollection<SourcePick> VisibleSources { get; } = new();

    public ObservableCollection<SearchItem> Results { get; } = new();

    public event EventHandler? StatusChanged;

    public bool HasResults => Results.Count > 0;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsNotBusy => !IsBusy;

    public bool HasSelection => SelectedResult is not null;

    public string SourceSummary => $"已选 {Sources.Count(s => s.IsSelected)} / {Sources.Count} 个可用书源";

    public string ResultTitle => Results.Count == 0 ? "搜索结果" : $"搜索结果 · {Results.Count} 本";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnSourceFilterChanged(string value) => RebuildVisibleSources();

    partial void OnSelectedResultChanged(SearchItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (value is not null && !ReferenceEquals(CardBook, value)) CardBook = value;
    }

    partial void OnCardBookChanged(SearchItem? value)
    {
        IsCardOpen = value is not null;
        CardCover = null;
        if (value is null && SelectedResult is not null) SelectedResult = null;
        OnPropertyChanged(nameof(CardCoverInitial));
        if (value is not null) _ = LoadCoverAsync(value);
    }

    /// <summary>书籍卡上的「开始阅读」（与工具条同一个入口）。</summary>
    [RelayCommand]
    private void ReadCardBook() => OpenInReader();

    /// <summary>书籍卡上的「加入书架」（与工具条同一个入口）。</summary>
    [RelayCommand]
    private void AddCardToShelf() => AddToShelf();

    partial void OnCardCoverChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCardCover));

    /// <summary>有封面就显示图；没有则显示书名首字占位块。</summary>
    public bool HasCardCover => CardCover is not null;

    public string CardCoverInitial
    {
        get
        {
            var name = CardBook?.Name;
            return string.IsNullOrWhiteSpace(name) ? "书" : name.Trim()[..1];
        }
    }

    [RelayCommand]
    private void CloseCard() => CardBook = null;

    private async Task LoadCoverAsync(SearchItem book)
    {
        if (_covers is null || string.IsNullOrWhiteSpace(book.CoverUrl)) return;

        var cover = await _covers.LoadAsync(book.CoverUrl).ConfigureAwait(true);
        if (!ReferenceEquals(CardBook, book)) return;
        CardCover = cover;
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void ReloadSources()
    {
        Sources.Clear();
        if (_store is not null)
        {
            foreach (var source in _store.LoadAll())
            {
                if (!source.Enabled || source.Capability != SourceCapability.Ready) continue;
                var pick = new SourcePick(source, true);
                pick.SelectionChanged += OnPickSelectionChanged;
                Sources.Add(pick);
            }
        }

        RebuildVisibleSources();
        OnPropertyChanged(nameof(SourceSummary));
        StatusText = Sources.Count == 0
            ? "没有可用书源 —— 先到「网络阅读 → 书源」导入 JSON。"
            : $"可用书源 {Sources.Count} 个";

        // 阅读页「换源」把书名带过来（取走即清空，⛔ 不残留到下次进页）
        CheckPendingKeyword();
    }

    /// <summary>
    /// 取走「换源」带过来的书名。
    /// <para>🔴 标签页复用时 VM 不重建 ⇒ 页面重新挂载那一刻也要再查一次，否则第二次换源没反应。</para>
    /// </summary>
    public void CheckPendingKeyword()
    {
        if (_session.TakePendingKeyword() is { } keyword) Keyword = keyword;
    }

    private void OnPickSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SourceSummary));
    }

    private void RebuildVisibleSources()
    {
        var key = (SourceFilter ?? "").Trim();
        VisibleSources.Clear();
        foreach (var pick in Sources)
        {
            if (key.Length > 0
                && pick.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0
                && pick.Hint.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
            VisibleSources.Add(pick);
        }
    }

    /// <summary>全选 / 全不选（只作用于当前过滤出来的那些）。</summary>
    [RelayCommand]
    private void SelectAllSources()
    {
        foreach (var pick in VisibleSources) pick.IsSelected = true;
        OnPropertyChanged(nameof(SourceSummary));
    }

    [RelayCommand]
    private void ClearSources()
    {
        foreach (var pick in VisibleSources) pick.IsSelected = false;
        OnPropertyChanged(nameof(SourceSummary));
    }

    [RelayCommand]
    private void ToggleSources() => ShowSources = !ShowSources;

    /// <summary>把当前选中的结果加入书架。</summary>
    [RelayCommand]
    private void AddToShelf()
    {
        if (SelectedResult is null) { StatusText = "先在结果里选一本书。"; return; }

        var book = new ShelfBook
        {
            BookUrl = SelectedResult.BookUrl,
            SourceUrl = SelectedResult.SourceUrl,
            Name = SelectedResult.Name,
            Author = SelectedResult.Author,
            CoverUrl = SelectedResult.CoverUrl,
        };

        StatusText = _shelf is not null && _shelf.Add(book)
            ? $"《{book.Name}》已加入书架"
            : "⚠ 加入书架失败（书库不可用）";
    }

    /// <summary>打开选中的书 → 阅读页（正文只在那一个页面上渲染）。</summary>
    [RelayCommand]
    private void OpenInReader()
    {
        if (SelectedResult is null) { StatusText = "先在结果里选一本书。"; return; }
        if (string.IsNullOrWhiteSpace(SelectedResult.SourceUrl)) { StatusText = "这条结果没有来源书源。"; return; }

        _session.PendingBook = new PendingBook(
            SelectedResult.SourceUrl, SelectedResult.BookUrl, SelectedResult.Name);

        if (_nav is null) { StatusText = "导航服务未就绪。"; return; }
        _nav.NavigateTo("reading-read");
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_crawl is null) { StatusText = "⚠ 抓取服务未就绪。"; return; }

        var keyword = (Keyword ?? "").Trim();
        if (keyword.Length == 0) { StatusText = "先输入书名或作者。"; return; }

        var picks = Sources.Where(s => s.IsSelected).Select(s => s.Source).ToList();
        if (picks.Count == 0) { StatusText = "至少勾选一个书源。"; return; }

        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Results.Clear();
        SelectedResult = null;
        ErrorText = "";
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ResultTitle));
        IsBusy = true;
        BusyPhase = $"正在用 {picks.Count} 个书源搜索「{keyword}」…";

        try
        {
            var arrived = 0;
            var hit = 0;
            var failures = new List<string>();

            // 🔴 逐源上屏：哪个源先回来就先显示它的结果 / 错误。
            // ⛔ 不要等所有源都结束再合并——源多、个别站点不响应时，界面会长时间停在「正在搜索」且一条结果都没有。
            var onSource = new Progress<SourceSearchResult>(r =>
            {
                arrived++;

                if (r.Ok)
                {
                    foreach (var item in r.Items) Results.Add(item);
                    hit += r.Items.Count;
                }
                else if (r.Error is not null)
                {
                    failures.Add(r.Source.Name + " · " + r.Error.Display);
                }

                BusyPhase = $"正在搜索… 已完成 {arrived} / {picks.Count} 个源 · 命中 {hit} 条";
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(ResultTitle));
            });

            var progress = new Progress<(int Done, int Total)>(p =>
                BusyPhase = $"正在搜索… 已完成 {p.Done} / {p.Total} 个源 · 命中 {hit} 条");

            var results = await _crawl.SearchManyAsync(picks, keyword, 1, token, progress, onSource)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(ResultTitle));

            ErrorText = failures.Count == 0 ? "" : string.Join("\n", failures.Take(5));

            StatusText = $"命中 {hit} 条 · 成功 {results.Count(r => r.Ok)} / {results.Count} 个源";
            if (hit == 0 && failures.Count > 0) StatusText = "⚠ 没有命中，下面是被拒绝或失败的原因";
        }
        catch (OperationCanceledException) { StatusText = "已取消"; }
        catch (Exception ex) { StatusText = "⚠ 搜索异常：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { /* 已释放 */ }
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Cancel();
        StatusChanged = null;
    }
}
