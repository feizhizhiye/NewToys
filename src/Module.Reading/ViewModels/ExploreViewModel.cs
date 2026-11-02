using System;
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

/// <summary>发现页左侧树的一个节点：根 = 书源，子 = 该源的分类。</summary>
public partial class ExploreNode : ObservableObject
{
    public ExploreNode(BookSource source)
    {
        Source = source;
        Name = source.Name;
    }

    public ExploreNode(BookSource source, ExploreCategory category)
    {
        Source = source;
        Category = category;
        Name = category.Name;
    }

    public BookSource Source { get; }

    /// <summary>分类节点才有；根节点（书源）为 <c>null</c>。</summary>
    public ExploreCategory? Category { get; }

    public string Name { get; }

    public bool IsCategory => Category is not null;

    public ObservableCollection<ExploreNode> Children { get; } = new();

    public string Hint => Category is null ? $"{Source.CapabilityText} · {Children.Count} 个分类" : "分类";

    [ObservableProperty] private bool _isExpanded;
}

/// <summary>
/// 「网络阅读 → 发现」页（<c>reading-explore</c>）：按书源的 <c>exploreUrl</c> 分类浏览（不用输关键词）。
/// <para>左侧「书源 → 分类」树（可折叠），右侧书单行列表；点一行弹书籍卡，卡里开读 / 加书架。</para>
/// </summary>
public partial class ExploreViewModel : ObservableObject, IStatusProvider, IDisposable
{
    private readonly SourceStore _store;
    private readonly CrawlService _crawl;
    private readonly ReadingSession _session;
    private readonly ShelfStore _shelf;
    private readonly CoverLoader _covers;
    private readonly INavigationService? _nav;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private ExploreNode? _selectedNode;
    [ObservableProperty] private BookSource? _selectedSource;
    [ObservableProperty] private ExploreCategory? _selectedCategory;
    [ObservableProperty] private SearchItem? _selectedResult;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyPhase = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _statusText = "就绪";

    /// <summary>书籍卡是否展开（点一本书打开；遮罩 / 关闭按钮收起）。</summary>
    [ObservableProperty] private bool _isCardOpen;
    [ObservableProperty] private SearchItem? _cardBook;
    [ObservableProperty] private Bitmap? _cardCover;

    /// <summary>当前分类页码（分类地址里带 <c>{{page}}</c> 才有意义）。</summary>
    [ObservableProperty] private int _currentPage = 1;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public ExploreViewModel() : this(null!, null!, new ReadingSession(), null!, null!) { }

    public ExploreViewModel(SourceStore store, CrawlService crawl, ReadingSession session,
        ShelfStore shelf, CoverLoader covers, INavigationService? nav = null)
    {
        _store = store;
        _crawl = crawl;
        _session = session;
        _shelf = shelf;
        _covers = covers;
        _nav = nav;
        ReloadSources();
    }

    /// <summary>左树的根节点（每个带分类表的书源一个）。</summary>
    public ObservableCollection<ExploreNode> Nodes { get; } = new();

    public ObservableCollection<SearchItem> Results { get; } = new();

    public event EventHandler? StatusChanged;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsNotBusy => !IsBusy;

    public bool HasResults => Results.Count > 0;

    /// <summary>分类地址带 <c>{{page}}</c> ⇒ 才有「上一页 / 下一页」。</summary>
    public bool CanPage => SelectedCategory?.HasPaging ?? false;

    public bool CanPrevPage => CanPage && CurrentPage > 1;

    public string PageText => CanPage ? $"第 {CurrentPage} 页" : "";

    /// <summary>右侧书单区抬头。</summary>
    public string ListTitle => SelectedCategory is null
        ? "选左边的分类"
        : $"{SelectedCategory.Name} · {Results.Count} 本";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnSelectedResultChanged(SearchItem? value)
    {
        if (value is not null) OpenCard(value);
    }

