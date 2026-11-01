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

/// <summary>
/// 「阅读 → 发现」页（<c>reading-explore</c>）：按书源的 <c>exploreUrl</c> 分类浏览（不用输关键词）。
/// <para>三栏：选源（只列真的带分类表的源） → 分类 → 该分类下的书；点书跳到「网上搜索」读目录与正文。</para>
/// </summary>
public partial class ExploreViewModel : ObservableObject, IStatusProvider, IDisposable
{
    private readonly SourceStore _store;
    private readonly CrawlService _crawl;
    private readonly ReadingSession _session;
    private readonly INavigationService? _nav;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private BookSource? _selectedSource;
    [ObservableProperty] private ExploreCategory? _selectedCategory;
    [ObservableProperty] private SearchItem? _selectedResult;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyPhase = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _statusText = "就绪";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public ExploreViewModel() : this(null!, null!, new ReadingSession()) { }

    public ExploreViewModel(SourceStore store, CrawlService crawl, ReadingSession session, INavigationService? nav = null)
    {
        _store = store;
        _crawl = crawl;
        _session = session;
        _nav = nav;
        ReloadSources();
    }

    /// <summary>带分类表的书源（只有这些能「发现」）。</summary>
    public ObservableCollection<BookSource> Sources { get; } = new();

    public ObservableCollection<ExploreCategory> Categories { get; } = new();

    public ObservableCollection<SearchItem> Results { get; } = new();

    public event EventHandler? StatusChanged;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsNotBusy => !IsBusy;

    public bool HasResults => Results.Count > 0;

    public bool HasSource => SelectedSource is not null;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnSelectedSourceChanged(BookSource? value)
    {
        OnPropertyChanged(nameof(HasSource));
        Categories.Clear();
        Results.Clear();
        SelectedCategory = null;
        SelectedResult = null;
        OnPropertyChanged(nameof(HasResults));

        if (value is null) return;

        foreach (var category in CrawlService.ParseExploreCategories(value)) Categories.Add(category);

        if (Categories.Count == 0)
        {
            ErrorText = "这个源没有可用的发现页分类（exploreUrl 为空或全是脚本）。改用「网上搜索」输关键词。";
            StatusText = "无可用的发现分类";
            return;
        }

        ErrorText = "";
        StatusText = $"《{value.Name}》共 {Categories.Count} 个分类";
        SelectedCategory = Categories[0];
    }

    partial void OnSelectedCategoryChanged(ExploreCategory? value)
    {
        Results.Clear();
        SelectedResult = null;
        OnPropertyChanged(nameof(HasResults));
        if (value is not null) _ = LoadCategoryAsync(value);
    }

    /// <summary>点「读这本书」：把书交给搜索页（那里有目录 + 正文）。</summary>
    [RelayCommand]
    private void Read()
    {
        if (SelectedResult is null) { StatusText = "先选一本书。"; return; }
        if (SelectedSource is null) return;

        _session.PendingBook = new PendingBook(SelectedSource.Url, SelectedResult.BookUrl, SelectedResult.Name);
        _nav?.NavigateTo("reading-search");
    }

    [RelayCommand]
    private void ReloadSources()
    {
        Sources.Clear();
        if (_store is not null)
        {
            foreach (var source in _store.LoadAll())
            {
                if (!source.Enabled || source.Capability != SourceCapability.Ready) continue;
                if (string.IsNullOrWhiteSpace(source.ExploreUrl)) continue;
                Sources.Add(source);
            }
        }

        StatusText = Sources.Count == 0
            ? "没有带分类表的可用书源 —— 先用「网上搜索」输关键词。"
            : $"带分类表的书源 {Sources.Count} 个";

        if (Sources.Count > 0) SelectedSource = Sources[0];
    }

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { /* 已释放 */ }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task LoadCategoryAsync(ExploreCategory category)
    {
        if (_crawl is null || SelectedSource is null) return;

        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        BusyPhase = $"正在读分类「{category.Name}」…";
        ErrorText = "";

        try
        {
            var result = await _crawl.ExploreAsync(SelectedSource, category, 1, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            if (result.Value is not null)
                foreach (var item in result.Value) Results.Add(item);

            OnPropertyChanged(nameof(HasResults));
            ErrorText = result.Error?.Display ?? "";
            StatusText = $"「{category.Name}」{Results.Count} 本";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = "读分类异常：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }
    }

    public void Dispose()
    {
        Cancel();
        StatusChanged = null;
    }
}
