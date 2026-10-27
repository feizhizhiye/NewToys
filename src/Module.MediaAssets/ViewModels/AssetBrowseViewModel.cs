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
/// 「资产浏览」页（需求 §6.6.1）—— **资源管理器形态**：左目录树 → 右「图标卡 / 详细信息」双视图 → 双击用默认程序打开。
/// <para>
/// 与「全部资产」分工：那边按**元数据**找（搜索 / 标签 / 筛选），这里按**磁盘位置**翻。
/// 目录数据一律来自库里的索引（<c>file_locations.relative_path</c>），⛔ 不实时枚举磁盘（NAS / 移动盘离线会卡死）。
/// </para>
/// <para>
/// 结构上沿用列表页基类（列显隐 / 列宽 / 排序 / 分页 / 底栏 / 状态栏 / 工具条全部现成），
/// 但**不是**列表页：左侧树自绘，右侧两套视图共用 <see cref="Items"/> —— 一行 = 一个**文件位置**，
/// 同一资产在 A / B 两个目录各出现一次是正确语义。
/// </para>
/// </summary>
public partial class AssetBrowseViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly AssetService _assets = null!;
    private readonly AssetBrowseService _browse = null!;
    private readonly AssetThumbCache _thumbCache = null!;
    private readonly MediaAssetsSettingsIO _io = null!;
    private readonly ThumbnailService _thumbGen = null!;
    private readonly FileOperationService _fileOps = null!;
    private readonly TaskQueue _queue = null!;
    private readonly OperationLogService _opLog = null!;

    /// <summary>全部可浏览的位置行（一次入内存；目录切换 = 内存筛选，⛔ 不逐目录查库）。</summary>
    private readonly List<BrowseEntry> _entries = new();

    /// <summary>存储源级根节点（未扁平化）。</summary>
    private readonly List<AssetDirNode> _roots = new();

    /// <summary>全部节点的扁平副本（找父子 / 面包屑用，与「可见节点」无关）。</summary>
    private readonly List<AssetDirNode> _allNodes = new();

    private CancellationTokenSource? _thumbCts;

    /// <summary>构造期卫哨：属性初始赋值不触发重取（数据还没读进来）。</summary>
    private bool _booted;

    /// <summary>当前目录的**未筛选**位置数（空态文案要区分「目录本来就空」与「筛选没命中」）。</summary>
    private int _dirCount;

    private bool _hasMissingThumbs;

    // ==================== 绑定集合 ====================

    /// <summary>左树可见节点（按展开状态扁平化）。</summary>
    public ObservableCollection<AssetDirNode> TreeNodes { get; } = new();

    /// <summary>右侧当前页的位置行（图标卡与详细视图**共用**这一个集合）。</summary>
    public ObservableCollection<AssetTile> Items { get; } = new();

    /// <summary>
    /// 两套视图各自的数据源：**只有当前视图拿得到 <see cref="Items"/>**，另一套为 null。
    /// <para>🔴 为什么不让两个 <c>ListBox</c> 都直接绑 <see cref="Items"/>：隐藏的列表照样会为每一项建容器
    /// （120 张卡 = 120 个 <c>ListBoxItem</c>，详细视图那一份还是 11 列的 <c>ColumnGrid</c>）⇒ 切目录时
    /// 双份容器 + 双份绑定求值，白等几百毫秒到几秒。数据源置空后隐藏列表不建容器。</para>
    /// </summary>
    public ObservableCollection<AssetTile>? TileSource => IsTileView ? Items : null;

    /// <summary>详细视图的数据源（图标视图下为 null，见 <see cref="TileSource"/>）。</summary>
    public ObservableCollection<AssetTile>? DetailSource => IsTileView ? null : Items;

    /// <summary>选中行（唯一真相在本集合；批量动作的对象）。</summary>
    public ObservableCollection<AssetRow> SelectedRows { get; } = new();

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
    [ObservableProperty] private int _iconSize = 144;
    [ObservableProperty] private bool _isTreeVisible = true;

    /// <summary>图标卡尺寸档位（工具条按钮组 / 校验用）。</summary>
    public static readonly int[] IconSizeChoices = { 96, 144, 200 };

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

    // ==================== 派生状态 ====================

    public bool IsDbAvailable => _assets?.IsAvailable ?? false;
    public bool HasItems => Items.Count > 0;
    public bool HasTree => TreeNodes.Count > 0;
    public bool HasSelection => Selected != null;
    public bool ShowDetailBody => Selected != null;
    public int SelectedCount => SelectedRows.Count;
    public bool HasSelectedRows => SelectedRows.Count > 0;
    public bool CanGoUp => SelectedNode is { IsStorageRoot: false, DirPath.Length: > 0 };
    public bool CanGenerateThumbs => _hasMissingThumbs && SelectedNode is { IsStorageRoot: false };

    /// <summary>空态文案：区分「目录本来就空」与「筛选 / 搜索没命中」。</summary>
    public string EmptyText => _dirCount > 0
        ? "当前筛选 / 搜索没有匹配项。清空搜索或把「类型」换成「全部类型」再看。"
        : "该目录下没有已索引资产。新文件请到「全部资产」用 📁 扫描入库。";

    /// <summary>详情卡数据源（当前选中位置行的资产行）。</summary>
    public AssetRow? DetailRow => Selected?.Row;

    protected override string PageKey => "assets-browse";

    protected override bool PagingEnabled => true;

    public override IReadOnlyList<int> PageSizeChoices => TilePageSizes;

    protected override IReadOnlyList<ListColumn> Columns => AssetColumns.All;

    /// <summary>选中项变化 ⇒ 请 View 把 <c>SelectedItems</c> 回填成 <see cref="SelectedRows"/>。</summary>
    public event EventHandler? SelectionSyncRequested;

    public AssetBrowseViewModel() { }

    public AssetBrowseViewModel(AssetService assets, AssetBrowseService browse, AssetThumbCache thumbs,
        MediaAssetsSettingsIO io, ThumbnailService thumbGen, FileOperationService fileOps,
        TaskQueue queue, OperationLogService log, IColumnLayoutService cols)
        : base(cols)
    {
        _assets = assets;
        _browse = browse;
        _thumbCache = thumbs;
        _io = io;
        _thumbGen = thumbGen;
        _fileOps = fileOps;
        _queue = queue;
        _opLog = log;

        LoadLayout();
        // 视频专属三列在本页默认隐藏（本页没有「筛选类型 = 视频」这层语义）；用户改过的偏好优先
        var saved = cols.LoadPage(PageKey);
        foreach (var key in new[] { "Duration", "Resolution", "Codec" })
            if (!saved.ContainsKey(key)) SetColumnVisible(key, false);

        BuildChoices();
        IsTileView = !string.Equals(io.Settings.BrowseViewMode, "details", StringComparison.OrdinalIgnoreCase);
        IconSize = NormalizeIconSize(io.Settings.BrowseIconSize);
        PageSize = 120;                     // 图标视图一屏约 4~6 列 × 2~3 行

        Reload(restoreLastFolder: true);
        _booted = true;
        StatusText = StatusSummary();
    }

    // ==================== 初始化 ====================

    private static int NormalizeIconSize(int size)
        => IconSizeChoices.Contains(size) ? size : 144;

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

    // ==================== 加载 ====================

    [RelayCommand]
    private void Refresh()
    {
        Reload(restoreLastFolder: false);
        StatusText = IsDbAvailable
            ? $"已重读索引：{_entries.Count} 个文件位置 / {CountDirectoryNodes()} 个目录"
            : "⚠ 资产库不可用（数据库打不开）——本页只读，不会崩。";
    }

    private int CountDirectoryNodes() => _allNodes.Count(n => !n.IsStorageRoot);

    private void Reload(bool restoreLastFolder)
    {
        var keepKey = restoreLastFolder
            ? _io.Settings.LastBrowseFolder
            : AssetBrowseService.KeyOf(SelectedNode);

        _entries.Clear();
        _roots.Clear();
        _allNodes.Clear();
        if (IsDbAvailable)
        {
            _entries.AddRange(_browse.LoadEntries());
            _roots.AddRange(_browse.BuildTree(_entries, _assets.ListStorages()));
        }

        CollectNodes();
        RebuildTree();
        SelectInitial(keepKey);
    }

    /// <summary>把全部节点（含未展开的子节点）收进扁平副本。</summary>
    private void CollectNodes()
    {
        void Walk(AssetDirNode n)
        {
            _allNodes.Add(n);
            foreach (var c in n.Children) Walk(c);
        }
        foreach (var root in _roots) Walk(root);
    }

    /// <summary>按展开状态扁平化（只保留「祖先链全展开」的节点）。</summary>
    private void RebuildTree()
    {
        void Add(AssetDirNode n, int depth)
        {
            n.Depth = depth;
            TreeNodes.Add(n);
            if (!n.IsExpanded) return;
            foreach (var c in n.Children) Add(c, depth + 1);
        }

        TreeNodes.Clear();
        foreach (var root in _roots) Add(root, 0);
        OnPropertyChanged(nameof(HasTree));
    }

    /// <summary>进页面 / 重读后的默认选中：上次的目录 → 第一个有内容的目录 → 第一个目录。</summary>
    private void SelectInitial(string? key)
    {
        var (sid, dir) = AssetBrowseService.ParseKey(key);
        var target = _allNodes.FirstOrDefault(n => !n.IsStorageRoot && n.StorageId == sid && n.DirPath == dir)
                     ?? _allNodes.FirstOrDefault(n => !n.IsStorageRoot && n.TotalCount > 0)
                     ?? _allNodes.FirstOrDefault(n => !n.IsStorageRoot);

        if (target == null)
        {
            // 没有任何目录（库空 / 库不可用）⇒ 清干净，右栏走空态
            SelectedNode = null;
            Crumbs.Clear();
            ResetToFirstPage();
            return;
        }

        SelectNode(target);
    }

    // ==================== 目录树 ====================

    /// <summary>点树节点：存储源抬头只展开 / 折叠，目录节点切换右侧内容。</summary>
    [RelayCommand]
    private void SelectNode(AssetDirNode? node)
    {
        if (node == null) return;

        if (node.IsStorageRoot)
        {
            node.IsExpanded = !node.IsExpanded;
            RebuildTree();
            return;
        }

        foreach (var n in _allNodes) n.IsSelected = ReferenceEquals(n, node);
        SelectedNode = node;
        ExpandAncestors(node);
        RebuildTree();
        BuildCrumbs();

        _io.Settings.LastBrowseFolder = AssetBrowseService.KeyOf(node);
        _io.Save();

        ResetToFirstPage();     // → RefreshView → ApplyView（重取当前目录的一页）
        StatusText = StatusSummary();
    }

    private void ExpandAncestors(AssetDirNode node)
    {
        var cur = ParentOf(node);
        while (cur != null)
        {
            cur.IsExpanded = true;
            cur = cur.IsStorageRoot ? null : ParentOf(cur);
        }
    }

    private AssetDirNode? ParentOf(AssetDirNode node)
    {
        if (node.IsStorageRoot) return null;
        var root = _roots.FirstOrDefault(r => r.StorageId == node.StorageId);
        if (node.DirPath.Length == 0) return root;
        return _allNodes.FirstOrDefault(n => !n.IsStorageRoot && n.StorageId == node.StorageId
                                             && n.DirPath == AssetService.ParentDir(node.DirPath));
    }

    [RelayCommand]
    private void GoUp()
    {
        if (!CanGoUp || SelectedNode == null) return;

        var parent = ParentOf(SelectedNode);
        if (parent == null) return;
        // 上一级是存储源抬头（不可承载文件）⇒ 落到它的「（根目录）」
        if (parent.IsStorageRoot)
            parent = _allNodes.FirstOrDefault(n => !n.IsStorageRoot && n.StorageId == parent.StorageId && n.DirPath.Length == 0)
                     ?? parent;
        SelectNode(parent);
    }

    [RelayCommand]
    private void GoCrumb(BrowseCrumb? crumb)
    {
        if (crumb?.Node == null) return;
        if (crumb.Node.IsStorageRoot) crumb.Node.IsExpanded = true;
        SelectNode(crumb.Node);
    }

    [RelayCommand]
    private void ToggleTree() => IsTreeVisible = !IsTreeVisible;

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var n in _allNodes) n.IsExpanded = true;
        RebuildTree();
        StatusText = "已展开全部目录。";
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var n in _allNodes) n.IsExpanded = false;
        foreach (var root in _roots) root.IsExpanded = true;
        RebuildTree();
        StatusText = "已折叠全部目录。";
    }

    private void BuildCrumbs()
    {
        Crumbs.Clear();
        if (SelectedNode == null) return;

        var root = _roots.FirstOrDefault(r => r.StorageId == SelectedNode.StorageId);
        if (root != null) Crumbs.Add(new BrowseCrumb(root.Name, null));

        var chain = new List<AssetDirNode>();
        var cur = SelectedNode;
        while (cur != null && !cur.IsStorageRoot)
        {
            chain.Add(cur);
            cur = ParentOf(cur);
        }
        chain.Reverse();
        foreach (var n in chain)
            Crumbs.Add(new BrowseCrumb(n.DirPath.Length == 0 ? AssetBrowseService.RootDirName : n.Name, n));
    }

    // ==================== 视图 ====================

    protected override void RefreshView() => ApplyView();

    private void ApplyView()
    {
        Items.Clear();
        _dirCount = 0;

        if (SelectedNode == null)
        {
            TotalCount = 0;
            _hasMissingThumbs = false;
            OnPropertyChanged(nameof(CanGenerateThumbs));
            NotifyItemsChanged();
            return;
        }

        var dirRows = _entries
            .Where(e => e.Row.StorageId == SelectedNode.StorageId && e.Dir == SelectedNode.DirPath)
            .ToList();
        _dirCount = dirRows.Count;

        IEnumerable<BrowseEntry> q = dirRows;
        var type = FilterType?.Value ?? "";
        if (type.Length > 0) q = q.Where(e => string.Equals(e.Row.Type, type, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(SearchText)) q = q.Where(e => AssetService.MatchesKeyword(e.Row, SearchText));

        var list = SortEntries(q).ToList();

        TotalCount = list.Count;
        var before = PageIndex;
        ClampPageIndex();
        if (PageIndex != before) return;   // 页码收敛后已按新页重取，⛔ 外层别再取一遍

        var offset = (long)(PageIndex - 1) * PageSize;
        var page = list.Skip((int)Math.Min(offset, int.MaxValue)).Take(PageSize).ToList();

        foreach (var e in page) Items.Add(new AssetTile(e.Row, e.LocationId, e.Dir, IconSize));

        ReconcileSelection();
        RefreshMissingThumbFlag(dirRows);   // 🔴 逐个 File.Exists 会碰磁盘 ⇒ 放后台，⛔ 别卡在切目录的这 200ms 里

        SyncChoiceSelection();
        UpdateHeaders();
        NotifyItemsChanged();
        OnPropertyChanged(nameof(CanGenerateThumbs));

        LoadThumbs();
    }

    private List<BrowseEntry> SortEntries(IEnumerable<BrowseEntry> rows)
    {
        var key = string.IsNullOrEmpty(SortColumn) ? "Name" : SortColumn;
        IOrderedEnumerable<BrowseEntry> ordered = key switch
        {
            "Type" => SortDescending ? rows.OrderByDescending(e => e.Row.TypeText, StringComparer.CurrentCulture) : rows.OrderBy(e => e.Row.TypeText, StringComparer.CurrentCulture),
            "Size" => SortDescending ? rows.OrderByDescending(e => e.Row.Size) : rows.OrderBy(e => e.Row.Size),
            "Modified" => SortDescending ? rows.OrderByDescending(e => e.Row.Mtime ?? "") : rows.OrderBy(e => e.Row.Mtime ?? ""),
            "Storage" => SortDescending ? rows.OrderByDescending(e => e.Row.StorageName, StringComparer.CurrentCulture) : rows.OrderBy(e => e.Row.StorageName, StringComparer.CurrentCulture),
            "Duration" => SortDescending ? rows.OrderByDescending(e => e.Row.DurationSeconds) : rows.OrderBy(e => e.Row.DurationSeconds),
            "Resolution" => SortDescending ? rows.OrderByDescending(e => e.Row.ResolutionText, StringComparer.CurrentCulture) : rows.OrderBy(e => e.Row.ResolutionText, StringComparer.CurrentCulture),
            "Codec" => SortDescending ? rows.OrderByDescending(e => e.Row.CodecText, StringComparer.CurrentCulture) : rows.OrderBy(e => e.Row.CodecText, StringComparer.CurrentCulture),
            "Tags" => SortDescending ? rows.OrderByDescending(e => e.Row.TagsText, StringComparer.CurrentCulture) : rows.OrderBy(e => e.Row.TagsText, StringComparer.CurrentCulture),
            "Categories" => SortDescending ? rows.OrderByDescending(e => e.Row.CategoriesText, StringComparer.CurrentCulture) : rows.OrderBy(e => e.Row.CategoriesText, StringComparer.CurrentCulture),
            "Collections" => SortDescending ? rows.OrderByDescending(e => e.Row.CollectionsText, StringComparer.CurrentCulture) : rows.OrderBy(e => e.Row.CollectionsText, StringComparer.CurrentCulture),
            _ => SortDescending ? rows.OrderByDescending(e => e.Row.Name, StringComparer.CurrentCulture) : rows.OrderBy(e => e.Row.Name, StringComparer.CurrentCulture)
        };
        return ordered.ThenBy(e => e.LocationId).ToList();
    }

    private static bool WantsThumb(AssetRow row)
        => row.Type is AssetTypes.Video or AssetTypes.Image;

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
    }

    private void NotifyItemsChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private string StatusSummary()
    {
        if (!IsDbAvailable) return "⚠ 资产库不可用（数据库打不开）——本页只读，不会崩。";
        if (IsDbAvailable && _entries.Count == 0) return "索引里还没有任何文件位置——先到「全部资产」用 📁 扫描把文件入库。";

        var dir = SelectedNode == null
            ? "（未选目录）"
            : SelectedNode.DirPath.Length == 0
                ? $"{SelectedNode.Name}"
                : $"{SelectedNode.Name}（{SelectedNode.DirPath}）";

        var missing = 0;
        if (SelectedNode != null)
            missing = _entries.Count(e => e.Row.StorageId == SelectedNode.StorageId && e.Dir == SelectedNode.DirPath && e.Row.IsMissing);

        var text = $"{dir}：{TotalCount} 项";
        if (_dirCount != TotalCount) text += $"（目录共 {_dirCount} 项，已筛选）";
        if (missing > 0) text += $" · 其中 {missing} 项在磁盘上已找不到（标灰）";
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
        OnPropertyChanged(nameof(CanGenerateThumbs));
    }

    [RelayCommand]
    private void SetIconSize(ChoiceItem? item)
    {
        if (item == null || !int.TryParse(item.Value, out var size)) return;
        IconSize = NormalizeIconSize(size);
        _io.Settings.BrowseIconSize = IconSize;
        _io.Save();
        SyncChoiceSelection();
        ApplyView();   // 卡片宽度变了 ⇒ 整页重建（选中按 LocationId 认领，不丢）
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
        // 切视图 ⇒ 只让当前视图持有数据源（隐藏那套不建容器）
        OnPropertyChanged(nameof(TileSource));
        OnPropertyChanged(nameof(DetailSource));
        OnPropertyChanged(nameof(CanGenerateThumbs));
        SyncChoiceSelection();
    }

    partial void OnIconSizeChanged(int value) => SyncChoiceSelection();

    partial void OnSelectedNodeChanged(AssetDirNode? value)
    {
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CanGenerateThumbs));
    }

    partial void OnSelectedChanged(AssetTile? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailRow));
        OnPropertyChanged(nameof(DetailFolder));
        RaiseDetailChanged();
    }

    // ==================== 选中（Ctrl 增选 / Shift 连选 / Ctrl+A 全选） ====================

    /// <summary>View 的选中变化回填（点卡片 / Ctrl 增选 / Shift 连选 / Ctrl+A 全走这里）。</summary>
    public void SetSelectionFromView(IReadOnlyList<AssetTile> tiles, AssetTile? clicked)
    {
        SelectedRows.Clear();
        foreach (var t in tiles) SelectedRows.Add(t.Row);

        NotifySelectionMeta();
        Selected = clicked
                   ?? (SelectedRows.Count > 0
                       ? Items.FirstOrDefault(t => ReferenceEquals(t.Row, SelectedRows[^1]))
                       : null);
        RaiseDetailChanged();
    }

    private void NotifySelectionMeta()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelectedRows));
    }

    /// <summary>列表重建 / 尺寸切档后按「资产 id + 位置」认领选中项（Items 里都是新实例）；切目录则自然清空。</summary>
    private void ReconcileSelection()
    {
        var ids = SelectedRows.Select(r => r.Id).ToHashSet();
        var picked = Items.Where(t => ids.Contains(t.Row.Id)).ToList();

        SelectedRows.Clear();
        foreach (var t in picked) SelectedRows.Add(t.Row);

        var pick = Selected == null
            ? null
            : picked.FirstOrDefault(t => t.LocationId == Selected.LocationId) ?? picked.FirstOrDefault();

        Selected = pick ?? (Items.Count > 0 ? Items[0] : null);
        if (Selected != null && SelectedRows.Count == 0) SelectedRows.Add(Selected.Row);

        NotifySelectionMeta();
        SelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        RaiseDetailChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        SelectedRows.Clear();
        foreach (var t in Items) SelectedRows.Add(t.Row);
        Selected = Items.FirstOrDefault();
        NotifySelectionMeta();
        SelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        StatusText = $"已全选当前页 {Items.Count} 项。";
    }

    [RelayCommand]
    private void SelectNone()
    {
        SelectedRows.Clear();
        NotifySelectionMeta();
        SelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        StatusText = "已取消选择。";
    }

    [RelayCommand]
    private void InvertSelection()
    {
        var picked = SelectedRows.Select(r => r.Id).ToHashSet();
        SelectedRows.Clear();
        foreach (var t in Items)
            if (!picked.Contains(t.Row.Id)) SelectedRows.Add(t.Row);
        NotifySelectionMeta();
        SelectionSyncRequested?.Invoke(this, EventArgs.Empty);
        StatusText = $"已反选当前页，选中 {SelectedRows.Count} 项。";
    }

    private List<AssetTile> BatchTiles()
        => Items.Where(t => SelectedRows.Any(r => r.Id == t.Row.Id)).ToList();

    // ==================== 缩略图 ====================

    private void LoadThumbs()
    {
        // 只取消不 Dispose：正在等待的信号量/任务还握着这个 token（Dispose 后注册会抛 ObjectDisposedException）
        _thumbCts?.Cancel();
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;

        var width = IconSize;
        foreach (var tile in Items)
        {
            if (!tile.WantsThumb) continue;
            _ = AssignThumbAsync(tile, width, ct);   // 并发闸在缓存里（4）；⛔ 不要在这里串行 await 120 张
        }
    }

    /// <summary>单张缩略图：后台解码 → UI 线程赋值。解码失败 / 文件已删 ⇒ 保持占位（⛔ 不抛、⛔ 不弹窗）。</summary>
    private async Task AssignThumbAsync(AssetTile tile, int width, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested) return;
            var path = _thumbGen.ThumbPathFor(tile.Row.FilePath);
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

    /// <summary>本目录里有没有「缺缩略图」的视频 / 图片（决定工具条按钮可用性）。后台算，结果回投 UI 线程。</summary>
    private void RefreshMissingThumbFlag(List<BrowseEntry> list)
    {
        var version = ++_thumbScanVersion;

        var paths = new List<string>();
        foreach (var e in list)
            if (WantsThumb(e.Row)) paths.Add(_thumbGen.ThumbPathFor(e.Row.FilePath));

        if (paths.Count == 0)
        {
            SetMissingThumbs(false, version);
            return;
        }

        _ = Task.Run(async () =>
        {
            var missing = false;
            try
            {
                // 搜索框每敲一个字都会走到这里 ⇒ 短延迟去抖，只为最后一次真正扫磁盘
                await Task.Delay(180).ConfigureAwait(false);
                if (version != _thumbScanVersion) return;

                foreach (var p in paths)
                    if (!File.Exists(p)) { missing = true; break; }
            }
            catch { /* 盘符掉线 / 权限不足 ⇒ 当作没有缺口 */ }
            Dispatcher.UIThread.Post(() => SetMissingThumbs(missing, version));
        });
    }

    private int _thumbScanVersion;

    private void SetMissingThumbs(bool missing, int version)
    {
        if (version != _thumbScanVersion || _hasMissingThumbs == missing) return;
        _hasMissingThumbs = missing;
        OnPropertyChanged(nameof(CanGenerateThumbs));
    }

    /// <summary>工具条「🖼 生成本目录缩略图」：只处理**当前目录**里还缺缓存的视频 / 图片。</summary>
    [RelayCommand]
    private void GenerateThumbs()
    {
        if (SelectedNode == null) { StatusText = "先在左边选一个目录。"; return; }

        var node = SelectedNode;
        var candidates = _entries
            .Where(e => e.Row.StorageId == node.StorageId && e.Dir == node.DirPath && WantsThumb(e.Row))
            .ToList();
        if (candidates.Count == 0) { StatusText = "这个目录里没有视频 / 图片（缩略图只对这两类生成）。"; return; }

        _queue.Enqueue($"生成缩略图（{node.Name}）", async (ct, p) =>
        {
            // 有没有缓存要碰磁盘 ⇒ 在队列线程里筛，⛔ 不在 UI 线程逐个 File.Exists
            var targets = await Task.Run(
                () => candidates.Where(e => !File.Exists(_thumbGen.ThumbPathFor(e.Row.FilePath))).ToList(), ct)
                .ConfigureAwait(false);
            if (targets.Count == 0) { p.Report("这个目录的缩略图都齐了，无需生成。"); return; }

            var ok = 0;
            var fail = 0;
            for (var i = 0; i < targets.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                var e = targets[i];
                p.Report($"[{i + 1}/{targets.Count}] {e.Row.FileName}");

                var loc = new FileLocation
                {
                    Id = e.LocationId,
                    AssetId = e.Row.Id,
                    StorageId = e.Row.StorageId,
                    Path = e.Row.FilePath,
                    FileName = e.Row.FileName,
                    Ext = Path.GetExtension(e.Row.FilePath),
                    Size = e.Row.Size,
                    Mtime = e.Row.Mtime
                };
                var (good, msg, _) = await _thumbGen.GenerateAsync(loc, ct).ConfigureAwait(false);
                if (good) ok++;
                else { fail++; p.Report($"跳过 {e.Row.FileName}：{msg}"); }
            }
            p.Report($"缩略图完成：成功 {ok}，失败 {fail}。");
            Dispatcher.UIThread.Post(() =>
            {
                LoadThumbs();
                // 只重算**本目录**的缺口标记（按钮只管当前目录）
                if (SelectedNode != null)
                    RefreshMissingThumbFlag(_entries.Where(e => e.Row.StorageId == SelectedNode.StorageId
                                                               && e.Dir == SelectedNode.DirPath).ToList());
            });
        });

        StatusText = $"已把「{node.Name}」的缩略图任务排进队列（进度看「任务中心」，可取消）。";
    }
}
