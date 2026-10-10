using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
/// 书签管理页（列表 + 详情，对标浏览器收藏夹体验）。
/// <para>
/// 功能：导入浏览器书签（AngleSharp 解析 Netscape HTML）、列表 + 右侧详情编辑、联通检测、
/// 「批量打开」分组、右键「发送到网页抓取」跨页传 URL。列显隐 / 详情开合偏好落
/// <c>config\columnlayout.json</c>（规范 3.6 / 3.8）。
/// </para>
/// </summary>
public partial class BookmarkViewModel : ListPageViewModelBase
{
    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly BookmarkService _svc = null!;
    private readonly INavigationService? _nav;
    private readonly WebTransferService _transfer = null!;
    private readonly List<BookmarkItem> _all = new();

    public ObservableCollection<BookmarkItem> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private BookmarkItem? _selected;
    [ObservableProperty] private string _searchText = "";

    // ============ 表头 ============
    [ObservableProperty] private string _titleHeader = "标题 ▲";
    [ObservableProperty] private string _urlHeader = "网址";
    [ObservableProperty] private string _folderHeader = "分组";
    [ObservableProperty] private string _statusHeader = "状态";

    // ============ 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase（v1.35 收口） ============
    //
    // XAML 只能绑具名属性 → 每列给一个直通属性（真正的状态在基类里，落 config\columnlayout.json）。

    public bool IsTitleVisible => Vis("Title");
    public bool IsUrlVisible => Vis("Url");
    public bool IsFolderVisible => Vis("Folder");
    public bool IsStatusVisible => Vis("Status");

    protected override string PageKey => "web-bookmark";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；最后一列 Detail 是「▤」按钮的固定窄列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Title", "标题", "2.6*"),
        new("Url", "网址", "3*"),
        new("Folder", "分组", "1.4*"),
        new("Status", "状态", "1.2*"),
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

    public BookmarkViewModel() { }

    public BookmarkViewModel(BookmarkService svc, IColumnLayoutService cols, INavigationService? nav, WebTransferService transfer)
        : base(cols)
    {
        _svc = svc;
        _nav = nav;
        _transfer = transfer;
        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        Refresh();

        // 跨页「发送到书签」：消费待处理 URL，直接建一条待补全的书签并打开详情。
        var pending = _transfer.TakeUrl();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            var draft = new BookmarkItem { Url = pending, Title = pending };
            _all.Add(draft);
            Items.Add(draft);
            Selected = draft;
            IsDetailVisible = true;
            SaveDetailFlag();
            StatusText = "已从其他页收到网址，补全标题后点「保存」。";
        }
    }

    // ============ 加载 ============
    [RelayCommand]
    private void Refresh()
    {
        _all.Clear();
        foreach (var b in _svc.Load().OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase))
            _all.Add(b);
        ApplyView();
        StatusText = $"共 {Items.Count} 条书签";
    }

    // ============ 导入浏览器书签（由 View 的「导入」按钮触发文件选择后调用） ============
    public async Task ImportBookmarksFromFileAsync(string path)
    {
        try
        {
            IsBusy = true;
            var html = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var res = _svc.ImportHtml(html, _all);
            if (res.Added == 0) { StatusText = "没有新的书签（可能都已存在）。"; return; }

            foreach (var b in res.Items) _all.Add(b);
            ApplyView();
            await PersistAsync();
            StatusText = $"已导入 {res.Added} 条书签" + (res.Folders > 0 ? $"（含 {res.Folders} 个分组）" : "");
        }
        catch (Exception ex)
        {
            StatusText = "导入失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ============ 增删改 ============
    [RelayCommand]
    private void NewBookmark()
    {
        var b = new BookmarkItem { Title = "新书签", Url = "https://" };
        _all.Add(b);
        Items.Add(b);
        Selected = b;
        IsDetailVisible = true;
        SaveDetailFlag();
        StatusText = "已新建书签，填写信息后点「保存」。";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected == null) return;
        if (string.IsNullOrWhiteSpace(Selected.Url)) { StatusText = "网址不能为空。"; return; }
        await PersistAsync();
        StatusText = $"已保存「{Selected.Title}」。";
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
        StatusText = "已删除书签。";
    }

    private async Task PersistAsync() => await Task.Run(() => _svc.Save(_all.ToList()));

    // ============ 联通检测 ============
    [RelayCommand]
    private async Task CheckAsync()
    {
        if (Selected == null) return;
        await CheckOneAsync(Selected);
        ApplyView();
    }

    [RelayCommand]
    private async Task CheckAllAsync()
    {
        if (Items.Count == 0) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在检测 {Items.Count} 条书签的连通性……";
            var sem = new System.Threading.SemaphoreSlim(6);
            await Task.WhenAll(Items.Select(async b =>
            {
                await sem.WaitAsync();
                try { await CheckOneAsync(b); }
                finally { sem.Release(); }
            }));
            StatusText = "批量检测完成。";
        }
        finally { IsBusy = false; ApplyView(); }
    }

    private async Task CheckOneAsync(BookmarkItem b)
    {
        var (ok, msg) = await _svc.CheckUrlAsync(b.Url).ConfigureAwait(false);
        b.CheckOk = ok;
        b.CheckStatus = msg;
        b.CheckedAt = DateTime.UtcNow;
    }

    // ============ 打开 / 批量打开 ============
    [RelayCommand]
    private void Open()
    {
        if (Selected == null) return;
        LaunchRunner.OpenUrlDefault(Selected.Url);
        StatusText = "已在默认浏览器打开：" + Selected.Url;
    }

    [RelayCommand]
    private void ToggleBatch()
    {
        if (Selected == null) return;
        Selected.IsBatchOpen = !Selected.IsBatchOpen;
        StatusText = Selected.IsBatchOpen ? "已加入「批量打开」。" : "已移出「批量打开」。";
    }

    [RelayCommand]
    private void OpenBatch()
    {
        // 优先打开当前选中的；否则打开所有标记为「批量打开」的。
        var targets = Selected != null
            ? new[] { Selected }
            : Items.Where(b => b.IsBatchOpen).ToArray();
        if (targets.Length == 0) { StatusText = "没有可批量打开的书签（先选中一个，或勾选「批量打开」）。"; return; }
        foreach (var b in targets) LaunchRunner.OpenUrlDefault(b.Url);
        StatusText = $"已批量打开 {targets.Length} 个书签。";
    }

    // ============ 跨页：发送到网页抓取 ============
    [RelayCommand]
    private void SendToScrape()
    {
        if (Selected == null) return;
        _transfer.PushUrl(Selected.Url);
        _nav?.NavigateTo("web-scrape");
    }

    // ============ 排序 ============
    private void ApplyView()
    {
        IEnumerable<BookmarkItem> q = _all;
        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(b => b.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || b.Url.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || b.Folder.Contains(text, StringComparison.OrdinalIgnoreCase));

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        q = SortDescending
            ? q.OrderByDescending(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
            : q.OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase);

        var ordered = q.ToList();
        Items.Clear();
        foreach (var b in ordered) Items.Add(b);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders()
    {
        TitleHeader = "标题" + SortArrow("Title");
    }

    partial void OnSelectedChanged(BookmarkItem? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnSearchTextChanged(string value) => ApplyView();
}