    partial void OnCardBookChanged(SearchItem? value)
    {
        IsCardOpen = value is not null;
        CardCover = null;
        OnPropertyChanged(nameof(CardCoverInitial));
    }

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

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanPrevPage));
        OnPropertyChanged(nameof(PageText));
    }

    partial void OnSelectedNodeChanged(ExploreNode? value)
    {
        if (value is null) return;

        if (!value.IsCategory)
        {
            // 点书源根节点 = 展开 / 折叠（像手机端那颗树）
            value.IsExpanded = !value.IsExpanded;
            return;
        }

        SelectedSource = value.Source;
        SelectedCategory = value.Category;
    }

    /// <summary>树里点了一个分类（视图层 SelectionChanged 转过来）。</summary>
    public void SelectCategory(ExploreNode? node)
    {
        if (node?.Category is null) return;
        SelectedNode = node;
        SelectedSource = node.Source;
        SelectedCategory = node.Category;
    }

    partial void OnSelectedCategoryChanged(ExploreCategory? value)
    {
        Results.Clear();
        SelectedResult = null;
        CardBook = null;
        CurrentPage = 1;
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CanPage));
        OnPropertyChanged(nameof(CanPrevPage));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(ListTitle));

        if (value is not null) _ = LoadCategoryAsync(value, 1);
    }

    /// <summary>分类翻页（只在地址带 <c>{{page}}</c> 时可用）。</summary>
    [RelayCommand]
    private void NextPage()
    {
        if (SelectedCategory is null || !CanPage) return;
        _ = LoadCategoryAsync(SelectedCategory, CurrentPage + 1);
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (SelectedCategory is null || !CanPrevPage) return;
        _ = LoadCategoryAsync(SelectedCategory, CurrentPage - 1);
    }

    /// <summary>列表里点一本书 = 展开书籍卡（阅读 / 加书架都在卡里）。</summary>
    public void OpenCard(SearchItem book)
    {
        if (book is null) return;
        CardBook = book;
        _ = LoadCoverAsync(book);
    }

    [RelayCommand]
    private void CloseCard()
    {
        CardBook = null;
        // 顺手清掉列表选中：否则再点同一行不会触发「选中变化」，卡片打不开第二次
        SelectedResult = null;
    }

    /// <summary>卡片里「开始阅读」→ 进阅读页。</summary>
    [RelayCommand]
    private void ReadCardBook()
    {
        if (CardBook is null) { StatusText = "先选一本书。"; return; }

        var sourceUrl = string.IsNullOrWhiteSpace(CardBook.SourceUrl) ? SelectedSource?.Url ?? "" : CardBook.SourceUrl;
        if (string.IsNullOrWhiteSpace(sourceUrl)) { StatusText = "这条结果没有来源书源。"; return; }

        _session.PendingBook = new PendingBook(sourceUrl, CardBook.BookUrl, CardBook.Name);
        if (_nav is null) { StatusText = "导航服务未就绪，改用「网上搜索」打开这本书。"; return; }
        _nav.NavigateTo("reading-read");
    }

    /// <summary>卡片里「加入书架」。</summary>
    [RelayCommand]
    private void AddCardToShelf()
    {
        if (CardBook is null) { StatusText = "先选一本书。"; return; }

        var book = new ShelfBook
        {
            BookUrl = CardBook.BookUrl,
            SourceUrl = string.IsNullOrWhiteSpace(CardBook.SourceUrl) ? SelectedSource?.Url ?? "" : CardBook.SourceUrl,
            Name = CardBook.Name,
            Author = CardBook.Author,
            CoverUrl = CardBook.CoverUrl,
        };

        StatusText = _shelf is not null && _shelf.Add(book)
            ? $"《{book.Name}》已加入书架"
            : "⚠ 加入书架失败（书库不可用）";
    }

    private async Task LoadCoverAsync(SearchItem book)
    {
        if (_covers is null || string.IsNullOrWhiteSpace(book.CoverUrl)) return;

        var cover = await _covers.LoadAsync(book.CoverUrl).ConfigureAwait(true);
        if (!ReferenceEquals(CardBook, book)) return;   // 期间换了书 ⇒ 丢弃旧封面
        CardCover = cover;
    }

    [RelayCommand]
    private void ReloadSources()
    {
        var expanded = Nodes.Where(n => n.IsExpanded).Select(n => n.Source.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Nodes.Clear();
        if (_store is not null)
        {
            foreach (var source in _store.LoadAll())
            {
                if (!source.Enabled) continue;
                // 发现页只要求「有分类表、且是文本源、且不是 WebView 源」—— 没有 searchUrl 的纯发现源同样可用
                if (source.Capability is SourceCapability.NeedsWebView or SourceCapability.Unsupported) continue;
                if (string.IsNullOrWhiteSpace(source.ExploreUrl)) continue;

                var categories = _crawl.ParseExploreCategories(source);
                if (categories.Count == 0) continue;

                var node = new ExploreNode(source);
                foreach (var category in categories) node.Children.Add(new ExploreNode(source, category));
                node.IsExpanded = expanded.Contains(source.Url) || Nodes.Count == 0;
                Nodes.Add(node);
            }
        }

        StatusText = Nodes.Count == 0
            ? "没有带分类表的可用书源 —— 先用「网上搜索」输关键词。"
            : $"带分类表的书源 {Nodes.Count} 个";

        OnPropertyChanged(nameof(ListTitle));
    }

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { /* 已释放 */ }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task LoadCategoryAsync(ExploreCategory category, int page)
    {
        if (_crawl is null || SelectedSource is null) return;

        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        BusyPhase = category.HasPaging ? $"正在读分类「{category.Name}」第 {page} 页…" : $"正在读分类「{category.Name}」…";
        ErrorText = "";

        try
        {
            var result = await _crawl.ExploreAsync(SelectedSource, category, page, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            Results.Clear();
            if (result.Value is not null)
                foreach (var item in result.Value) Results.Add(item);

            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(ListTitle));
            ErrorText = result.Error?.Display ?? "";

            // 失败就不翻页（免得停在空白的第 N 页）
            if (result.Error is null) CurrentPage = page;

            StatusText = result.Error is null
                ? $"「{category.Name}」{Results.Count} 本" + (category.HasPaging ? $" · 第 {page} 页" : "")
                : $"⚠ 「{category.Name}」没读出来";
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
