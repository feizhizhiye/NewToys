using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
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
public partial class BookmarkViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条 + 选择器命令（NT-055 / 需求 §4.8.5） ====================

    /// <summary>
    /// 📥 导入书签：弹文件选择器（宿主经 <c>Core.DialogHooks</c> 注入 —— VM ⛔ 不引 Avalonia），
    /// 选完把 HTML 交给 <see cref="ImportBookmarksFromFileAsync"/>。
    /// </summary>
    [RelayCommand]
    private async Task ImportBookmarksAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择浏览器导出的书签 HTML", ImportExportDefaultDir, false, "*.html;*.htm");
        var file = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(file)) return;
        await ImportBookmarksFromFileAsync(file);
    }

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📥 导入书签", Classes = "primary", CommandPath = nameof(ImportBookmarksCommand), ToolTip = "选择浏览器导出的书签 HTML 文件" },
        new() { Text = "➕ 新建", CommandPath = nameof(NewBookmarkCommand) },
        new() { Text = "🔗 检测全部", CommandPath = nameof(CheckAllCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Text = "🚀 批量打开", CommandPath = nameof(OpenBatchCommand) },
        new() { Text = "🔄 重读书签", CommandPath = nameof(RefreshCommand), ToolTip = "重新读一遍本地书签配置（⛔ 不联网、不导浏览器）" },
        new() { Kind = ToolbarItemKind.TextField, Width = 220, Placeholder = "搜索标题 / 网址 / 分组…", ValuePath = nameof(SearchText) },
        new() { Kind = ToolbarItemKind.Label, Text = "右键列表项：联通检测 · 批量打开 · 打开 · 发送到抓取 · 编辑 · 删除" },
    };

    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly BookmarkService _svc = null!;
    private readonly INavigationService? _nav;
    private readonly WebTransferService _transfer = null!;
    private readonly List<BookmarkItem> _all = new();

    public ObservableCollection<BookmarkItem> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;
    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
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
        // v3.32 §4.8.3：每列给一条示例值（空态水印行 + 列宽判定）
        new("Title", "标题", "2.6*", sample: "NewToys 项目主页"),
        new("Url", "网址", "3*", sample: "https://example.com/newtoys"),
        new("Folder", "分组", "1.4*", sample: "工作"),
        new("Status", "状态", "1.2*", sample: "HTTP 200"),
    };

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    // ==================== v3.32 §4.8.3：列表示例行 + 详情卡示例态 ====================

    /// <summary>详情卡 / 列表示例行用的**示例书签**。</summary>
    private static readonly BookmarkItem SampleItem = new()
    {
        Title = "NewToys 项目主页",
        Url = "https://example.com/newtoys",
        Folder = "工作",
        Notes = "（示例数据）这里可以放备注。",
        IsBatchOpen = true,
        CheckStatus = "HTTP 200",
        CheckOk = true,
        CheckedAt = new DateTime(2026, 10, 13, 15, 0, 0)
    };

    /// <summary>列表为空时显示**示例行**（⛔ 不进 <c>Items</c>、不参与统计 / 筛选 / 排序）。</summary>
    public bool ShowSampleRow => HasSampleRow && Items.Count == 0;

    /// <summary>详情卡的数据源：有选中项用真值，没选中用**示例书签**。</summary>
    public BookmarkItem DetailItem => Selected ?? SampleItem;

    /// <summary>详情卡正文是否显示（有选中项，或处于示例态）。</summary>
    public bool ShowDetailBody => HasSelection || IsDetailSample;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    // ==================== 详情面板 + 声明式详情卡（NT-054 的 L2 迁移页，需求 §4.5.1） ====================

    /// <summary>
    /// 「最近检测」块是否要显示（原 XAML 用 <c>ObjectConverters.IsNotNull</c>，描述符只认布尔路径）。
    /// </summary>
    public bool HasCheckStatus => !string.IsNullOrWhiteSpace(DetailItem.CheckStatus);

    /// <summary>检测时间的展示串（原 XAML 走 <c>StringFormat</c>，描述符不支持格式化 ⇒ 这里先拼好）。</summary>
    public string CheckedAtDisplay => DetailItem.CheckedAt is { } t ? t.ToString("yyyy-MM-dd HH:mm") : "";

    private void NotifyCheckInfo()
    {
        OnPropertyChanged(nameof(HasCheckStatus));
        OnPropertyChanged(nameof(CheckedAtDisplay));
    }

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    /// <summary>工具条末位的详情开关 → 走本命令（⛔ 基类 <c>ToggleDetailCommand</c> 不通知 Shell，右列不会跟着开合）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的页内自绘卡片对应：标题 / 网址 / 分组 / 备注 / 加入批量打开 + 最近检测块 + 四个卡内动作。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        SamplePath = nameof(IsDetailSample),
        EmptyText = "在左侧列表里选中一条书签",
        SampleText = "（示例数据）在左侧选中一条书签后，这里显示它的真实内容；示例态下不可编辑。",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "标题",
                        ValuePath = "DetailItem.Title",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "网址",
                        ValuePath = "DetailItem.Url",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "分组",
                        ValuePath = "DetailItem.Folder",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Placeholder = "如：工作 / 学习"
                    },
                    new DetailField
                    {
                        Label = "备注",
                        ValuePath = "DetailItem.Notes",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        MultiLine = true,
                        Rows = 80
                    },
                    new DetailField
                    {
                        Label = "加入「批量打开」",
                        ValuePath = "DetailItem.IsBatchOpen",
                        Kind = FieldKind.Check
                    },
                    new DetailField
                    {
                        Label = "最近检测",
                        ValuePath = "DetailItem.CheckStatus",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        VisibleWhen = nameof(HasCheckStatus)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Prefix = "检测时间：",
                        ValuePath = nameof(CheckedAtDisplay),
                        VisibleWhen = nameof(HasCheckStatus)
                    }
                }
            }
        },
        Actions =
        {
            // 原来这四个按钮是页内卡里的 WrapPanel（只在有选中 / 示例态之外显示）；声明式卡里进 Actions 区。
            new DetailAction
            {
                Text = "💾 保存",
                Classes = "primary",
                ToolTip = "把当前内容写回书签文件",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = SaveCommand
            },
            new DetailAction
            {
                Text = "🌐 打开",
                Classes = "ghost",
                ToolTip = "在默认浏览器里打开这条书签",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = OpenCommand
            },
            new DetailAction
            {
                Text = "🔗 检测",
                Classes = "ghost",
                ToolTip = "检测这条书签的连通性（HTTP 状态）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = CheckCommand
            },
            new DetailAction
            {
                Text = "🗑 删除",
                Classes = "ghost",
                ToolTip = "删除这条书签",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = DeleteCommand
            }
        }
    };

    private readonly IAppConfigService? _app;

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _app?.VideoDbFolder ?? "";

    public BookmarkViewModel() { }

    public BookmarkViewModel(BookmarkService svc, IColumnLayoutService cols, INavigationService? nav, WebTransferService transfer, IAppConfigService app)
        : base(cols)
    {
        _svc = svc;
        _nav = nav;
        _transfer = transfer;
        _app = app;
        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        UpdateDetailSample(false);        // v3.32 §4.8.3：初始没有选中项 → 详情卡进"示例态"
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
        NotifyCheckInfo();
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
        finally { IsBusy = false; ApplyView(); NotifyCheckInfo(); }
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
        OnPropertyChanged(nameof(ShowSampleRow));   // v3.32：有真实数据后示例行消失
        UpdateDetailSample(Selected != null);
    }

    private void UpdateHeaders()
    {
        TitleHeader = "标题" + SortArrow("Title");
    }

    partial void OnSelectedChanged(BookmarkItem? value)
    {
        UpdateDetailSample(value != null);   // v3.32 §4.8.3：没选中项 → 详情卡改用示例书签
        OnPropertyChanged(nameof(DetailItem));
        NotifyCheckInfo();      // 切选中项 / 示例态 → 「最近检测」块跟着换
        OnPropertyChanged(nameof(HasSelection));
    }
    partial void OnSearchTextChanged(string value) => ApplyView();
}
