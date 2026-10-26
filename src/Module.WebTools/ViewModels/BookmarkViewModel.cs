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
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.UI.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 书签管理页：整页一棵树（文件夹行 + 书签行同列表混排，缩进表示层级）+ 右侧详情卡。
/// <para>
/// 功能：导入浏览器书签（AngleSharp 解析 Netscape HTML，保留文件夹层级）、展开 / 折叠、
/// 联通检测、批量打开、跨页发送到网页抓取、按所选浏览器配置打开。
/// 列显隐 / 详情开合偏好落 <c>config\columnlayout.json</c>。
/// </para>
/// <para>
/// 🔴 树⛔ 用 <c>TreeView</c>：它会把整行（含网址 / 状态列）随层级右移，与表头必然错位。
/// 这里按先序把可见节点拍平进 <see cref="VisibleNodes"/>，缩进只作用第一列的占位 Border。
/// </para>
/// </summary>
public partial class BookmarkViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条（Shell 承载） ====================

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
        new() { Text = "📥 导入书签", Classes = "ghost", CommandPath = nameof(ImportBookmarksCommand), ToolTip = "选择浏览器导出的书签 HTML 文件（保留文件夹层级）" },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索标题 / 网址 / 分组…", ValuePath = nameof(SearchText) },
        new() { Text = "➕ 新建", CommandPath = nameof(NewBookmarkCommand), ToolTip = "新书签落在当前选中的文件夹下" },
        new() { Text = "🔗 检测全部", CommandPath = nameof(CheckAllCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Text = "🚀 批量打开", CommandPath = nameof(OpenBatchCommand) },
        new() { Text = "🔄 重读书签", CommandPath = nameof(RefreshCommand), ToolTip = "重新读一遍本地书签库（⛔ 不联网、不导浏览器）" },
        new()
        {
            Kind = ToolbarItemKind.Combo,
            Width = 190,
            ItemsPath = nameof(OpenWithOptions),
            DisplayPath = nameof(OpenWithOption.Label),
            SelectedItemPath = nameof(SelectedOpenWith),
            ToolTip = "「打开 / 批量打开 / 全部打开」用哪个浏览器（复用「设置 → 启动与行为」的浏览器1 / 浏览器2）"
        }
    };

    private readonly BookmarkService _svc = null!;
    private readonly INavigationService? _nav;
    private readonly WebTransferService _transfer = null!;
    private readonly ILaunchConfigService? _launch;
    private readonly LaunchRunner? _runner;

    /// <summary>全量书签（存储顺序无关，树的派生源）。</summary>
    private readonly List<BookmarkItem> _all = new();

    /// <summary>用户折叠过的文件夹路径（只记内存，⛔ 不持久化）；重建树时据此恢复展开态。</summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>完整树（数据变化才重建）；筛选时另建临时子树，⛔ 不改它。</summary>
    private ObservableCollection<BookmarkNode> _tree = new();

    /// <summary>当前展示的树（完整树 或 筛选后的临时子树）。</summary>
    private ObservableCollection<BookmarkNode> _display = new();

    /// <summary>当前可见行（先序拍平：文件夹行的展开子节点紧跟其后）。列表绑这个。</summary>
    public ObservableCollection<BookmarkNode> VisibleNodes { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";

    /// <summary>列表选中行（文件夹行 / 书签行都可能）。列表 <c>SelectedItem</c> 绑它。</summary>
    [ObservableProperty] private BookmarkNode? _selectedNode;

    /// <summary>当前选中行指向的书签（文件夹行 / 无选中 = null）。详情卡与各条命令取它。</summary>
    [ObservableProperty] private BookmarkItem? _selected;

    /// <summary>打开方式（系统默认 / 浏览器1 / 浏览器2）。</summary>
    public ObservableCollection<OpenWithOption> OpenWithOptions { get; } = new();

    [ObservableProperty] private OpenWithOption? _selectedOpenWith;

    // ============ 表头 ============
    [ObservableProperty] private string _titleHeader = "标题 ▲";
    [ObservableProperty] private string _urlHeader = "网址";
    [ObservableProperty] private string _statusHeader = "状态";

    public bool IsTitleVisible => Vis("Title");
    public bool IsUrlVisible => Vis("Url");
    public bool IsStatusVisible => Vis("Status");

    protected override string PageKey => "web-bookmark";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；「分组」列已删 —— 树缩进本身表达层级。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Title", "标题", "2.6*", sample: "NewToys 项目主页"),
        new("Url", "网址", "3*", sample: "https://example.com/newtoys"),
        new("Status", "状态", "1.2*", sample: "HTTP 200"),
    };

    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (SelectedNode == null && VisibleNodes.Count > 0) SelectedNode = VisibleNodes[0];
    }

    public bool HasSelection => SelectedNode != null;
    public bool HasItems => VisibleNodes.Count > 0;

    /// <summary>当前选中的是文件夹行（决定详情卡换卡、文件夹级动作可用性）。</summary>
    public bool IsFolderSelected => SelectedNode?.IsFolder == true;

    /// <summary>当前选中的是书签行。</summary>
    public bool IsItemSelected => SelectedNode?.IsBookmark == true;

    // ==================== 详情面板 + 声明式详情卡 ====================

    /// <summary>详情卡的数据源：有选中书签时显示它（文件夹行 = null）。</summary>
    public BookmarkItem? DetailItem => Selected;

    public bool ShowDetailBody => SelectedNode != null;

    public bool HasCheckStatus => !string.IsNullOrWhiteSpace(DetailItem?.CheckStatus);

    /// <summary>检测时间的展示串（描述符不支持格式化 ⇒ 这里先拼好）。</summary>
    public string CheckedAtDisplay => DetailItem?.CheckedAt is { } t ? t.ToString("yyyy-MM-dd HH:mm") : "";

    /// <summary>文件夹行的路径摘要（根级 = 未分组）。</summary>
    public string FolderPathText =>
        SelectedNode?.IsFolder == true ? (SelectedNode.Path.Length > 0 ? SelectedNode.Path : "（未分组）") : "";

    /// <summary>文件夹行的书签数（含子孙）。</summary>
    public string FolderCountText => SelectedNode?.IsFolder == true ? $"{SelectedNode.Count} 条" : "";

    private void NotifyCheckInfo()
    {
        OnPropertyChanged(nameof(HasCheckStatus));
        OnPropertyChanged(nameof(CheckedAtDisplay));
    }

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不自绘视图。</summary>
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

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _itemCard;
    private DetailCardDescriptor? _folderCard;

    /// <summary>
    /// 详情卡描述符（C# 对象字面量）：<b>书签行与文件夹行是两张卡</b> —— 字段与动作都不同，
    /// 选中类型变化时通知 Shell 重挂（两张卡是不同实例，Shell 侧属性值一定发生变化）。
    /// </summary>
    public DetailCardDescriptor? Card => IsFolderSelected
        ? _folderCard ??= BuildFolderCard()
        : _itemCard ??= BuildItemCard();

    private DetailCardDescriptor BuildItemCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一条书签",
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
                        Placeholder = "如：工作/项目A（/ 分隔层级）"
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
            new DetailAction
            {
                Text = "💾 保存",
                Classes = "primary",
                ToolTip = "把当前内容写回书签库",
                IsVisiblePath = nameof(IsItemSelected),
                Command = SaveCommand
            },
            new DetailAction
            {
                Text = "🌐 打开",
                Classes = "ghost",
                ToolTip = "按工具条的「打开方式」打开这条书签",
                IsVisiblePath = nameof(IsItemSelected),
                Command = OpenCommand
            },
            new DetailAction
            {
                Text = "🔗 检测",
                Classes = "ghost",
                ToolTip = "检测这条书签的连通性（HTTP 状态）",
                IsVisiblePath = nameof(IsItemSelected),
                Command = CheckCommand
            },
            new DetailAction
            {
                Text = "📌 写入浏览器配置",
                Classes = "ghost",
                ToolTip = "把该网址追加进「打开方式」所选浏览器的网址列表（追加 + 去重）",
                IsVisiblePath = nameof(IsItemSelected),
                IsEnabledPath = nameof(CanWriteToBrowserConfig),
                Command = WriteToBrowserConfigCommand
            },
            new DetailAction
            {
                Text = "🗑 删除",
                Classes = "ghost",
                ToolTip = "删除这条书签",
                IsVisiblePath = nameof(IsItemSelected),
                Command = DeleteCommand
            }
        }
    };

    /// <summary>文件夹行的卡：只有路径 / 条数 + 两个子树级动作（保存 / 删除 / 送抓取对文件夹无意义）。</summary>
    private DetailCardDescriptor BuildFolderCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一条书签或文件夹",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "文件夹",
                        ValuePath = nameof(FolderPathText),
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "书签数",
                        ValuePath = nameof(FolderCountText),
                        Kind = FieldKind.Text,
                        Selectable = true
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "🌐 全部打开",
                Classes = "primary",
                ToolTip = "把这个文件夹下（含子文件夹）的所有网址交给所选浏览器 —— 不改设置里的网址列表",
                IsVisiblePath = nameof(IsFolderSelected),
                Command = OpenFolderCommand
            },
            new DetailAction
            {
                Text = "🔗 全部检测",
                Classes = "ghost",
                ToolTip = "逐条检测这个文件夹下的所有书签",
                IsVisiblePath = nameof(IsFolderSelected),
                Command = CheckFolderCommand
            }
        }
    };

    private readonly IAppConfigService? _app;

    /// <summary>导入 / 导出默认目录（当前配置档 <c>data\</c>）。</summary>
    public string ImportExportDefaultDir => _app?.DataDirectory ?? "";

    public BookmarkViewModel() { }

    public BookmarkViewModel(BookmarkService svc, IColumnLayoutService cols, INavigationService? nav,
        WebTransferService transfer, IAppConfigService app,
        ILaunchConfigService launch, LaunchRunner runner)
        : base(cols)
    {
        _svc = svc;
        _nav = nav;
        _transfer = transfer;
        _app = app;
        _launch = launch;
        _runner = runner;

        LoadLayout();
        Refresh();

        // 跨页「发送到书签」：消费待处理 URL，直接建一条待补全的书签并打开详情。
        var pending = _transfer.TakeUrl();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            var draft = new BookmarkItem { Url = pending, Title = pending };
            _all.Add(draft);
            RebuildTree();
            ApplyView();
            SelectedNode = FindNode(draft);
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
        foreach (var b in _svc.Load()) _all.Add(b);
        RebuildTree();
        ApplyView();
        RebuildOpenWith();
        UpdateStatus();
    }

    /// <summary>按当前数据重建完整树（⛔ 不动折叠记忆，重建时按路径恢复展开态）。</summary>
    private void RebuildTree() => _tree = BuildTree(_all, forceExpand: false);

    private void UpdateStatus()
    {
        var folders = CountFolders(_tree);
        StatusText = folders > 0 ? $"共 {_all.Count} 条书签 · {folders} 个文件夹" : $"共 {_all.Count} 条书签";
    }

    // ============ 导入浏览器书签 ============

    public async Task ImportBookmarksFromFileAsync(string path)
    {
        try
        {
            IsBusy = true;
            var html = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var res = _svc.ImportHtml(html, _all);
            if (res.Added == 0) { StatusText = "没有新的书签（可能都已存在）。"; return; }

            foreach (var b in res.Items) _all.Add(b);
            RebuildTree();
            ApplyView();
            await PersistAsync();
            StatusText = $"已导入 {res.Added} 条书签" + (res.Folders > 0 ? $"（含 {res.Folders} 个文件夹）" : "");
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
        // 落在当前选中文件夹：选中文件夹行 = 它本身；选中书签行 = 它所属文件夹；无选中 = 未分组
        var folder = SelectedNode is { IsFolder: true } f ? f.Path : SelectedNode?.Item?.Folder ?? "";
        var b = new BookmarkItem { Title = "新书签", Url = "https://", Folder = folder };
        _all.Add(b);
        ExpandAncestors(folder);
        RebuildTree();
        ApplyView();
        SelectedNode = FindNode(b);
        IsDetailVisible = true;
        SaveDetailFlag();
        UpdateStatus();
        StatusText = "已新建书签，填写信息后点「保存」。";
    }

    [RelayCommand(CanExecute = nameof(IsItemSelected))]
    private async Task SaveAsync()
    {
        if (Selected is not { } item) return;
        if (string.IsNullOrWhiteSpace(item.Url)) { StatusText = "网址不能为空。"; return; }
        await PersistAsync();
        ExpandAncestors(item.Folder);
        RebuildTree();          // 分组是路径 ⇒ 改分组可能换父节点
        ApplyView();
        UpdateStatus();
        SelectedNode = FindNode(item) ?? SelectedNode;
        StatusText = $"已保存「{item.Title}」。";
    }

    [RelayCommand(CanExecute = nameof(IsItemSelected))]
    private async Task DeleteAsync()
    {
        if (Selected is not { } gone) return;
        _all.Remove(gone);
        await PersistAsync();
        RebuildTree();
        ApplyView();
        if (SelectedNode?.Item == gone) SelectedNode = null;
        UpdateStatus();
        StatusText = "已删除书签。";
    }

    /// <summary>把某条路径及其全部祖先记为展开（新建 / 改分组后要能立刻看到那一行）。</summary>
    private void ExpandAncestors(string folder)
    {
        var path = folder;
        while (!string.IsNullOrEmpty(path))
        {
            _collapsed.Remove(path);
            var cut = path.LastIndexOf('/');
            path = cut <= 0 ? "" : path[..cut];
        }
    }

    private async Task PersistAsync() => await Task.Run(() => _svc.Save(_all.ToList()));

    // ============ 联通检测 ============

    [RelayCommand(CanExecute = nameof(IsItemSelected))]
    private async Task CheckAsync()
    {
        if (Selected is not { } item) return;
        await CheckOneAsync(item);
        NotifyRowsChanged();
        StatusText = $"已检测：{item.CheckStatus}";
    }

    [RelayCommand]
    private async Task CheckAllAsync()
    {
        if (_all.Count == 0) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在检测 {_all.Count} 条书签的连通性……";
            await RunChecksAsync(_all);
            StatusText = "批量检测完成。";
        }
        finally { IsBusy = false; NotifyRowsChanged(); }
    }

    /// <summary>文件夹行的「🔗 全部检测」：只测该子树。</summary>
    [RelayCommand(CanExecute = nameof(IsFolderSelected))]
    private async Task CheckFolderAsync()
    {
        if (SelectedNode is not { IsFolder: true } node) return;
        var items = FolderItems(node);
        if (items.Count == 0) { StatusText = "这个文件夹下没有书签。"; return; }
        try
        {
            IsBusy = true;
            StatusText = $"正在检测「{node.DisplayName}」下的 {items.Count} 条书签……";
            await RunChecksAsync(items);
            StatusText = $"「{node.DisplayName}」检测完成（{items.Count} 条）。";
        }
        finally { IsBusy = false; NotifyRowsChanged(); }
    }

    private async Task RunChecksAsync(IReadOnlyCollection<BookmarkItem> items)
    {
        using var sem = new SemaphoreSlim(6);
        await Task.WhenAll(items.Select(async b =>
        {
            await sem.WaitAsync().ConfigureAwait(false);
            try { await CheckOneAsync(b).ConfigureAwait(false); }
            finally { sem.Release(); }
        })).ConfigureAwait(false);
    }

    private async Task CheckOneAsync(BookmarkItem b)
    {
        var (ok, msg) = await _svc.CheckUrlAsync(b.Url).ConfigureAwait(false);
        b.CheckOk = ok;
        b.CheckStatus = msg;
        b.CheckedAt = DateTime.UtcNow;
    }

    /// <summary>源条目被就地改动（检测 / 编辑）后刷行 —— <see cref="BookmarkItem"/> 是普通类，不会自己发通知。</summary>
    private void NotifyRowsChanged()
    {
        foreach (var n in VisibleNodes) n.NotifyChanged();
        NotifyCheckInfo();
    }

    // ============ 打开 / 批量打开 ============

    [RelayCommand(CanExecute = nameof(IsItemSelected))]
    private void Open()
    {
        if (Selected == null) return;
        OpenUrl(Selected.Url);
        StatusText = $"已用「{SelectedOpenWith?.Label}」打开：{Selected.Url}";
    }

    /// <summary>文件夹行的「🌐 全部打开」：子树全部网址交给所选浏览器，⛔ 不写设置里的网址列表。</summary>
    [RelayCommand(CanExecute = nameof(IsFolderSelected))]
    private void OpenFolder()
    {
        if (SelectedNode is not { IsFolder: true } node) return;
        var urls = FolderItems(node).Select(b => b.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        if (urls.Count == 0) { StatusText = "这个文件夹下没有可打开的网址。"; return; }
        OpenUrls(urls);
        StatusText = $"已用「{SelectedOpenWith?.Label}」打开「{node.DisplayName}」下 {urls.Count} 个书签。";
    }

    [RelayCommand(CanExecute = nameof(IsItemSelected))]
    private void ToggleBatch()
    {
        if (Selected == null) return;
        Selected.IsBatchOpen = !Selected.IsBatchOpen;
        StatusText = Selected.IsBatchOpen ? "已加入「批量打开」。" : "已移出「批量打开」。";
    }

    [RelayCommand]
    private void OpenBatch()
    {
        // 优先打开当前选中文件夹的子树 / 当前书签；否则打开所有标记为「批量打开」的。
        List<string> urls;
        if (SelectedNode is { IsFolder: true } node)
            urls = FolderItems(node).Select(b => b.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        else if (Selected != null)
            urls = new List<string> { Selected.Url };
        else
            urls = _all.Where(b => b.IsBatchOpen).Select(b => b.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();

        if (urls.Count == 0) { StatusText = "没有可批量打开的书签（先选中一项，或勾选「批量打开」）。"; return; }
        OpenUrls(urls);
        StatusText = $"已批量打开 {urls.Count} 个书签（{SelectedOpenWith?.Label}）。";
    }

    private void OpenUrl(string url) => OpenUrls(new[] { url });

    /// <summary>按「打开方式」执行：未选具体配置 / 未配程序路径 ⇒ 回退系统默认浏览器。</summary>
    private void OpenUrls(IReadOnlyCollection<string> urls)
    {
        var cfg = SelectedOpenWith?.Config;
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.ProgramPath) || _runner == null)
        {
            foreach (var u in urls) LaunchRunner.OpenUrlDefault(u);
            return;
        }
        _runner.OpenBrowser(cfg.ProgramPath, cfg.Arguments, urls);
    }

    // ============ 写入浏览器配置 ============

    public bool CanWriteToBrowserConfig
        => IsItemSelected && SelectedOpenWith?.Config is { } c && !string.IsNullOrWhiteSpace(c.ProgramPath);

    [RelayCommand(CanExecute = nameof(CanWriteToBrowserConfig))]
    private void WriteToBrowserConfig()
    {
        var cfg = SelectedOpenWith?.Config;
        if (cfg == null || Selected is not { } item || _launch == null) return;

        var url = (item.Url ?? "").Trim();
        if (url.Length == 0) { StatusText = "网址为空，没写入。"; return; }

        var urls = (cfg.Urls ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (urls.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            StatusText = $"该网址已在「{cfg.Name}」里（共 {urls.Count} 条），未重复写入。";
            return;
        }

        urls.Add(url);
        cfg.Urls = string.Join(Environment.NewLine, urls);
        var (ok, message) = _launch.Save(_launch.Config);
        StatusText = ok ? $"已写入「{cfg.Name}」（共 {urls.Count} 条）。" : "写入失败：" + message;
    }

    private void RebuildOpenWith()
    {
        var keep = SelectedOpenWith?.Index ?? 0;
        OpenWithOptions.Clear();
        OpenWithOptions.Add(new OpenWithOption { Index = 0, Label = "系统默认浏览器" });

        var cfg = _launch?.Config;
        if (cfg?.Browser1 is { } b1)
            OpenWithOptions.Add(new OpenWithOption
            {
                Index = 1,
                Label = "浏览器1 · " + (string.IsNullOrWhiteSpace(b1.Name) ? "未命名" : b1.Name) +
                        (string.IsNullOrWhiteSpace(b1.ProgramPath) ? "（未配置）" : ""),
                Config = b1
            });
        if (cfg?.Browser2 is { } b2)
            OpenWithOptions.Add(new OpenWithOption
            {
                Index = 2,
                Label = "浏览器2 · " + (string.IsNullOrWhiteSpace(b2.Name) ? "未命名" : b2.Name) +
                        (string.IsNullOrWhiteSpace(b2.ProgramPath) ? "（未配置）" : ""),
                Config = b2
            });

        SelectedOpenWith = OpenWithOptions.FirstOrDefault(o => o.Index == keep) ?? OpenWithOptions[0];
    }

    // ============ 跨页：发送到网页抓取 ============

    [RelayCommand(CanExecute = nameof(IsItemSelected))]
    private void SendToScrape()
    {
        if (Selected == null) return;
        _transfer.PushUrl(Selected.Url);
        _nav?.NavigateTo("web-scrape");
    }

    // ============ 展开 / 折叠 ============

    /// <summary>双击文件夹整行：翻转展开态（书签行双击放行，留给以后「双击打开网址」）。</summary>
    [RelayCommand(CanExecute = nameof(IsFolderSelected))]
    private void ToggleExpand()
    {
        if (SelectedNode is not { IsFolder: true } node) return;
        SetExpanded(node, !node.IsExpanded);
    }

    /// <summary>键盘 ←：折叠当前文件夹。</summary>
    [RelayCommand(CanExecute = nameof(IsFolderSelected))]
    private void Collapse()
    {
        if (SelectedNode is { IsFolder: true, IsExpanded: true } node) SetExpanded(node, false);
    }

    /// <summary>键盘 →：展开当前文件夹。</summary>
    [RelayCommand(CanExecute = nameof(IsFolderSelected))]
    private void Expand()
    {
        if (SelectedNode is { IsFolder: true, IsExpanded: false } node) SetExpanded(node, true);
    }

    private void SetExpanded(BookmarkNode node, bool expanded)
    {
        node.IsExpanded = expanded;
        if (expanded) _collapsed.Remove(node.Path);
        else _collapsed.Add(node.Path);
        Materialize();      // 可见行集合变 ⇒ 重新拍平（折叠把子孙移出可见集合）
    }

    // ============ 树：派生 / 拍平 / 排序 ============

    /// <summary>
    /// 由扁平书签派生树：按 <see cref="BookmarkItem.Folder"/> 的 <c>/</c> 路径逐级建文件夹节点，
    /// 书签挂到对应父节点（无分组 = 根级）。空文件夹不会出现（树只能从有书签的路径长出）。
    /// </summary>
    private ObservableCollection<BookmarkNode> BuildTree(IEnumerable<BookmarkItem> items, bool forceExpand)
    {
        var roots = new ObservableCollection<BookmarkNode>();
        var folders = new Dictionary<string, BookmarkNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var siblings = roots;
            var path = "";
            var depth = 0;
            foreach (var part in SplitFolder(item.Folder))
            {
                path = path.Length == 0 ? part : path + "/" + part;
                if (!folders.TryGetValue(path, out var folder))
                {
                    folder = new BookmarkNode
                    {
                        Kind = BookmarkNodeKind.Folder,
                        Name = part,
                        Path = path,
                        Depth = depth,
                        IsExpanded = forceExpand || !_collapsed.Contains(path)
                    };
                    folders[path] = folder;
                    siblings.Add(folder);
                }
                siblings = folder.Children;
                depth++;
            }

            siblings.Add(new BookmarkNode
            {
                Kind = BookmarkNodeKind.Bookmark,
                Name = item.Title,
                Path = item.Folder,
                Depth = depth,
                Item = item
            });
        }

        foreach (var folder in folders.Values) folder.Count = CountBookmarks(folder);
        SortLevel(roots);
        return roots;
    }

    private static string[] SplitFolder(string? folder)
        => string.IsNullOrWhiteSpace(folder)
            ? Array.Empty<string>()
            : folder.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int CountBookmarks(BookmarkNode node)
    {
        var n = 0;
        foreach (var child in node.Children) n += child.Item != null ? 1 : CountBookmarks(child);
        return n;
    }

    private static int CountFolders(IEnumerable<BookmarkNode> nodes)
    {
        var n = 0;
        foreach (var node in nodes)
        {
            if (!node.IsFolder) continue;
            n += 1 + CountFolders(node.Children);
        }
        return n;
    }

    /// <summary>
    /// 同级排序（⛔ 不跨层重排，否则树结构被打散）：文件夹恒在前，其余按当前排序列；
    /// 顺序有变才重填集合（避免无谓的视图重建）。
    /// </summary>
    private void SortLevel(ObservableCollection<BookmarkNode> nodes)
    {
        if (nodes.Count > 1)
        {
            var sorted = nodes.ToList();
            sorted.Sort(CompareSiblings);
            if (!sorted.SequenceEqual(nodes))
            {
                nodes.Clear();
                foreach (var n in sorted) nodes.Add(n);
            }
        }
        foreach (var n in nodes) if (n.IsFolder) SortLevel(n.Children);
    }

    private int CompareSiblings(BookmarkNode a, BookmarkNode b)
    {
        if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;   // 文件夹在前（与浏览器收藏夹一致）

        var r = SortColumn switch
        {
            "Url" => string.Compare(a.Url, b.Url, StringComparison.CurrentCultureIgnoreCase),
            "Status" => string.Compare(a.CheckStatus, b.CheckStatus, StringComparison.CurrentCultureIgnoreCase),
            _ => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase)
        };
        return SortDescending ? -r : r;
    }

    /// <summary>先序拍平：只把**当前展开的**节点放进 <see cref="VisibleNodes"/>。</summary>
    private void Materialize()
    {
        // 🔴 先记住选中行：列表清空时 ListBox 会把 SelectedItem 写回 null（TwoWay），清完必须还原
        var keep = SelectedNode;

        VisibleNodes.Clear();

        void Walk(IEnumerable<BookmarkNode> nodes)
        {
            foreach (var node in nodes)
            {
                VisibleNodes.Add(node);
                if (node.IsFolder && node.IsExpanded) Walk(node.Children);
            }
        }

        Walk(_display);

        // 折叠后原选中行可能已不可见 ⇒ 落到最近一个仍可见的祖先文件夹
        if (keep != null)
            SelectedNode = VisibleNodes.Contains(keep) ? keep : NearestVisibleFolder(keep);

        OnPropertyChanged(nameof(HasItems));
    }

    private BookmarkNode? NearestVisibleFolder(BookmarkNode node)
    {
        var path = node.Path;
        while (!string.IsNullOrEmpty(path))
        {
            var hit = VisibleNodes.FirstOrDefault(n =>
                n.IsFolder && string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
            var cut = path.LastIndexOf('/');
            path = cut <= 0 ? "" : path[..cut];
        }
        return null;
    }

    /// <summary>找出当前树里指向某条书签的节点（保存 / 新建后定位用）。</summary>
    private BookmarkNode? FindNode(BookmarkItem item)
        => VisibleNodes.FirstOrDefault(n => ReferenceEquals(n.Item, item));

    /// <summary>文件夹子树里的全部书签（含子文件夹）。</summary>
    private static List<BookmarkItem> FolderItems(BookmarkNode folder)
    {
        var list = new List<BookmarkItem>();

        void Walk(BookmarkNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.Item != null) list.Add(child.Item);
                else Walk(child);
            }
        }

        Walk(folder);
        return list;
    }

    // ============ 视图刷新 ============

    /// <summary>
    /// 重算可见行：搜索 / 快速筛选生效时**重建只含命中书签及其祖先的子树**并强制展开
    /// （否则命中的行藏在折叠里 = 搜到看不见）；无筛选时用完整树。
    /// </summary>
    private void ApplyView()
    {
        _display = IsFiltering ? BuildTree(Filtered().ToList(), forceExpand: true) : _tree;

        SortLevel(_display);
        Materialize();
        UpdateHeaders();

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsFolderSelected));
        OnPropertyChanged(nameof(IsItemSelected));
    }

    private bool IsFiltering => (SearchText ?? "").Trim().Length > 0 || ActiveFilterCount > 0;

    private IEnumerable<BookmarkItem> Filtered()
    {
        IEnumerable<BookmarkItem> q = _all;
        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(b => b.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || b.Url.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || b.Folder.Contains(text, StringComparison.OrdinalIgnoreCase));

        return ApplyFilters(q);     // 多项筛选（基类）：条件之间取「且」
    }

    private void UpdateHeaders()
    {
        TitleHeader = "标题" + SortArrow("Title");
        UrlHeader = "网址" + SortArrow("Url");
        StatusHeader = "状态" + SortArrow("Status");
    }

    partial void OnSelectedNodeChanged(BookmarkNode? value)
    {
        Selected = value?.Item;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsFolderSelected));
        OnPropertyChanged(nameof(IsItemSelected));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(FolderPathText));
        OnPropertyChanged(nameof(FolderCountText));

        OpenCommand.NotifyCanExecuteChanged();
        CheckCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ToggleBatchCommand.NotifyCanExecuteChanged();
        SendToScrapeCommand.NotifyCanExecuteChanged();
        ToggleExpandCommand.NotifyCanExecuteChanged();
        CollapseCommand.NotifyCanExecuteChanged();
        ExpandCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        CheckFolderCommand.NotifyCanExecuteChanged();
        WriteToBrowserConfigCommand.NotifyCanExecuteChanged();

        // 书签卡 ↔ 文件夹卡是两张不同实例 ⇒ 只在**换形态**时通知 Shell 重挂（同形态只换字段，渲染器会自己跟）
        if (value?.Kind != _lastKind)
        {
            _lastKind = value?.Kind;
            OnPropertyChanged(nameof(Card));
            DetailChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private BookmarkNodeKind? _lastKind;

    partial void OnSelectedChanged(BookmarkItem? value)
    {
        OnPropertyChanged(nameof(DetailItem));
        NotifyCheckInfo();
    }

    partial void OnSelectedOpenWithChanged(OpenWithOption? value)
    {
        WriteToBrowserConfigCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ApplyView();
}
