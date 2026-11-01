using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
}

/// <summary>
/// 「阅读 → 网上搜索」页（<c>reading-search</c>）：一页三段 —— 上段选源 + 关键词，
/// 中段搜索结果，右段目录 + 正文。
/// <para>
/// 三步（搜索 / 目录 / 正文）失败时把 <see cref="Models.StepError"/> 的「哪一步、哪条规则、什么原因」
/// 直接显示在错误条上，⛔ 不只是一句"失败了"。
/// </para>
/// </summary>
public partial class SearchViewModel : ObservableObject, IStatusProvider, IDisposable
{
    private readonly SourceStore _store;
    private readonly CrawlService _crawl;
    private readonly ShelfStore _shelf;
    private readonly ReadingSession _session;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyPhase = "";
    [ObservableProperty] private SearchItem? _selectedResult;
    [ObservableProperty] private Chapter? _selectedChapter;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _bookInfoText = "";
    [ObservableProperty] private string _contentTitle = "";
    [ObservableProperty] private string _contentText = "";

    /// <summary>设置项兜底构造。</summary>
    public SearchViewModel() : this(null!, null!, null!, new ReadingSession()) { }

    public SearchViewModel(SourceStore store, CrawlService crawl, ShelfStore shelf, ReadingSession session)
    {
        _store = store;
        _crawl = crawl;
        _shelf = shelf;
        _session = session;
        ReloadSources();
    }

    public ObservableCollection<SourcePick> Sources { get; } = new();

    public ObservableCollection<SearchItem> Results { get; } = new();

    public ObservableCollection<Chapter> Chapters { get; } = new();

    /// <summary>正文变化（视图层据此把文本推给 <c>TextViewer</c>）。</summary>
    public event EventHandler? ContentChanged;

    public event EventHandler? StatusChanged;

    public bool HasResults => Results.Count > 0;

    public bool HasChapters => Chapters.Count > 0;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsNotBusy => !IsBusy;

    public string SourceSummary => $"已选 {Sources.Count(s => s.IsSelected)} / {Sources.Count} 个可用书源";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnContentTextChanged(string value) => ContentChanged?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedResultChanged(SearchItem? value)
    {
        Chapters.Clear();
        SelectedChapter = null;
        OnPropertyChanged(nameof(HasChapters));
        if (value is not null) _ = LoadBookAsync(value);
    }

