using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

// Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「资产浏览」页（需求 §6.6.1）—— **资源管理器形态**：左目录树 → 右「图标卡 / 详细信息」双视图 → 双击打开。
/// <para>
/// 🔴 数据来源：**磁盘实时枚举**为主，索引只做叠加。
/// 目录树 = 盘符 / 共享根 + 逐层子目录（展开时懒加载）；右侧 = 该目录的真实内容（子目录 + 全部文件）。
/// 索引命中 ⇒ 这一项「已入库」（有缩略图、可做库操作）；没命中 ⇒ 标灰但仍能打开 / 定位。
/// </para>
/// <para>
/// 与「全部资产」分工：那边按**元数据**找（搜索 / 标签 / 筛选），这里按**磁盘位置**翻。
/// </para>
/// </summary>
public partial class AssetBrowseViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly AssetService _assets = null!;
    private readonly AssetBrowseService _browse = null!;
    private readonly AssetLibraryAddService _lib = null!;
    private readonly AssetThumbCache _thumbCache = null!;
    private readonly MediaAssetsSettingsIO _io = null!;
    private readonly ThumbnailService _thumbGen = null!;
    private readonly FileOperationService _fileOps = null!;
    private readonly TaskQueue _queue = null!;
    private readonly OperationLogService _opLog = null!;
    private readonly ScanService _scan = null!;

    /// <summary>跨模块消息通道（本页 → 视频工具投递待导入的视频路径）。</summary>
    private readonly IModuleBus? _bus;

    /// <summary>模块内导航（发送完顺带跳到视频列表页）。</summary>
    private readonly AssetNavigationStore? _navStore;

    /// <summary>全部可浏览的位置行（一次入内存，用来做「已入库」叠加与树上的计数徽标）。</summary>
    private readonly List<BrowseEntry> _entries = new();

    /// <summary>绝对文件路径 → 索引位置行（命中即「已入库」）。</summary>
    private Dictionary<string, BrowseEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>绝对目录 → 已入库的子孙文件数（树上计数徽标）。</summary>
    private Dictionary<string, int> _libCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前存储源清单（判断「路径是否已入库」只看它，⛔ 不逐项查库）。</summary>
    private List<StorageSource> _storages = new();

    /// <summary>绝对目录 → 目录节点（找父子 / 面包屑用）。</summary>
    private Dictionary<string, AssetDirNode> _nodeByKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>树的顶级节点（盘符 / 共享根）。</summary>
    private readonly List<AssetDirNode> _roots = new();

    /// <summary>读库代数：异步重读期间又发起一次重读 ⇒ 旧结果直接丢。</summary>
    private int _loadVersion;

    /// <summary>当前列表的代数：切目录 / 翻页 / 换尺寸都会 +1，异步回来的旧结果据此作废。</summary>
    private int _viewVersion;

    /// <summary>图标卡每批追加的张数（首屏先给一屏，其余按后台优先级续上）。</summary>
    private const int TileChunkSize = 24;

    /// <summary>存储源 id → 根目录（相对路径换算绝对路径 / 兼容旧持久化键）。</summary>
    private Dictionary<long, string> _storageRoots = new();

    private CancellationTokenSource? _thumbCts;

    /// <summary>构造期卫哨：属性初始赋值不触发重取（数据还没读进来）。</summary>
    private bool _booted;

    /// <summary>当前目录的**未筛选**条目数（空态文案要区分「目录本来就空」与「筛选没命中」）。</summary>
    private int _dirCount;

    // ==================== 绑定集合 ====================

    /// <summary>左树可见节点（按展开状态扁平化）。整体替换而非逐项 Add。</summary>
    public ObservableCollection<AssetDirNode> TreeNodes
    {
        get => _treeNodes;
        private set { _treeNodes = value; OnPropertyChanged(); }
    }

    private ObservableCollection<AssetDirNode> _treeNodes = new();

    /// <summary>右侧当前页的条目（图标卡与详细视图**共用**这一个集合）。</summary>
    public ObservableCollection<AssetTile> Items { get; } = new();

    /// <summary>
    /// 两套视图各自的数据源：**只有当前视图拿得到 <see cref="Items"/>**，另一套为 null。
    /// <para>隐藏的列表照样会为每一项建容器 ⇒ 数据源置空后它不建容器。</para>
    /// </summary>
    public ObservableCollection<AssetTile>? TileSource => IsTileView ? Items : null;

    /// <summary>详细视图的数据源（图标视图下为 null，见 <see cref="TileSource"/>）。</summary>
    public ObservableCollection<AssetTile>? DetailSource => IsTileView ? null : Items;

    /// <summary>选中行（唯一真相；**只装已入库的**，库操作按它走）。</summary>
    public ObservableCollection<AssetRow> SelectedRows { get; } = new();

    /// <summary>选中条目（含未入库的；打开 / 加入资产库按它走）。</summary>
    private readonly List<AssetTile> _selectedTiles = new();

    /// <summary>选中条目快照（含未入库项）。</summary>
    public IReadOnlyList<AssetTile> SelectedTiles => _selectedTiles;

    public ObservableCollection<BrowseCrumb> Crumbs { get; } = new();

    public ObservableCollection<FilterOption> TypeOptions { get; } = new();
    public ObservableCollection<ChoiceItem> ViewModes { get; } = new();
    public ObservableCollection<ChoiceItem> IconSizes { get; } = new();
    public ObservableCollection<ChoiceItem> SortChips { get; } = new();

    // ==================== 页面状态 ====================

    [ObservableProperty] private AssetDirNode? _selectedNode;
    [ObservableProperty] private AssetTile? _selected;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private FilterOption? _filterType;
    [ObservableProperty] private bool _isTileView = true;
    [ObservableProperty] private int _iconSize = 300;
    [ObservableProperty] private bool _isTreeVisible = true;

    /// <summary>正在后台读索引 / 枚举目录。空态文案与工具条可用性都看它。</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>树筛选：只显示已加入资产库的目录（连它们的上级一起留着，否则树只剩盘符）。</summary>
    [ObservableProperty] private bool _onlyLibrary;

    /// <summary>右键瞄准的树节点（⛔ 不改选中、不导航 —— 右键只负责瞄准）。</summary>
    [ObservableProperty] private AssetDirNode? _contextNode;

    /// <summary>图标卡宽度的合法值（当前只一种固定宽度；用于纠正历史配置里残留的 96 / 144 / 200）。</summary>
    public static readonly int[] IconSizeChoices = { 300 };

    /// <summary>图标视图的页大小预设（120 起；详细视图沿用基类默认）。</summary>
    public static readonly int[] TilePageSizes = { 60, 120, 240, 500 };

    // ==================== 表头（详细视图） ====================

    [ObservableProperty] private string _nameHeader = "名称";
    [ObservableProperty] private string _typeHeader = "类型";
    [ObservableProperty] private string _tagsHeader = "标签";
    [ObservableProperty] private string _categoriesHeader = "分类";
    [ObservableProperty] private string _collectionsHeader = "集合";
    [ObservableProperty] private string _sizeHeader = "大小";
    [ObservableProperty] private string _modifiedHeader = "修改时间";
    [ObservableProperty] private string _storageHeader = "存储源";
    [ObservableProperty] private string _durationHeader = "时长";
    [ObservableProperty] private string _resolutionHeader = "分辨率";
    [ObservableProperty] private string _codecHeader = "编码";
    [ObservableProperty] private string _bitrateHeader = "码率";

    public bool IsNameVisible => Vis("Name");
    public bool IsTypeVisible => Vis("Type");
    public bool IsTagsVisible => Vis("Tags");
    public bool IsCategoriesVisible => Vis("Categories");
    public bool IsCollectionsVisible => Vis("Collections");
    public bool IsSizeVisible => Vis("Size");
    public bool IsModifiedVisible => Vis("Modified");
    public bool IsStorageVisible => Vis("Storage");
    public bool IsDurationVisible => Vis("Duration");
    public bool IsResolutionVisible => Vis("Resolution");
    public bool IsCodecVisible => Vis("Codec");
    public bool IsBitrateVisible => Vis("Bitrate");

    // ==================== 派生状态 ====================

    public bool IsDbAvailable => _assets?.IsAvailable ?? false;
    public bool HasItems => Items.Count > 0;
    public bool HasTree => TreeNodes.Count > 0;
    public bool HasSelection => Selected != null;
    public bool ShowDetailBody => Selected != null;
    public int SelectedCount => SelectedRows.Count;
    public bool HasSelectedRows => SelectedRows.Count > 0;

    /// <summary>「展开 / 折叠」单键文案：还有已读取的层级折叠着 ⇒ 显示「展开全部」。</summary>
    public string ExpandAllText => AnyExpandableCollapsed() ? "↕ 展开" : "↕ 收起";

    /// <summary>树空着且没开筛选 ⇒ 「没有可浏览的磁盘」。</summary>
    public bool ShowNoDiskHint => !OnlyLibrary && TreeNodes.Count == 0;

    /// <summary>树空着是因为筛选 ⇒ 单独提示（⛔ 复用「没有磁盘」会误导）。</summary>
    public bool ShowLibraryEmptyHint => OnlyLibrary && TreeNodes.Count == 0;

    /// <summary>右键的目录还不在库里 ⇒ 菜单项可用。</summary>
    public bool CanAddContextNodeToLibrary => ContextNode is { InLibrary: false };

    /// <summary>右键的目录里有未入库文件 ⇒ 「重新扫描此目录」可用（库不可用时不给点）。</summary>
    public bool CanRescanContextNode => IsDbAvailable && ContextNode is { HasPending: true };

    /// <summary>顶级目录（盘符 / 共享根）没有「上一级」。</summary>
    public bool CanGoUp => SelectedNode != null && ParentOf(SelectedNode) != null;

    /// <summary>空态文案：区分「还在读目录」「目录本来就空」与「筛选 / 搜索没命中」。</summary>
    public string EmptyText => IsBusy
        ? "正在读取目录…"
        : _dirCount > 0
            ? "当前筛选 / 搜索没有匹配项。清空搜索或把「类型」换成「全部类型」再看。"
            : SelectedNode is { IsAccessible: false }
                ? "这个目录打不开（权限不足 / 磁盘掉线）。"
                : "这个目录是空的。";

    /// <summary>详情卡数据源（当前选中条目的行）。</summary>
    public AssetRow? DetailRow => Selected?.Row;

    protected override string PageKey => "assets-browse";

    protected override bool PagingEnabled => true;

    public override IReadOnlyList<int> PageSizeChoices => TilePageSizes;

    protected override IReadOnlyList<ListColumn> Columns => AssetColumns.All;

    /// <summary>选中项变化 ⇒ 请 View 把 <c>SelectedItems</c> 回填成 <see cref="SelectedRows"/>。</summary>
    public event EventHandler? SelectionSyncRequested;

    public AssetBrowseViewModel() { }

    public AssetBrowseViewModel(AssetService assets, AssetBrowseService browse, AssetLibraryAddService lib,
        AssetThumbCache thumbs, MediaAssetsSettingsIO io, ThumbnailService thumbGen,
        FileOperationService fileOps, TaskQueue queue, OperationLogService log, IColumnLayoutService cols,
        ScanService scan, IModuleBus? bus = null, AssetNavigationStore? navStore = null)
        : base(cols)
    {
        _bus = bus;
        _navStore = navStore;
        _assets = assets;
        _browse = browse;
        _lib = lib;
        _thumbCache = thumbs;
        _io = io;
        _thumbGen = thumbGen;
        _fileOps = fileOps;
        _queue = queue;
        _opLog = log;
        _scan = scan;

        LoadLayout();
        // 视频专属四列在本页默认隐藏（本页没有「筛选类型 = 视频」这层语义）；用户改过的偏好优先
        var saved = cols.LoadPage(PageKey);
        foreach (var key in new[] { "Duration", "Resolution", "Codec", "Bitrate" })
            if (!saved.ContainsKey(key)) SetColumnVisible(key, false);

        BuildChoices();
        IsTileView = !string.Equals(io.Settings.BrowseViewMode, "details", StringComparison.OrdinalIgnoreCase);
        IconSize = NormalizeIconSize(io.Settings.BrowseIconSize);
        PageSize = 120;                     // 图标视图一屏约 4~6 列 × 2~3 行

        Reload(restoreLastFolder: true);
        _booted = true;
    }

    // ==================== 初始化 ====================

    private static int NormalizeIconSize(int size)
        => IconSizeChoices.Contains(size) ? size : 300;

    private void BuildChoices()
    {
        ViewModes.Clear();
        ViewModes.Add(new ChoiceItem("▦ 图标卡", "tiles"));
        ViewModes.Add(new ChoiceItem("☰ 详细信息", "details"));

        IconSizes.Clear();
        foreach (var size in IconSizeChoices)
            IconSizes.Add(new ChoiceItem(size.ToString(CultureInfo.InvariantCulture) + " px", size.ToString(CultureInfo.InvariantCulture)));

        TypeOptions.Clear();
        foreach (var (label, value) in AssetTypeTexts.Choices) TypeOptions.Add(new FilterOption(label, value));
        FilterType = TypeOptions[0];

        SortChips.Clear();
        SortChips.Add(new ChoiceItem("名称", "Name"));
        SortChips.Add(new ChoiceItem("大小", "Size"));
        SortChips.Add(new ChoiceItem("修改时间", "Modified"));
        SortChips.Add(new ChoiceItem("类型", "Type"));

        SyncChoiceSelection();
    }

    private void SyncChoiceSelection()
    {
        foreach (var c in ViewModes) c.IsSelected = (c.Value == "tiles") == IsTileView;
        foreach (var c in IconSizes)
            c.IsSelected = int.TryParse(c.Value, out var v) && v == IconSize;
        foreach (var c in SortChips)
            c.IsSelected = string.Equals(c.Value, string.IsNullOrEmpty(SortColumn) ? "Name" : SortColumn,
                                         StringComparison.OrdinalIgnoreCase);
    }

    // ==================== 加载（索引叠加层 + 树根） ====================

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await ReloadAsync(restoreLastFolder: false).ConfigureAwait(true);
        StatusText = IsDbAvailable
            ? $"已重读索引：{_entries.Count} 个已入库文件位置"
            : "⚠ 资产库不可用（数据库打不开）——本页仍可浏览磁盘，但无法入库 / 识别已入库项。";
    }

    /// <summary>重读索引（调用方不关心完成时机，结果回来自己刷界面）。</summary>
    private void Reload(bool restoreLastFolder) => _ = ReloadAsync(restoreLastFolder);

    /// <summary>
    /// 读索引（**后台线程**）+ 重建树根。目录内容不在这一步读 —— 由选中节点时按需枚举。
    /// </summary>
    private async Task ReloadAsync(bool restoreLastFolder)
    {
        var keepKey = restoreLastFolder
            ? _io.Settings.LastBrowseFolder
            : AssetBrowseService.KeyOf(SelectedNode);

        var version = ++_loadVersion;
        IsBusy = true;
        StatusText = "正在读取索引…";

        var entries = new List<BrowseEntry>();
        var storages = new List<StorageSource>();
        var byPath = new Dictionary<string, BrowseEntry>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();

        if (IsDbAvailable)
        {
            try
            {
                (entries, storages, byPath, counts, roots) = await Task.Run(() =>
                {
                    var list = _browse.LoadEntries();
                    var srcs = _assets.ListStorages();
                    var map = new Dictionary<string, BrowseEntry>(list.Count, StringComparer.OrdinalIgnoreCase);
                    var dirs = new List<string>(list.Count);
                    foreach (var e in list)
                    {
                        if (string.IsNullOrEmpty(e.Row.FilePath)) continue;
                        map[e.Row.FilePath] = e;
                        dirs.Add(AssetBrowseService.AbsDir(
                            srcs.FirstOrDefault(s => s.Id == e.Row.StorageId)?.RootPath, e.Dir));
                    }
                    return (list, srcs, map, AssetBrowseService.CountByAncestor(dirs),
                            AssetBrowseService.ListRootDirectories(srcs));
                }).ConfigureAwait(true);
            }
            catch { /* 读库失败 ⇒ 按「全部未入库」处理，磁盘仍可浏览 */ }
        }
        else
        {
            roots = AssetBrowseService.ListRootDirectories(storages);
        }

        if (version != _loadVersion) return;   // 期间又发起了一次重读 ⇒ 本次作废

        _entries.Clear();
        _entries.AddRange(entries);
        _storages = storages;
        _byPath = byPath;
        _libCounts = counts;
        _storageRoots = new Dictionary<long, string>(storages.Count);
        foreach (var s in storages) _storageRoots[s.Id] = s.RootPath;   // 索引器赋值：库脏（重复 id）也不抛
        _selectedDirNode = null;   // 旧节点实例已作废（SelectInitial 会重新认领）
        _nodeByKey.Clear();
        _roots.Clear();

        foreach (var r in roots) _roots.Add(MakeNode(r, 0));

        IsBusy = false;
        await SelectInitialAsync(keepKey).ConfigureAwait(true);
        if (OnlyLibrary) await ExpandToLibraryAsync(quiet: true).ConfigureAwait(true);   // 筛选开着 ⇒ 重读后把库目录重新展开到可见
    }

    /// <summary>造一个树节点（计数徽标 / 灰度都按当前索引与存储源算）。</summary>
    private AssetDirNode MakeNode(string absPath, int depth)
    {
        var count = _libCounts.GetValueOrDefault(absPath);
        var node = new AssetDirNode
        {
            AbsPath = absPath,
            Name = NameOf(absPath),
            ToolTipText = absPath,
            Depth = depth,
            InLibrary = IsInLibrary(absPath),
            TotalCount = count,
            HasLibraryContent = count > 0
        };
        _nodeByKey[absPath] = node;
        return node;
    }

    /// <summary>路径是否落在某个已登记存储源之内。</summary>
    private bool IsInLibrary(string absPath)
        => _storages.Any(s => AssetBrowseService.IsUnder(absPath, s.RootPath));

    private static string NameOf(string abs)
    {
        if (abs.EndsWith('\\')) return abs;       // 盘符根 D:\
        var i = abs.LastIndexOf('\\');
        return i < 0 ? abs : abs[(i + 1)..];
    }

    /// <summary>进页面 / 重读后的默认选中：上次的目录 → 第一个有已入库文件的盘 → 第一个盘。</summary>
    private async Task SelectInitialAsync(string? key)
    {
        var saved = AssetBrowseService.ResolveKey(key, _storageRoots);
        if (saved.Length > 0)
        {
            var restored = await EnsureNodeAsync(saved).ConfigureAwait(true);
            if (restored != null) { SelectNode(restored); return; }
        }

        var target = _roots.FirstOrDefault(n => n.TotalCount > 0 && n.InLibrary)
                     ?? _roots.FirstOrDefault(n => n.InLibrary)
                     ?? _roots.FirstOrDefault();

        if (target == null)
        {
            SelectedNode = null;
            Crumbs.Clear();
            ResetToFirstPage();
            StatusText = "没有可浏览的磁盘或共享目录。";
            return;
        }

        SelectNode(target);
    }

    /// <summary>
    /// 把绝对路径的节点链建出来（缺哪层就枚举哪层），返回最末节点。
    /// 用于恢复上次目录 / 「加入资产库」后跳到目标目录。
    /// </summary>
    private async Task<AssetDirNode?> EnsureNodeAsync(string absPath)
    {
        var abs = AssetBrowseService.NormalizeDir(absPath);
        if (abs.Length == 0) return null;
        if (_nodeByKey.TryGetValue(abs, out var hit)) return hit;

        var chain = new List<string>();
        for (string? cur = abs; cur != null; cur = AssetBrowseService.ParentDir(cur)) chain.Insert(0, cur);
        if (chain.Count == 0) return null;

        var node = _roots.FirstOrDefault(r => string.Equals(r.AbsPath, chain[0], StringComparison.OrdinalIgnoreCase));
        if (node == null) return null;   // 不在任何盘符 / 共享根之下（盘已拔掉）

        for (var i = 1; i < chain.Count; i++)
        {
            await EnsureChildrenAsync(node).ConfigureAwait(true);
            var next = node.Children.FirstOrDefault(c => string.Equals(c.AbsPath, chain[i], StringComparison.OrdinalIgnoreCase));
            if (next == null) return null;
            node = next;
        }
        return node;
    }

    // ==================== 树 ====================

    /// <summary>
    /// 枚举一层子目录并挂到节点上（已枚举过 ⇒ 直接返回）。顺手数一遍直属文件里还没入库的
    /// —— 同一次目录枚举顺带取，不进额外 IO（⛔ 不递归）。
    /// </summary>
    private async Task EnsureChildrenAsync(AssetDirNode node)
    {
        if (node.IsLoaded) return;

        var abs = node.AbsPath;
        BrowseDirContent? content;
        try { content = await Task.Run(() => AssetBrowseService.LoadDirectory(abs)).ConfigureAwait(true); }
        catch { content = null; }

        node.IsLoaded = true;
        node.IsAccessible = content is { Accessible: true };
        if (content is { Accessible: true })
        {
            foreach (var d in content.Dirs) node.Children.Add(MakeNode(d.Path, node.Depth + 1));

            // 只在**已经和库有关**的目录上提示待加入：完全没入库的目录一律不飘红，
            // 否则整棵树每个目录都挂着 ⟳N（它们本来就是要用「➕ 加入资产库」的）。
            if (node.InLibrary || node.HasLibraryContent)
                node.PendingCount = CountPending(content.Files);
        }

        node.RefreshDerived();
    }

    /// <summary>
    /// 直属文件里**还没入库**的个数（与扫描器同一套忽略规则；库不可用 ⇒ 0，避免全盘误报）。
    /// </summary>
    private int CountPending(List<FsEntry> files)
    {
        if (!IsDbAvailable || files.Count == 0) return 0;

        var s = _io.Settings;
        var ignoreExt = MediaAssetsSettings.ParseList(s.IgnoreExtensions);

        var n = 0;
        foreach (var f in files)
        {
            if (_byPath.ContainsKey(f.Path)) continue;
            if (ignoreExt.Contains(Path.GetExtension(f.Path))) continue;
            if (s.IgnoreTempFiles && f.Name.StartsWith("~$", StringComparison.Ordinal)) continue;
            n++;
        }
        return n;
    }

    /// <summary>按展开状态扁平化（只保留「祖先链全展开」的节点；筛选开着时再滤掉未入库分支）。</summary>
    private void RebuildTree()
    {
        var flat = new ObservableCollection<AssetDirNode>();

        void Add(AssetDirNode n, int depth)
        {
            n.Depth = depth;
            flat.Add(n);
            if (!n.IsExpanded) return;
            foreach (var c in n.Children)
            {
                if (OnlyLibrary && !IsLibraryBranch(c)) continue;
                Add(c, depth + 1);
            }
        }

        foreach (var root in _roots)
        {
            if (OnlyLibrary && !IsLibraryBranch(root)) continue;
            Add(root, 0);
        }

        TreeNodes = flat;       // 一次性换源：⛔ 别 Clear + 逐项 Add（N 轮列表更新）
        OnPropertyChanged(nameof(HasTree));
        OnPropertyChanged(nameof(ExpandAllText));
        OnPropertyChanged(nameof(ShowNoDiskHint));
        OnPropertyChanged(nameof(ShowLibraryEmptyHint));
    }

    /// <summary>
    /// 筛选下的保留判据：节点自身已入库，**或某个存储源的根在它下面** ——
    /// 后者是祖先链（库目录通常是 <c>D:\Videos</c>，只留已入库节点的话树里连 <c>D:\</c> 都不会出现）。
    /// </summary>
    private bool IsLibraryBranch(AssetDirNode node)
        => node.InLibrary || _storages.Any(s => AssetBrowseService.IsUnder(s.RootPath, node.AbsPath));

    private bool AnyExpandableCollapsed()
        => _nodeByKey.Values.Any(n => n.IsLoaded && n.Children.Count > 0 && !n.IsExpanded);

    /// <summary>
    /// 点树节点：切到该目录的内容；有子目录且还折叠着就同时展开（懒加载那一层）。
    /// <para>🔴 再点一次**已选中**的目录 = 收起 / 展开子级（不重载内容）。</para>
    /// </summary>
    [RelayCommand]
    private void SelectNode(AssetDirNode? node)
    {
        if (node == null) return;

        ClearResultSetState();   // 点目录 = 主动回到目录浏览（否则结果集模式会一直压着树）

        if (ReferenceEquals(_selectedDirNode, node) && node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
            if (node.IsExpanded) LoadChildren(node);
            RebuildTree();
            return;
        }

        SetSelectedDirNode(node);
        SelectedNode = node;

        var expandedChanged = false;
        if (!node.IsExpanded && node.HasChildren)
        {
            node.IsExpanded = true;
            expandedChanged = true;
            LoadChildren(node);              // 首次展开 ⇒ 后台枚举子目录
        }
        if (ExpandAncestors(node)) expandedChanged = true;
        if (expandedChanged) RebuildTree();

        BuildCrumbs();

        _io.Settings.LastBrowseFolder = AssetBrowseService.KeyOf(node);
        _io.Save();

        ResetToFirstPage();     // → RefreshView → ApplyView（重新枚举当前目录的一屏）
        StatusText = StatusSummary();
    }

    /// <summary>后台枚举 + 完成后重建树（⛔ 不 await：点树不该卡顿）。</summary>
    private void LoadChildren(AssetDirNode node)
    {
        if (node.IsLoaded) return;
        _ = LoadChildrenCoreAsync(node);
    }

    private async Task LoadChildrenCoreAsync(AssetDirNode node)
    {
        try
        {
            await EnsureChildrenAsync(node).ConfigureAwait(true);   // 回到 UI 线程再重建树
            RebuildTree();
        }
        catch { /* 枚举失败 ⇒ 节点保持「未展开」，⛔ 不把异常抛到 UI */ }
    }

    /// <summary>切节点高亮：只动「旧 + 新」两个节点。</summary>
    private void SetSelectedDirNode(AssetDirNode node)
    {
        if (ReferenceEquals(_selectedDirNode, node)) return;
        if (_selectedDirNode != null) _selectedDirNode.IsSelected = false;
        _selectedDirNode = node;
        node.IsSelected = true;
    }

    private AssetDirNode? _selectedDirNode;

    /// <summary>把目标节点的祖先链展开；返回**是否有节点的展开状态被改动**。</summary>
    private bool ExpandAncestors(AssetDirNode node)
    {
        var changed = false;
        var cur = ParentOf(node);
        while (cur != null)
        {
            if (!cur.IsExpanded) { cur.IsExpanded = true; changed = true; }
            cur = ParentOf(cur);
        }
        return changed;
    }

    private AssetDirNode? ParentOf(AssetDirNode node)
    {
        var parent = AssetBrowseService.ParentDir(node.AbsPath);
        return parent == null ? null : _nodeByKey.GetValueOrDefault(parent);
    }

    [RelayCommand]
    private void GoUp()
    {
        if (!CanGoUp || SelectedNode == null) return;

        var parent = ParentOf(SelectedNode);
        if (parent == null) return;
        SelectNode(parent);
    }

    [RelayCommand]
    private void GoCrumb(BrowseCrumb? crumb)
    {
        if (crumb?.Node == null) return;
        SelectNode(crumb.Node);
    }

    [RelayCommand]
    private void ToggleTree() => IsTreeVisible = !IsTreeVisible;

    /// <summary>
    /// 展开 / 折叠一把开关（⛔ 两个按钮）：还有已读取的层级折叠着就展开，全展开了再点就折叠。
    /// </summary>
    [RelayCommand]
    private void ToggleExpandAll()
    {
        if (AnyExpandableCollapsed()) ExpandAll();
        else CollapseAll();
    }

    /// <summary>
    /// 展开全部 = **只展开已经枚举过的层级**（⛔ 不递归扫盘：那等于进页面就把所有盘走一遍）。
    /// </summary>
    private void ExpandAll()
    {
        var n = 0;
        foreach (var node in _nodeByKey.Values.Where(x => x.IsLoaded && x.Children.Count > 0))
        {
            if (!node.IsExpanded) { node.IsExpanded = true; n++; node.RefreshDerived(); }
        }
        RebuildTree();
        StatusText = n > 0
            ? $"已展开 {n} 个已读取的目录（更深的层级展开时再读）。"
            : "没有可展开的层级（子目录在首次展开时才读取）。";
    }

    private void CollapseAll()
    {
        foreach (var n in _nodeByKey.Values) n.IsExpanded = false;
        RebuildTree();
        StatusText = "已折叠全部目录。";
    }

    /// <summary>只显示已加入资产库：开着时把每个存储源的层级链展开到可见（否则库目录全埋在折叠的子层里）。</summary>
    [RelayCommand]
    private void ToggleOnlyLibrary() => OnlyLibrary = !OnlyLibrary;

    partial void OnOnlyLibraryChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoDiskHint));
        OnPropertyChanged(nameof(ShowLibraryEmptyHint));
        if (!_booted) return;

        if (value) _ = ExpandToLibraryAsync();
        else RebuildTree();
    }

    partial void OnContextNodeChanged(AssetDirNode? value)
    {
        OnPropertyChanged(nameof(CanAddContextNodeToLibrary));
        OnPropertyChanged(nameof(CanRescanContextNode));
        RefreshContextStorage();
    }

    private async Task ExpandToLibraryAsync(bool quiet = false)
    {
        var roots = _storages.Select(s => s.RootPath)
                             .Where(p => !string.IsNullOrWhiteSpace(p))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList();

        foreach (var root in roots)
        {
            try
            {
                var node = await EnsureNodeAsync(root).ConfigureAwait(true);
                if (node == null) continue;
                node.IsExpanded = true;
                ExpandAncestors(node);
                LoadChildren(node);        // 库目录本身也铺开一层子目录（后台枚举，⛔ 不 await）
            }
            catch { /* 单个存储源读不到 ⇒ 跳过它，其余照展开（⛔ 不把异常抛到 UI） */ }
        }

        RebuildTree();
        if (quiet) return;
        StatusText = TreeNodes.Count > 0
            ? $"只显示已加入资产库的目录（{roots.Count} 个存储源）。"
            : "还没有加入资产库的目录 —— 关掉筛选，右键一个磁盘目录选「➕ 加入资产库」。";
    }

    private void BuildCrumbs()
    {
        Crumbs.Clear();
        if (SelectedNode == null) return;

        var chain = new List<AssetDirNode>();
        for (AssetDirNode? cur = SelectedNode; cur != null; cur = ParentOf(cur)) chain.Add(cur);
        chain.Reverse();

        // 面包屑 = 盘符 / 共享根 → 逐层目录（每段可跳）
        foreach (var n in chain)
            Crumbs.Add(new BrowseCrumb(n.Name, n));
    }

    // ==================== 视图（目录内容） ====================

    protected override void RefreshView() => ApplyView();

    private void ApplyView()
    {
        var version = ++_viewVersion;          // 作废上一批还没填完的卡
        var thumbCt = BeginThumbBatch();        // 上一批的缩略图解码一并取消
        Items.Clear();
        _thumbRequested.Clear();                // 新一页 ⇒ 懒解码配额从头算
        ResetThumbQueue();                      // 上一页还没生成的别占着生成泵
        _dirCount = 0;

        var node = SelectedNode;
        if (_resultSet != null)
        {
            IsBusy = true;
            _ = LoadResultSetAsync(version, thumbCt);   // 结果集模式：来源是索引，⛔ 不枚举目录
            return;
        }

        if (node == null)
        {
            IsBusy = false;
            TotalCount = 0;
            NotifyItemsChanged();
            return;
        }

        IsBusy = true;
        _ = LoadDirectoryAsync(node, version, thumbCt);
    }

    /// <summary>
    /// 枚举当前目录（后台）→ 叠加索引 → 筛选排序 → 填当前页。
    /// <para>🔴 目录内容**实时枚举**：磁盘上新增 / 删除的文件立刻可见，⛔ 不等扫描。</para>
    /// </summary>
    private async Task LoadDirectoryAsync(AssetDirNode node, int version, CancellationToken thumbCt)
    {
        var abs = node.AbsPath;
        BrowseDirContent content;
        try { content = await Task.Run(() => AssetBrowseService.LoadDirectory(abs)).ConfigureAwait(true); }
        catch { content = new BrowseDirContent { Path = abs, Accessible = false }; }

        if (version != _viewVersion) return;

        node.IsAccessible = content.Accessible;
        node.RefreshDerived();

        var all = BuildTiles(content);
        _dirCount = all.Count;

        var list = FilterAndSort(all, FilterType?.Value ?? "", SearchText, SortColumn, SortDescending);

        IsBusy = false;
        TotalCount = list.Count;
        var before = PageIndex;
        ClampPageIndex();
        if (PageIndex != before) return;   // 页码收敛后已按新页重取

        var offset = (long)(PageIndex - 1) * PageSize;
        var page = list.Skip((int)Math.Min(offset, int.MaxValue)).Take(PageSize).ToList();

        FillChunk(page, 0, version, thumbCt);

        SyncChoiceSelection();
        UpdateHeaders();
        NotifyItemsChanged();
    }

    /// <summary>实时目录内容 → 卡片列表（索引命中 = 已入库，未命中 = 合成行 + 标灰）。</summary>
    private List<AssetTile> BuildTiles(BrowseDirContent content)
    {
        var width = IconSize;
        var tiles = new List<AssetTile>(content.Dirs.Count + content.Files.Count);

        foreach (var d in content.Dirs)
            tiles.Add(new AssetTile(FolderRow(d), 0, "", d.Path, IsInLibrary(d.Path), true, width));

        foreach (var f in content.Files)
        {
            if (_byPath.TryGetValue(f.Path, out var entry))
                tiles.Add(new AssetTile(entry.Row, entry.LocationId, entry.Dir, f.Path, true, false, width));
            else
                tiles.Add(new AssetTile(FileRow(f), 0, "", f.Path, false, false, width));
        }

        return tiles;
    }

    /// <summary>未入库文件 → 合成行（Id=0；文件名 / 大小 / 修改时间照填，界面无需判空）。</summary>
    private static AssetRow FileRow(FsEntry f) => new()
    {
        Id = 0,
        Name = f.Name,
        Title = Path.GetFileNameWithoutExtension(f.Name),
        FileName = f.Name,
        FilePath = f.Path,
        Type = f.Type,
        Size = f.Size,
        Mtime = f.Mtime
    };

    private static AssetRow FolderRow(FsEntry f) => new()
    {
        Id = 0,
        Name = f.Name,
        Title = f.Name,
        FileName = f.Name,
        FilePath = f.Path,
        Type = AssetTypes.Other,
        Mtime = f.Mtime
    };

    /// <summary>搜索 / 类型过滤 + 排序（纯 CPU，可在后台线程跑）。目录恒排在最前。</summary>
    private static List<AssetTile> FilterAndSort(
        List<AssetTile> all, string type, string search, string? sortColumn, bool sortDescending)
    {
        IEnumerable<AssetTile> q = all;

        if (type.Length > 0)
            q = q.Where(t => t.IsFolder || string.Equals(t.Row.Type, type, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            q = q.Where(t => words.All(w =>
                t.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase) ||
                t.AbsPath.Contains(w, StringComparison.CurrentCultureIgnoreCase)));
        }

        var key = string.IsNullOrEmpty(sortColumn) ? "Name" : sortColumn;
        var ordered = q.OrderBy(t => t.IsFolder ? 0 : 1);   // 文件夹恒在前（与资源管理器一致）
        ordered = key switch
        {
            "Type" => sortDescending
                ? ordered.ThenByDescending(t => t.TypeText, StringComparer.CurrentCulture)
                : ordered.ThenBy(t => t.TypeText, StringComparer.CurrentCulture),
            "Size" => sortDescending
                ? ordered.ThenByDescending(t => t.Row.Size)
                : ordered.ThenBy(t => t.Row.Size),
            "Modified" => sortDescending
                ? ordered.ThenByDescending(t => t.Row.Mtime ?? "")
                : ordered.ThenBy(t => t.Row.Mtime ?? ""),
            // 视频专属四列（本页默认隐藏，用户手动打开后可排序）—— 缺这些分支会静默退回「按名称」
            "Duration" => sortDescending
                ? ordered.ThenByDescending(t => t.Row.DurationSeconds)
                : ordered.ThenBy(t => t.Row.DurationSeconds),
            "Resolution" => sortDescending
                ? ordered.ThenByDescending(t => t.Row.ResolutionText, StringComparer.CurrentCulture)
                : ordered.ThenBy(t => t.Row.ResolutionText, StringComparer.CurrentCulture),
            "Codec" => sortDescending
                ? ordered.ThenByDescending(t => t.Row.CodecText, StringComparer.CurrentCulture)
                : ordered.ThenBy(t => t.Row.CodecText, StringComparer.CurrentCulture),
            "Bitrate" => sortDescending
                ? ordered.ThenByDescending(t => t.Row.BitRateBps)
                : ordered.ThenBy(t => t.Row.BitRateBps),
            _ => sortDescending
                ? ordered.ThenByDescending(t => t.Name, StringComparer.CurrentCulture)
                : ordered.ThenBy(t => t.Name, StringComparer.CurrentCulture)
        };

        return ordered.ThenBy(t => t.AbsPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 分批填卡：一次最多 <see cref="TileChunkSize"/> 张，剩下的按后台优先级续上。
    /// <para>🔴 一页 500 张会让切目录僵住好几秒；分批后第一屏立刻出现，用户已经能点、能滚。</para>
    /// </summary>
    private void FillChunk(List<AssetTile> tiles, int from, int version, CancellationToken thumbCt)
    {
        if (version != _viewVersion) return;

        var to = Math.Min(from + TileChunkSize, tiles.Count);
        for (var i = from; i < to; i++)
        {
            var tile = tiles[i];
            Items.Add(tile);
            // 🔴 只解首屏配额：屏幕只能显示十来张，其余等滚到可视区（ReportViewport）
            if (tile.WantsThumb && Items.Count <= InitialThumbQuota && _thumbRequested.Add(tile.ThumbKey))
                _ = AssignThumbAsync(tile, IconSize, thumbCt);   // 并发闸在缓存里
        }

        if (to >= tiles.Count)
        {
            ReconcileSelection();   // 全部到位后再裁选中（⛔ 别在第一批就裁）
            return;
        }

        if (from == 0 && Selected == null && Items.Count > 0)
            Selected = Items[0];    // 详情卡先有内容可显示

        Dispatcher.UIThread.Post(
            () => FillChunk(tiles, to, version, thumbCt),
            DispatcherPriority.Background);
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        TypeHeader = "类型" + SortArrow("Type");
        TagsHeader = "标签" + SortArrow("Tags");
        CategoriesHeader = "分类" + SortArrow("Categories");
        CollectionsHeader = "集合" + SortArrow("Collections");
        SizeHeader = "大小" + SortArrow("Size");
        ModifiedHeader = "修改时间" + SortArrow("Modified");
        StorageHeader = "存储源" + SortArrow("Storage");
        DurationHeader = "时长" + SortArrow("Duration");
        ResolutionHeader = "分辨率" + SortArrow("Resolution");
        CodecHeader = "编码" + SortArrow("Codec");
        BitrateHeader = "码率" + SortArrow("Bitrate");
    }

    private void NotifyItemsChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private string StatusSummary()
    {
        var dir = SelectedNode == null ? "（未选目录）" : SelectedNode.AbsPath;
        if (SelectedNode == null) return dir;

        var lib = Items.Count(t => t.InLibrary);
        var text = $"{dir}：{TotalCount} 项";
        if (_dirCount != TotalCount) text += $"（目录共 {_dirCount} 项，已筛选）";
        if (Items.Count > 0) text += $" · 本页已入库 {lib} / {Items.Count}";
        if (!SelectedNode.InLibrary) text += " · ⚠ 该目录还没加入资产库（右键可加入）";
        return text;
    }

    // ==================== 工具条动作（视图 / 尺寸 / 排序 / 搜索） ====================

    [RelayCommand]
    private void SetViewMode(ChoiceItem? item)
    {
        if (item == null) return;
        IsTileView = item.Value == "tiles";
        _io.Settings.BrowseViewMode = IsTileView ? "tiles" : "details";
        _io.Save();
        SyncChoiceSelection();
    }

    [RelayCommand]
    private void SetIconSize(ChoiceItem? item)
    {
        if (item == null || !int.TryParse(item.Value, out var size)) return;
        IconSize = NormalizeIconSize(size);
        _io.Settings.BrowseIconSize = IconSize;
        _io.Save();
        SyncChoiceSelection();
        ApplyView();   // 卡片宽度变了 ⇒ 整页重建（选中按绝对路径认领，不丢）
    }

    [RelayCommand]
    private void SetSort(ChoiceItem? item)
    {
        if (item == null) return;
        SortCommand.Execute(item.Value);
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_booted) ResetToFirstPage();
    }

    partial void OnFilterTypeChanged(FilterOption? value)
    {
        if (_booted) ResetToFirstPage();
    }

    partial void OnIsTileViewChanged(bool value)
    {
        OnPropertyChanged(nameof(TileSource));
        OnPropertyChanged(nameof(DetailSource));
        SyncChoiceSelection();
    }

    partial void OnIconSizeChanged(int value) => SyncChoiceSelection();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HasItems));
    }

    partial void OnSelectedNodeChanged(AssetDirNode? value) => OnPropertyChanged(nameof(CanGoUp));

    partial void OnSelectedChanged(AssetTile? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailRow));
        OnPropertyChanged(nameof(DetailFolder));
        OnPropertyChanged(nameof(DetailPath));
        OnPropertyChanged(nameof(DetailIsInLibrary));
        OnPropertyChanged(nameof(DetailIsFolder));
        RaiseDetailChanged();
    }

    // ==================== 选中（Ctrl 增选 / Shift 连选 / Ctrl+A 全选） ====================

    /// <summary>View 的选中变化回填（点卡片 / Ctrl 增选 / Shift 连选 / Ctrl+A 全走这里）。</summary>
    public void SetSelectionFromView(IReadOnlyList<AssetTile> tiles, AssetTile? clicked)
    {
        _selectedTiles.Clear();
        _selectedTiles.AddRange(tiles);

        SelectedRows.Clear();
        foreach (var t in tiles)
            if (t.InLibrary) SelectedRows.Add(t.Row);   // 库操作只认已入库项

        NotifySelectionMeta();
        Selected = clicked
                   ?? (tiles.Count > 0 ? tiles[^1] : null);
        RaiseDetailChanged();
    }

    private void NotifySelectionMeta()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelectedRows));
        OnPropertyChanged(nameof(HasSelectedTiles));
        OnPropertyChanged(nameof(CanBatchRename));
    }

    /// <summary>列表重建 / 尺寸切档后按**绝对路径**认领选中项（Items 里都是新实例）。</summary>
    private void ReconcileSelection()
    {
        var keys = _selectedTiles.Select(t => t.AbsPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var picked = Items.Where(t => keys.Contains(t.AbsPath)).ToList();

        _selectedTiles.Clear();
        _selectedTiles.AddRange(picked);

        SelectedRows.Clear();
        foreach (var t in picked)
            if (t.InLibrary) SelectedRows.Add(t.Row);

        var pick = Selected == null
            ? null
            : picked.FirstOrDefault(t => string.Equals(t.AbsPath, Selected.AbsPath, StringComparison.OrdinalIgnoreCase))
              ?? picked.FirstOrDefault();

        Selected = pick ?? (Items.Count > 0 ? Items[0] : null);
        if (Selected is { InLibrary: true } && SelectedRows.Count == 0) SelectedRows.Add(Selected.Row);

        NotifySelectionMeta();
        SelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        RaiseDetailChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        _selectedTiles.Clear();
        _selectedTiles.AddRange(Items);

        SelectedRows.Clear();
        foreach (var t in Items)
            if (t.InLibrary) SelectedRows.Add(t.Row);
        Selected = Items.FirstOrDefault();
        NotifySelectionMeta();
        SelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        StatusText = $"已全选当前页 {Items.Count} 项（其中 {SelectedRows.Count} 项已入库）。";
    }

    [RelayCommand]
    private void SelectNone()
    {
        _selectedTiles.Clear();
        SelectedRows.Clear();
        NotifySelectionMeta();
        SelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        StatusText = "已取消选择。";
    }

    [RelayCommand]
    private void InvertSelection()
    {
        var picked = _selectedTiles.Select(t => t.AbsPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedTiles.Clear();
        _selectedTiles.AddRange(Items.Where(t => !picked.Contains(t.AbsPath)));

        SelectedRows.Clear();
        foreach (var t in _selectedTiles)
            if (t.InLibrary) SelectedRows.Add(t.Row);
        NotifySelectionMeta();
        SelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        StatusText = $"已反选当前页，选中 {_selectedTiles.Count} 项。";
    }

    /// <summary>批量操作的候选：当前选中里**已入库**的条目。</summary>
    private List<AssetTile> BatchTiles()
        => _selectedTiles.Where(t => t.InLibrary).ToList();

    // ==================== 缩略图 ====================

    /// <summary>首屏就解这么多张缩略图（约 3 屏），其余等滚动到可视区（见 <see cref="ReportViewport"/>）。</summary>
    private const int InitialThumbQuota = 36;

    /// <summary>补缩略图前扫盘的去抖（连续滚动只扫一次盘）。</summary>
    private const int ThumbScanDebounceMs = 180;

    /// <summary>生成泵一批的张数（一批落盘后立刻回填界面，滚动才不会看起来没反应）。</summary>
    private const int ThumbGenBatchSize = 4;

    /// <summary>待生成队列的上限（滚动越快越容易堆，超出就丢掉最早的 —— 那批已经滚过去了）。</summary>
    private const int ThumbGenQueueCap = 60;

    /// <summary>生成泵的并行度（每个视频一次 ffmpeg 子进程，太多只会互相抢 CPU）。</summary>
    private static readonly int ThumbGenParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    /// <summary>已经排过解码的卡片（按绝对路径；切目录 / 翻页时清空）。</summary>
    private readonly HashSet<string> _thumbRequested = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已经排进生成队列的卡片（同一路径只排一次）。**只在 UI 线程读写**。</summary>
    private readonly HashSet<string> _thumbGenSeen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>待生成的卡片（滚动期间持续追加，泵串行出队）。跨线程 ⇒ 全程加锁。</summary>
    private readonly Queue<AssetTile> _thumbGenQueue = new();
    private readonly object _thumbGenSync = new();

    /// <summary>生成泵是否在跑（一个目录只跑一个泵，⛔ 不为每张卡开任务）。</summary>
    private bool _thumbGenPumping;

    /// <summary>缺口扫描是否在跑（0/1，Interlocked 读写）。</summary>
    private int _thumbScanRunning;

    /// <summary>扫描期间又滚到的新窗口（只留最新的那个）。</summary>
    private volatile List<AssetTile>? _thumbScanNext;

    /// <summary>切目录 / 翻页：丢掉还没生成的那批（上一个目录的图没人看了，⛔ 别占着生成泵）。</summary>
    private void ResetThumbQueue()
    {
        _thumbScanNext = null;
        lock (_thumbGenSync)
        {
            while (_thumbGenQueue.Count > 0) _thumbGenSeen.Remove(_thumbGenQueue.Dequeue().ThumbKey);
        }
        _thumbGenSeen.Clear();
    }

    /// <summary>起一批新的缩略图解码（取消上一批）。只取消不 Dispose。</summary>
    private CancellationToken BeginThumbBatch()
    {
        _thumbCts?.Cancel();
        _thumbCts = new CancellationTokenSource();
        return _thumbCts.Token;
    }

    /// <summary>
    /// 可视区变化：给真正看得见的那一段补缩略图（上下各预取一屏）。
    /// <para>🔴 几百项的目录别一次全解：屏幕只显示十几张，其余解码纯属白读磁盘。</para>
    /// <para>🔴 只解码**已缓存**的图；缓存缺失的那部分交给 <see cref="RequestThumbGeneration"/> 现做 ——
    /// 于是滚动时缩略图是「一路滚一路长出来」，⛔ 不是进目录时等一次性批处理。</para>
    /// </summary>
    public void ReportViewport(double offsetY, double viewportHeight, double extentHeight)
    {
        if (!IsTileView || Items.Count == 0 || extentHeight <= 0 || viewportHeight <= 0) return;

        var perItem = Math.Max(1, extentHeight / Items.Count);
        var firstVisible = (int)Math.Floor(Math.Max(0, offsetY) / perItem);
        var pageItems = Math.Max(1, (int)Math.Ceiling(viewportHeight / perItem));

        var first = Math.Max(0, firstVisible - pageItems);
        var last = Math.Min(Items.Count - 1, firstVisible + pageItems * 2);
        if (last < first) return;

        RequestThumbsInRange(first, last);
        RequestThumbGeneration(first, last);
    }

    private void RequestThumbsInRange(int first, int last)
    {
        var ct = _thumbCts?.Token ?? CancellationToken.None;
        if (ct.IsCancellationRequested) return;

        first = Math.Max(0, first);
        last = Math.Min(Items.Count - 1, last);
        for (var i = first; i <= last; i++)
        {
            var tile = Items[i];
            if (!tile.WantsThumb || !_thumbRequested.Add(tile.ThumbKey)) continue;
            _ = AssignThumbAsync(tile, IconSize, ct);
        }
    }

    /// <summary>
    /// 可视区里**还没有缓存文件**的卡片排进生成队列。
    /// <para>🔴 查盘放后台（去抖 <see cref="ThumbScanDebounceMs"/>ms）—— 滚动帧里⛔ 不做 <c>File.Exists</c>。
    /// 扫描期间又滚到别处 ⇒ 记下最新窗口，这轮扫完接着扫（滚动途中持续补，不是等停下来才补）。</para>
    /// </summary>
    private void RequestThumbGeneration(int first, int last)
    {
        if (!_io.Settings.AutoGenerateThumbnails) return;   // 设置里关了 → 只显示类型图标，⛔ 不读盘

        var window = new List<AssetTile>(last - first + 1);
        for (var i = first; i <= last; i++)
        {
            var tile = Items[i];
            if (!tile.WantsThumb || _thumbGenSeen.Contains(tile.ThumbKey)) continue;
            window.Add(tile);
        }
        if (window.Count == 0) return;

        if (Interlocked.Exchange(ref _thumbScanRunning, 1) == 1)
        {
            _thumbScanNext = window;   // 已在扫 ⇒ 只留最新窗口
            return;
        }
        _ = ScanThumbWindowAsync(window);
    }

    /// <summary>后台扫一个窗口的缓存缺口 → 回 UI 线程入队；期间有新窗口就接着扫。</summary>
    private async Task ScanThumbWindowAsync(List<AssetTile> window)
    {
        try
        {
            while (true)
            {
                await Task.Delay(ThumbScanDebounceMs).ConfigureAwait(false);

                var missing = window.Where(t => !File.Exists(_thumbGen.ThumbPathFor(t.AbsPath))).ToList();
                if (missing.Count > 0) Dispatcher.UIThread.Post(() => QueueThumbGeneration(missing));

                var next = _thumbScanNext;
                if (next == null) break;
                _thumbScanNext = null;
                window = next;
            }
        }
        catch { /* 盘符掉线 / 权限不足 ⇒ 当作没有缺口 */ }
        finally
        {
            Volatile.Write(ref _thumbScanRunning, 0);

            // 收尾竞态：置 0 之前刚好又滚到一个窗口 ⇒ 别把它漏掉
            if (_thumbScanNext is { } leftover)
            {
                _thumbScanNext = null;
                if (Interlocked.Exchange(ref _thumbScanRunning, 1) == 0) _ = ScanThumbWindowAsync(leftover);
                else _thumbScanNext = leftover;
            }
        }
    }

    /// <summary>
    /// UI 线程：把缺图的卡排进生成队列；泵没在跑就启动一个。
    /// <para>滚回去再滚回来只会命中 <see cref="_thumbGenSeen"/>，⛔ 不重复生成；也不会每张卡开一个任务。</para>
    /// </summary>
    private void QueueThumbGeneration(List<AssetTile> tiles)
    {
        var added = false;
        foreach (var tile in tiles)
        {
            if (!_thumbGenSeen.Add(tile.ThumbKey)) continue;
            lock (_thumbGenSync) _thumbGenQueue.Enqueue(tile);
            added = true;
        }
        if (!added) return;

        // 上限：快速滚过大目录时别把整页都排上 —— 滚过去的那批已经没人看了
        while (true)
        {
            AssetTile? dropped = null;
            lock (_thumbGenSync)
            {
                if (_thumbGenQueue.Count <= ThumbGenQueueCap) break;
                dropped = _thumbGenQueue.Dequeue();
            }
            if (dropped == null) break;
            _thumbGenSeen.Remove(dropped.ThumbKey);
        }

        lock (_thumbGenSync)
        {
            if (_thumbGenPumping) return;
            _thumbGenPumping = true;
        }

        var dirName = SelectedNode?.Name ?? "";
        _queue.Enqueue(dirName.Length > 0 ? $"生成缩略图（{dirName}）" : "生成缩略图", PumpThumbGenerationAsync);
    }

    /// <summary>
    /// 生成泵：串行出队、每批 <see cref="ThumbGenBatchSize"/> 张并行生成，**一批落盘就回填界面**，
    /// 队列空了才退出 —— 滚动期间队列被持续追加，缩略图于是跟着滚动一路长出来。
    /// </summary>
    private async Task PumpThumbGenerationAsync(CancellationToken ct, IProgress<string> p)
    {
        var ok = 0;
        var fail = 0;
        var done = 0;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var batch = new List<AssetTile>(ThumbGenBatchSize);
                lock (_thumbGenSync)
                {
                    while (_thumbGenQueue.Count > 0 && batch.Count < ThumbGenBatchSize)
                        batch.Add(_thumbGenQueue.Dequeue());
                }
                if (batch.Count == 0) break;

                await Parallel.ForEachAsync(batch,
                    new ParallelOptions { MaxDegreeOfParallelism = ThumbGenParallelism, CancellationToken = ct },
                    async (tile, token) =>
                    {
                        var loc = new FileLocation
                        {
                            Id = tile.LocationId,
                            AssetId = tile.Row.Id,
                            StorageId = tile.Row.StorageId,
                            Path = tile.AbsPath,
                            FileName = tile.Row.FileName,
                            Ext = Path.GetExtension(tile.AbsPath),
                            Size = tile.Row.Size,
                            Mtime = tile.Row.Mtime
                        };
                        var (good, msg, _) = await _thumbGen.GenerateAsync(loc, token).ConfigureAwait(false);
                        if (good) Interlocked.Increment(ref ok);
                        else
                        {
                            Interlocked.Increment(ref fail);
                            p.Report($"跳过 {tile.Row.FileName}：{msg}");
                        }
                        p.Report($"[{Interlocked.Increment(ref done)}] {tile.Row.FileName}");
                    }).ConfigureAwait(false);

                Dispatcher.UIThread.Post(() => DecodeTiles(batch));
            }

            p.Report($"缩略图完成：成功 {ok}，失败 {fail}。");
        }
        finally
        {
            var leftovers = new List<AssetTile>();
            lock (_thumbGenSync)
            {
                _thumbGenPumping = false;
                while (_thumbGenQueue.Count > 0) leftovers.Add(_thumbGenQueue.Dequeue());
            }

            // 取消 / 异常退出（或恰好同时又有卡入队）：交回「未排」状态，下次滚到再生成
            if (leftovers.Count > 0)
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var tile in leftovers) _thumbGenSeen.Remove(tile.ThumbKey);
                    if (!ct.IsCancellationRequested) QueueThumbGeneration(leftovers);
                });
        }
    }

    /// <summary>把一批刚生成好的缩略图解码回填（缓存里本来没有它们的条目）。</summary>
    private void DecodeTiles(List<AssetTile> tiles)
    {
        var ct = _thumbCts?.Token ?? CancellationToken.None;
        if (ct.IsCancellationRequested) return;
        foreach (var tile in tiles) _ = AssignThumbAsync(tile, IconSize, ct);
    }

    /// <summary>单张缩略图：后台解码 → UI 线程赋值。失败 ⇒ 保持占位（⛔ 不抛、⛔ 不弹窗）。</summary>
    private async Task AssignThumbAsync(AssetTile tile, int width, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested) return;
            var path = _thumbGen.ThumbPathFor(tile.AbsPath);
            var bmp = await _thumbCache.GetAsync(path, width, ct).ConfigureAwait(false);
            if (bmp == null || ct.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!ct.IsCancellationRequested) tile.Thumb = bmp;
            });
        }
        catch (OperationCanceledException) { /* 切目录 / 换尺寸 ⇒ 正常取消 */ }
        catch { /* 解码链上的任何异常都不许冒到 UI 线程 */ }
    }
}