    partial void OnSelectedChapterChanged(Chapter? value)
    {
        if (value is not null) _ = LoadContentAsync(value);
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void ReloadSources()
    {
        var pending = _session.TakePendingSourceUrl();

        Sources.Clear();
        if (_store is not null)
        {
            foreach (var source in _store.LoadAll())
            {
                if (!source.Enabled || source.Capability != SourceCapability.Ready) continue;
                var selected = pending is null || string.Equals(pending, source.Url, StringComparison.OrdinalIgnoreCase);
                Sources.Add(new SourcePick(source, selected));
            }
        }

        OnPropertyChanged(nameof(SourceSummary));
        StatusText = Sources.Count == 0
            ? "没有可用书源 —— 先到「阅读 → 书源」导入 JSON。"
            : $"可用书源 {Sources.Count} 个";

        CheckPendingBook();
    }

    /// <summary>
    /// 取走并打开「待读的书」（书架 / 发现页带过来的）。
    /// <para>🔴 标签页复用时 VM 不会重建 ⇒ 每次页面重新挂载都要再查一次，否则第二次跳转没反应。</para>
    /// </summary>
    public void CheckPendingBook()
    {
        if (_session.TakePendingBook() is { } pending) OpenPending(pending);
    }

    /// <summary>从书架 / 发现页带过来的书：预选它的源并在结果里打开。</summary>
    private void OpenPending(PendingBook pending)
    {
        var source = FindSource(pending.SourceUrl);
        if (source is null)
        {
            ErrorText = $"《{pending.Name}》的来源书源已删除或已停用，换一个源重新搜。";
            return;
        }

        foreach (var pick in Sources)
            pick.IsSelected = string.Equals(pick.Source.Url, pending.SourceUrl, StringComparison.OrdinalIgnoreCase);

        var item = new SearchItem
        {
            Name = pending.Name,
            BookUrl = pending.BookUrl,
            SourceUrl = pending.SourceUrl,
            SourceName = source.Name,
        };

        Results.Insert(0, item);
        OnPropertyChanged(nameof(HasResults));
        SelectedResult = item;
    }

    /// <summary>全选 / 全不选书源。</summary>
    [RelayCommand]
    private void SelectAllSources()
    {
        foreach (var pick in Sources) pick.IsSelected = true;
        OnPropertyChanged(nameof(SourceSummary));
    }

    [RelayCommand]
    private void ClearSources()
    {
        foreach (var pick in Sources) pick.IsSelected = false;
        OnPropertyChanged(nameof(SourceSummary));
    }

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

    /// <summary>上一章。</summary>
    [RelayCommand]
    private void PrevChapter()
    {
        if (Chapters.Count == 0) return;
        var index = SelectedChapter is null ? 0 : Chapters.IndexOf(SelectedChapter);
        if (index > 0) SelectedChapter = Chapters[index - 1];
    }

    /// <summary>下一章。</summary>
    [RelayCommand]
    private void NextChapter()
    {
        if (Chapters.Count == 0) return;
        var index = SelectedChapter is null ? -1 : Chapters.IndexOf(SelectedChapter);
        if (index >= 0 && index < Chapters.Count - 1) SelectedChapter = Chapters[index + 1];
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
        Chapters.Clear();
        SelectedChapter = null;
        ErrorText = "";
        BookInfoText = "";
        ContentTitle = "";
        ContentText = "";
        IsBusy = true;
        BusyPhase = $"正在用 {picks.Count} 个书源搜索「{keyword}」…";

        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
                BusyPhase = $"正在搜索… 已完成 {p.Done} / {p.Total} 个源");

            var results = await _crawl.SearchManyAsync(picks, keyword, 1, token, progress).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            var merged = results.Where(r => r.Ok).SelectMany(r => r.Items).ToList();
            foreach (var item in merged) Results.Add(item);

            OnPropertyChanged(nameof(HasResults));

            var failed = results.Where(r => !r.Ok).ToList();
            ErrorText = failed.Count == 0
                ? ""
                : string.Join("\n", failed.Take(5).Select(r => r.Source.Name + " · " + r.Error!.Display));

            StatusText = $"命中 {merged.Count} 条 · 成功 {results.Count(r => r.Ok)} / {results.Count} 个源";
            if (merged.Count == 0 && failed.Count > 0) StatusText = "⚠ 没有命中，下面是被拒绝或失败的原因";
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

    // ==================== 目录 / 正文 ====================

    private BookSource? FindSource(string url)
        => Sources.FirstOrDefault(s => string.Equals(s.Source.Url, url, StringComparison.OrdinalIgnoreCase))?.Source;

    private async Task LoadBookAsync(SearchItem item)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var source = FindSource(item.SourceUrl);
        if (source is null) { ErrorText = "找不到这条结果对应的书源。"; return; }

        IsBusy = true;
        BusyPhase = "正在读详情与目录…";
        ErrorText = "";

        try
        {
            var detail = await _crawl.DetailAsync(source, item.BookUrl, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            var name = string.IsNullOrWhiteSpace(detail.Value?.Name) ? item.Name : detail.Value!.Name;
            var author = string.IsNullOrWhiteSpace(detail.Value?.Author) ? item.Author : detail.Value!.Author;
            var tocUrl = detail.Value?.TocUrl ?? item.BookUrl;

            var chapters = await _crawl.ChaptersAsync(source, tocUrl, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            Chapters.Clear();
            if (chapters.Value is not null)
                foreach (var chapter in chapters.Value) Chapters.Add(chapter);

            OnPropertyChanged(nameof(HasChapters));

            BookInfoText = $"{name} · {author} · {Chapters.Count} 章";

            var errors = new[] { detail.Error, chapters.Error }.Where(e => e is not null).Select(e => e!.Display).ToList();
            ErrorText = string.Join("\n", errors);

            StatusText = Chapters.Count > 0
                ? $"《{name}》共 {Chapters.Count} 章 · 点章节看正文"
                : "⚠ 目录为空";

            if (Chapters.Count > 0) SelectedChapter = Chapters[0];
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = "读目录异常：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }
    }

    private async Task LoadContentAsync(Chapter chapter)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var source = SelectedResult is null ? null : FindSource(SelectedResult.SourceUrl);

        if (source is null) return;
        if (string.IsNullOrWhiteSpace(chapter.Url)) { ErrorText = "这一章没有链接（目录是纯标题）。"; return; }

        IsBusy = true;
        BusyPhase = "正在抓正文…";
        ErrorText = "";
        ContentTitle = chapter.Name;

        try
        {
            var content = await _crawl.ContentAsync(source, chapter.Url, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            ContentText = content.Value ?? "";
            ErrorText = content.Error?.Display ?? "";
            StatusText = ContentText.Length > 0 ? $"《{chapter.Name}》{ContentText.Length} 字" : "⚠ 正文为空";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = "抓正文异常：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }
    }

    public void Dispose()
    {
        Cancel();
        ContentChanged = null;
        StatusChanged = null;
    }
}
