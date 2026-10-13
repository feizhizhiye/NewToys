using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.MediaAssets.Views.Detail;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「全部资产」页（NT-016）—— 本模块的**样板列表页**：后续 5 个类型页与标签 / 集合 / 存储源 / 任务 /
/// 回收站页都照这个模式复刻（列显隐 / 排序 / 筛选由基类承载，详情面板由 **Shell** 承载）。
/// <para>
/// 三个要点：
/// <list type="number">
///   <item>本页是**索引视图**：显示名这类逻辑改动直接改库；**重命名 / 移动 / 删除磁盘文件归 NT-019**，一个都不做。</item>
///   <item>详情面板实现 <see cref="IDetailPanelProvider"/>，<see cref="DetailContent"/> 返回 <c>AssetDetailView</c>
///        → 页面 XAML 里 **⛔ 不再自带右栏**（否则会出现两个详情卡）。</item>
///   <item>列键与行模型属性名**不一致**的列，必须在 <see cref="FieldText"/> 里显式映射 ——
///        基类只做反射取同名属性，取不到则该条筛选**静默失效**（v3.15 教训）。</item>
/// </list>
/// </para>
/// </summary>
public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider
{
    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly AssetService _assets = null!;
    private readonly ScanService _scan = null!;
    private readonly TagService _tagSvc = null!;
    private readonly CategoryService _catSvc = null!;
    private readonly CollectionService _colSvc = null!;
    private readonly RelationService _relationSvc = null!;
    private readonly OperationLogService _opLog = null!;
    private readonly AssetNavigationStore _navStore = null!;
    private readonly FileOperationService _fileOps = null!;
    private readonly AssetImportExport _impExp = null!;
    private readonly IAppConfigService? _app;
    private readonly List<AssetRow> _all = new();
    private AssetDetailView? _detailView;

    /// <summary>详情卡正在回填（改名框失焦保存要跳过这一次，⛔ 别把"显示"当成"用户改了"）。</summary>
    private bool _loadingDetail;

    public ObservableCollection<AssetRow> Items { get; } = new();

    /// <summary>当前选中资产的**全部文件位置**（详情卡列出；一个资产可能有多份拷贝）。</summary>
    public ObservableCollection<FileLocation> Locations { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private AssetRow? _selected;
    [ObservableProperty] private string _searchText = "";

    /// <summary>详情卡的显示名输入框（双向绑；**失焦**保存，⛔ 卡内不放按钮）。</summary>
    [ObservableProperty] private string _detailDisplayName = "";

    // ==================== 筛选条下拉 ====================

    public ObservableCollection<FilterOption> TypeOptions { get; } = new();
    public ObservableCollection<FilterOption> StorageOptions { get; } = new();
    public ObservableCollection<FilterOption> StatusOptions { get; } = new();
    public ObservableCollection<FilterOption> TagOptions { get; } = new();

    [ObservableProperty] private FilterOption? _filterType;
    [ObservableProperty] private FilterOption? _filterStorage;
    [ObservableProperty] private FilterOption? _filterStatus;
    [ObservableProperty] private FilterOption? _filterTag;

    // ==================== 表头 ====================

    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _typeHeader = "类型";
    [ObservableProperty] private string _tagsHeader = "标签";
    [ObservableProperty] private string _categoriesHeader = "分类";
    [ObservableProperty] private string _collectionsHeader = "集合";
    [ObservableProperty] private string _sizeHeader = "大小";
    [ObservableProperty] private string _modifiedHeader = "修改时间";
    [ObservableProperty] private string _storageHeader = "存储源";

    // ==================== 列显隐（XAML 只能绑具名属性；状态在基类，落 config\columnlayout.json） ====================

    /// <summary>勾选列（Pick）恒可见 —— 它是批量操作的前提。</summary>
    public bool IsPickVisible => true;

    public bool IsNameVisible => Vis("Name");
    public bool IsTypeVisible => Vis("Type");
    public bool IsTagsVisible => Vis("Tags");
    public bool IsCategoriesVisible => Vis("Categories");
    public bool IsCollectionsVisible => Vis("Collections");
    public bool IsSizeVisible => Vis("Size");
    public bool IsModifiedVisible => Vis("Modified");
    public bool IsStorageVisible => Vis("Storage");

    protected override string PageKey => "assets-all";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // 🔴 v3.34 / NT-018：首列 = **勾选列**（Pick）——
        //    加了首列后，表头 / 示例行 / 行模板里**所有 Grid.Column 索引都要 +1**（⛔ 别漏一处）。
        new("Pick", "", "34", sortable: false),
        // Sample 要给"典型内容宽度"（v3.22）：示例宽 ≤ 表头宽的列会被锁成表头宽、不可拖
        new("Name", "名称", "2.6*", sample: "The.Matrix.1999.1080p.mkv"),
        new("Type", "类型", "0.8*", sample: "视频"),
        new("Tags", "标签", "1.4*", sample: "科幻, 收藏"),
        new("Categories", "分类", "1.2*", sample: "电影 / 动作"),
        new("Collections", "集合", "1.2*", sample: "周末片单"),
        new("Size", "大小", "0.9*", sample: "4.2 GB"),
        new("Modified", "修改时间", "1.3*", sample: "2026-10-13 21:04"),
        new("Storage", "存储源", "1.2*", sample: "移动硬盘 D")
    };

    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    /// <summary>
    /// 列键 → 行里真正的值（筛选用）。
    /// 🔴 **列键 ≠ 属性名的列必须在这里显式写**（基类反射取同名属性会拿到 null → 该条筛选**静默失效**）：
    /// Tags / Categories / Collections / Modified / Storage 五个都是。
    /// </summary>
    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not AssetRow a) return base.FieldText(row, columnKey);
        return columnKey switch
        {
            "Name" => a.Name,
            "Type" => a.TypeText,
            "Tags" => a.TagsText,
            "Categories" => a.CategoriesText,
            "Collections" => a.CollectionsText,
            "Size" => a.SizeText,
            "Modified" => a.ModifiedText,
            "Storage" => a.StorageName,
            "Status" => a.StatusText,
            _ => base.FieldText(row, columnKey)
        };
    }

    /// <summary>数值比较（「大于 / 小于」）：只有大小列有数值语义（字节数）。</summary>
    protected override double? FieldNumber(object row, string columnKey)
        => row is AssetRow a && columnKey == "Size" ? a.Size : base.FieldNumber(row, columnKey);

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// v3.22 §4.8.1：列表为空时才显示**示例行**（让用户在没有数据时先看清每列是什么，
    /// 同时给「示例宽 ≤ 表头宽 → 锁列宽」提供测量样本）。有真实数据后整条消失。
    /// ⛔ 它不在 <see cref="Items"/> 里：不参与筛选 / 排序 / 统计。
    /// </summary>
    public bool ShowSampleRow => HasSampleRow && Items.Count == 0;

    // ==================== v3.32 §4.8.3：详情卡示例态 ====================

    /// <summary>详情卡的**示例行**（与列表示例行同一批数据）。</summary>
    private static readonly AssetRow SampleRow = new()
    {
        Name = "The.Matrix.1999.1080p.mkv",
        Title = "The.Matrix.1999.1080p.mkv",
        Type = AssetTypes.Video,
        Status = AssetStatuses.Active,
        FileName = "The.Matrix.1999.1080p.mkv",
        FilePath = @"D:\Movies\The.Matrix.1999.1080p.mkv",
        Size = 4_509_715_660,                       // ≈ 4.2 GB
        Mtime = "2026-10-13 21:04:00",
        StorageName = "移动硬盘 D",
        TagsText = "科幻, 收藏",
        CategoriesText = "电影 / 动作",
        CollectionsText = "周末片单"
    };

    /// <summary>详情卡的数据源：有选中项用真值，没选中用**示例行**（示例态下卡片整体禁用）。</summary>
    public AssetRow DetailRow => Selected ?? SampleRow;

    /// <summary>详情卡正文是否显示（有选中项，或处于示例态）。</summary>
    public bool ShowDetailBody => HasSelection || IsDetailSample;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _app?.VideoDbFolder ?? "";

    // ==================== NT-022：列表 JSON 导入 / 导出 ====================

    /// <summary>
    /// 📤 导出**当前列表**（<c>_all</c>，即当前模式的全量、⛔ 不含筛选后的可见集）为 JSON 文本。
    /// 组织信息（标签 / 分类 / 集合）按**名字**导出（⛔ 不存内部 id）——换机 / 合并都能用。
    /// </summary>
    public string ExportAssetsJson()
    {
        var scope = IsDraft ? "draft-preview" : "library";
        return _impExp.Export(_all, scope);
    }

    /// <summary>
    /// 📥 导入 JSON：**追加合并**（按路径去重，⛔ 不静默覆盖）。完成后刷新列表。
    /// </summary>
    public (bool Ok, string Message) ImportAssetsJson(string json)
    {
        var (ok, msg, _, _, _) = _impExp.Import(json);
        if (ok) Reload();
        return (ok, msg);
    }

    /// <summary>资产库是否可用（不可用时页面照常显示 + 状态栏提示，⛔ 不崩）。</summary>
    public bool IsDbAvailable => _assets?.IsAvailable ?? false;

    public AssetAllViewModel() { }

    public AssetAllViewModel(AssetService assets, ScanService scan, IColumnLayoutService cols,
        IAppConfigService? app = null,
        TagService? tagSvc = null, CategoryService? catSvc = null, CollectionService? colSvc = null,
        RelationService? relationSvc = null, OperationLogService? opLog = null, AssetNavigationStore? navStore = null,
        FileOperationService? fileOps = null, AssetImportExport? impExp = null)
        : base(cols)
    {
        _assets = assets;
        _scan = scan;
        _app = app;
        _tagSvc = tagSvc!;
        _catSvc = catSvc!;
        _colSvc = colSvc!;
        _relationSvc = relationSvc!;
        _opLog = opLog!;
        _navStore = navStore!;
        _fileOps = fileOps!;
        _impExp = impExp!;

        LoadLayout();                       // 列显隐 + 详情栏开合（基类）
        BuildStaticOptions();               // 类型 / 状态（固定值）
        Reload();                           // 存储源 / 标签下拉要读库 → 放在读库之后
        UpdateDetailSample(false);          // v3.32 §4.8.3：初始没有选中项 → 详情卡进"示例态"
        LoadDetail(null);

        StatusText = StatusSummary();
    }

    // ==================== v3.34：类型页「薄壳」共用的两处缝 ====================

    /// <summary>
    /// 状态栏前缀（v3.34）：5 个类型页（<c>AssetVideoViewModel</c> 等）覆写成 <c>"视频："</c> 之类；默认空串。
    /// <para>🔴 覆写**必须写成常量表达式**（<c>protected override string StatusPrefix =&gt; "视频：";</c>），
    /// ⛔ 别用字段 / 构造期赋的值 —— 基类构造函数里就会读它写状态栏，那一刻派生类的字段还没初始化。</para>
    /// </summary>
    protected virtual string StatusPrefix => "";

    /// <summary>一次读库的最大行数（v3.34）：超过时状态栏**显式提示**，⛔ 不再静默截断。</summary>
    public const int QueryLimit = 2000;

    /// <summary>库里资产数 ≥ <see cref="QueryLimit"/> → 列表只显示前 N 条（状态栏提示）。</summary>
    private bool _truncated;

    /// <summary>截断提示后缀（没截断时为空串）。</summary>
    private string TruncatedHint => _truncated ? $"（库中超过 {QueryLimit} 条，仅显示前 {QueryLimit} 条）" : "";

    /// <summary>列表概况文案（状态栏用）：类型前缀 + 条数 + 截断提示。派生类构造末尾可调它刷新文案。</summary>
    protected string StatusSummary() => IsDbAvailable
        ? $"{StatusPrefix}共 {Items.Count} 个资产" + (Items.Count == 0 ? "（点「📂 导入」添加存储源或文件）" : "") + TruncatedHint
        : $"{StatusPrefix}资产库不可用（数据库打不开）——本页只读，不会崩。";

    /// <summary>
    /// v3.34：把筛选条的「类型」预设为某个类型（**类型页薄壳**构造末尾调一次）。
    /// <para>基类构造已把全量行装进内存 → 这里只**重算视图**（<c>FilterType</c> 一变即触发
    /// <c>OnFilterTypeChanged → ApplyView</c>），⛔ **不重查库**。</para>
    /// <para>🔴 「类型」是**页面属性**（筛选条下拉），不在基类 <c>Filters</c> 里 ——
    /// 所以点「🧹 清除筛选（全部列）」或表头右键快捷筛选**不会把它清掉**（这正是类型页要的语义）。</para>
    /// </summary>
    protected void SetPresetType(string type)
    {
        var hit = TypeOptions.FirstOrDefault(o => string.Equals(o.Value, type, StringComparison.Ordinal));
        if (hit != null) FilterType = hit;      // → OnFilterTypeChanged → ApplyView
        StatusText = StatusSummary();           // 条数文案跟上（基类构造里写的是"全部"的条数）
    }

    // ==================== 加载 ====================

    [RelayCommand]
    private void Refresh()
    {
        Reload();
        StatusText = IsDbAvailable
            ? $"{StatusPrefix}已重读资产库：共 {Items.Count} 个资产" + TruncatedHint
            : $"{StatusPrefix}资产库不可用（数据库打不开）。";
    }

    private void Reload()
    {
        _all.Clear();
        if (!IsDbAvailable) { _truncated = false; ApplyView(); return; }
        AddRows(_assets.QueryRows(limit: QueryLimit));
        _truncated = _all.Count >= QueryLimit;   // v3.34：截断了就明说（⛔ 不再静默少显示）
        RebuildStorageOptions();
        RebuildTagOptions();
        ApplyView();
    }

    // ==================== 导入：文件夹 / 文件（由 View 弹选择框后调用） ====================

    /// <summary>
    /// v3.29 草稿态：列表里装的**不是库里的数据**，而是「预览扫描」的结果（还没入库）。
    /// 用户看完决定：💾 加入库（<see cref="CommitDraftCommand"/>）或 🧹 清除列表（<see cref="ClearListCommand"/>）。
    /// </summary>
    [ObservableProperty] private bool _isDraft;

    /// <summary>扫描是否**含子目录**（默认开；与视频列表页一致）。</summary>
    [ObservableProperty] private bool _recursive = true;

    /// <summary>
    /// 工具条的路径输入框（可**手输 / 粘贴**，也可由「📁 扫描文件夹」的选择框回填）。
    /// 为什么要有它：NAS / 网络路径、以及反复重扫同一个目录时，弹选择框很难用。
    /// </summary>
    [ObservableProperty] private string _importPath = "";

    /// <summary>「➕ 登记文件夹」攒下来的待扫目录（登记**不**扫描，等「🔄 扫描全部」一起扫）。</summary>
    public ObservableCollection<string> PendingFolders { get; } = new();

    public bool HasPendingFolders => PendingFolders.Count > 0;

    // ==================== ① 预览扫描（📁 / 📄）：只进内存，⛔ 不写库 ====================

    /// <summary>按工具条输入框里的路径预览扫描（手输入口；回车 / 点「⤓」都走这里）。</summary>
    [RelayCommand]
    private async Task PreviewPathAsync()
    {
        var dir = (ImportPath ?? "").Trim().Trim('"');
        if (dir.Length == 0) { StatusText = "先填一个文件夹路径。"; return; }
        if (!Directory.Exists(dir)) { StatusText = $"目录不存在：{dir}"; return; }
        await PreviewFolderAsync(dir).ConfigureAwait(false);
    }

    /// <summary>预览扫一个文件夹 → 进草稿态。</summary>
    public async Task PreviewFolderAsync(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || IsBusy) return;
        ImportPath = dir;
        try
        {
            IsBusy = true;
            StatusText = $"正在预览扫描「{dir}」（只读文件信息，不写库）…";
            var progress = PostProgress();
            var rows = await _scan.PreviewAsync(dir, Recursive, progress, CancellationToken.None).ConfigureAwait(false);
            LoadDraft(rows);
            StatusText = $"预览到 {Items.Count} 个：点「💾 加入库」保存，或「🧹 清除列表」换文件夹重扫。";
        }
        catch (Exception ex) { StatusText = "预览失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>预览选中的若干文件（不遍历目录）→ 进草稿态。</summary>
    public Task PreviewFilesAsync(IReadOnlyList<string> files)
    {
        if (files == null || files.Count == 0) return Task.CompletedTask;

        var rows = new List<AssetRow>();
        foreach (var f in files)
        {
            try
            {
                if (!File.Exists(f)) continue;
                var info = new FileInfo(f);
                var name = Path.GetFileNameWithoutExtension(info.Name);
                var dir = Path.GetDirectoryName(f) ?? "";
                rows.Add(new AssetRow
                {
                    Name = name,
                    Title = name,
                    FileName = info.Name,
                    FilePath = f,
                    Type = ScanService.GuessAssetType(info.Extension),
                    Size = info.Length,
                    Mtime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    StorageName = Path.GetFileName(dir.TrimEnd('\\', '/'))
                });
            }
            catch { /* 单个文件读不了 → 跳过 */ }
        }
        LoadDraft(rows);
        StatusText = $"预览到 {Items.Count} 个文件：点「💾 加入库」保存，或「🧹 清除列表」放弃。";
        return Task.CompletedTask;
    }

    /// <summary>把一批预览行装进列表并进入草稿态。</summary>
    private void LoadDraft(List<AssetRow> rows)
    {
        _all.Clear();
        AddRows(rows);
        Selected = null;
        IsDraft = true;
        ApplyView();
    }

    // ==================== ② 加入库 / 清除列表（草稿态专用） ====================

    [RelayCommand]
    private void CommitDraft()
    {
        if (!IsDraft || _all.Count == 0) { StatusText = "没有待入库的预览结果。"; return; }

        var n = _assets.CommitRows(_all);
        IsDraft = false;
        Reload();
        StatusText = n == 0 ? "入库失败（数据库不可用？）。" : $"已把 {n} 个加入资产库。";
    }

    [RelayCommand]
    private void ClearList()
    {
        // 🔴 任何状态都可用（v3.29 用户反馈：库视图下置灰看着像坏了）。两种语义：
        if (IsDraft)
        {
            // 草稿态 = 丢弃预览结果，回库视图
            _all.Clear();
            Selected = null;
            IsDraft = false;
            Reload();
            StatusText = "已清除预览结果，可以换个文件夹重新扫描。";
            return;
        }

        // 库视图 = 只清**当前显示**（数据仍在库里，「🔄 重读资产库」即找回）—— 对齐视频列表的同名按钮（§6.4.11）
        Items.Clear();
        Selected = null;
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowSampleRow));
        StatusText = "已清空列表显示（数据仍在库里，点「🔄 重读资产库」找回）。";
    }

    /// <summary>库文件全路径（右键「🗄 打开库文件」的 ToolTip 用）。</summary>
    public string DatabasePath => _assets.DatabasePath;

    /// <summary>
    /// 用**系统默认程序**打开选中的文件（视频 = 播放、书 = 阅读、图 = 看图）。
    /// 🔴 这类"针对某一条"的动作为什么不在工具条：工具条只放**页面级**功能（扫描 / 筛选 / 入库…），
    /// 单条操作归右键菜单（用户 v3.29 定调，需求 §4.8 硬规则 9）。
    /// </summary>
    [RelayCommand]
    private void OpenFile()
    {
        if (Selected == null) return;
        Launch(Selected.FilePath, false, "文件");
    }

    /// <summary>在资源管理器里**选中**该文件（右键「📁 打开所在文件夹」）。</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        if (Selected == null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                Arguments = $"/select,\"{Selected.FilePath}\""
            });
            StatusText = "已在资源管理器中定位该文件。";
        }
        catch (Exception ex) { StatusText = "打开文件夹失败：" + ex.Message; }
    }

    /// <summary>用系统默认程序打开一个路径（<paramref name="isDb"/> 只影响提示文案）。</summary>
    private void Launch(string path, bool isDb, string what)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { StatusText = $"{what}不存在：{path}"; return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            StatusText = isDb
                ? "已用系统默认程序打开库文件（没反应多半是没装能开 .db 的工具）。"
                : $"已用系统默认程序打开{what}。";
        }
        catch (Exception ex)
        {
            StatusText = $"打开{what}失败：" + ex.Message + (isDb ? " —— 需要先装一个能打开 .db 的工具（如 DB Browser for SQLite）。" : "");
        }
    }

    /// <summary>
    /// 用**系统默认程序**打开 <c>newtoys.db</c>（装了 DB Browser for SQLite 之类的工具就能直接看表）。
    /// ⛔ 打开的是只读副本视角：别的程序占用时 SQLite 的 WAL 模式仍允许我们继续写，但**别在工具里改数据**。
    /// </summary>
    [RelayCommand]
    private void OpenDbFile() => Launch(_assets.DatabasePath, true, "库文件");

    // ==================== ③ 登记文件夹 + 扫描全部（直接入库） ====================

    /// <summary>登记一个文件夹到「待扫列表」（⛔ 不扫描、⛔ 不入库）。</summary>
    public void RegisterFolder(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        if (!Directory.Exists(dir)) { StatusText = $"目录不存在：{dir}"; return; }

        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) name = dir;

        var storage = new StorageSource { Name = name, RootPath = dir, Type = StorageTypes.Local };
        _assets.UpsertStorage(storage);
        if (!PendingFolders.Contains(dir)) PendingFolders.Add(dir);

        OnPropertyChanged(nameof(HasPendingFolders));
        StatusText = $"已登记「{name}」（共 {PendingFolders.Count} 个待扫）——继续添加，然后点「🔄 扫描全部」。";
    }

    /// <summary>把已登记的存储源**全部**扫一遍并直接入库（`ScanAsync(null)` 扫所有在线存储源）。</summary>
    [RelayCommand]
    private async Task ScanAllAsync()
    {
        PendingFolders.Clear();
        OnPropertyChanged(nameof(HasPendingFolders));
        await RunJobAsync("正在扫描全部存储源……", (ct, progress) => _scan.ScanAsync(null, progress, ct)).ConfigureAwait(false);
    }

    // ==================== ④ 通用：进度上报 / 任务外壳 ====================

    /// <summary>
    /// 造一个进度报告器：把后台线程的进度文本 **Post 回 UI 线程**再写 <c>StatusText</c>
    /// （⛔ 直接在后台线程写界面状态会出线程问题；本项目之前没有从后台推进度的先例）。
    /// </summary>
    private IProgress<string> PostProgress()
        => new Progress<string>(text =>
        {
            try { Dispatcher.UIThread.Post(() => StatusText = text); }
            catch { /* 还没有 UI 线程（设计期 / 已关闭）→ 忽略 */ }
        });


    private async Task RunJobAsync(string startText,
        Func<CancellationToken, IProgress<string>, Task<ScanJob>> work)
    {
        if (IsBusy) { StatusText = "还有任务在跑，等它结束再试。"; return; }

        try
        {
            IsBusy = true;
            StatusText = startText;

            // ⚠️ 进度回调来自后台线程（ScanService 里的 Task.Run）→ 必须 Post 回 UI 线程再碰界面状态
            var progress = new Progress<string>(text =>
            {
                try { Dispatcher.UIThread.Post(() => StatusText = text); }
                catch { /* 设计期 / 还没有 UI 线程：忽略，不影响任务本身 */ }
            });

            var job = await work(CancellationToken.None, progress).ConfigureAwait(false);

            Reload();
            StatusText = job.Status == ScanStatuses.Cancelled
                ? "已取消。"
                : $"完成：新增 {job.AddedFiles} / 更新 {job.UpdatedFiles} / 丢失 {job.MissingFiles} / 失败 {job.ErrorCount}";
        }
        catch (Exception ex)
        {
            StatusText = "失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ==================== v3.34 / NT-018：勾选列 + 批量逻辑操作 ====================

    /// <summary>行被勾了几条（工具条「🏷 加标签」「🗂 加集合」按它操作）。</summary>
    public int CheckedCount => _all.Count(r => r.IsChecked);

    public bool HasChecked => CheckedCount > 0;

    public string CheckedText => CheckedCount == 0 ? "未勾选" : $"已勾选 {CheckedCount} 个";

    /// <summary>
    /// 表头 ☑ 的三态（🔴 用**原生三态 CheckBox**，⛔ 别用字符"☑"画 —— 字体会渲染成深色块，v3.16 连踩两轮）：
    /// <c>true</c> = 全选、<c>false</c> = 全不选、<c>null</c> = 部分选中。
    /// </summary>
    [ObservableProperty] private bool? _allCheckedState = false;

    /// <summary>表头 ☑ 点一下：当前不是"全选"就全选，否则全不选。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var target = AllCheckedState != true;
        foreach (var r in _all) r.IsChecked = target;
        RefreshChecked();
        StatusText = target ? $"已全选 {_all.Count} 条。" : "已取消全选。";
    }

    /// <summary>表头右键：取消选择。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        foreach (var r in _all) r.IsChecked = false;
        RefreshChecked();
        StatusText = "已取消选择。";
    }

    /// <summary>表头右键：反选。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var r in _all) r.IsChecked = !r.IsChecked;
        RefreshChecked();
        StatusText = $"已反选，当前勾选 {CheckedCount} 条。";
    }

    /// <summary>重算勾选计数与表头三态（行的 IsChecked 一变就会被调）。</summary>
    public void RefreshChecked()
    {
        var total = _all.Count;
        var n = _all.Count(r => r.IsChecked);
        AllCheckedState = total == 0 ? false : n == 0 ? false : n == total ? true : null;

        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(CheckedText));
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetRow.IsChecked)) RefreshChecked();
    }

    /// <summary>把一批行装进 <c>_all</c> 并挂上勾选监听（⛔ 别直接 `_all.Add`，否则计数不更新）。</summary>
    private void AddRows(IEnumerable<AssetRow> rows)
    {
        foreach (var r in rows)
        {
            r.PropertyChanged += OnRowPropertyChanged;
            _all.Add(r);
        }
    }

    /// <summary>工具条「🏷 加标签」：给**勾选的行**打一个标签（输入标签名，不存在就建）。</summary>
    [RelayCommand]
    private async Task AddTagToCheckedAsync()
    {
        var ids = _all.Where(r => r.IsChecked).Select(r => r.Id).ToList();
        if (ids.Count == 0) { StatusText = "先在左边勾选要打标签的资产。"; return; }

        var name = await DialogHooks.InputAsync($"给勾选的 {ids.Count} 个资产打什么标签？", "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var tagId = _tagSvc.Ensure(name.Trim());
        if (tagId <= 0) { StatusText = "打标签失败（库不可用？）。"; return; }

        var n = _tagSvc.AddTagToAssets(ids, tagId);
        _opLog.Log("tag", "tag", tagId, afterJson: $"{{\"assets\":{ids.Count},\"added\":{n},\"name\":\"{name.Trim()}\"}}");
        Reload();
        StatusText = $"已给 {n} 个资产打上标签「{name.Trim()}」（原本已有该标签的不重复）。";
    }

    /// <summary>工具条「🗂 加集合」：把**勾选的行**加进一个集合（输入集合名，不存在就建一个手动集合）。</summary>
    [RelayCommand]
    private async Task AddToCollectionCheckedAsync()
    {
        var ids = _all.Where(r => r.IsChecked).Select(r => r.Id).ToList();
        if (ids.Count == 0) { StatusText = "先在左边勾选要加入集合的资产。"; return; }

        var name = await DialogHooks.InputAsync($"把勾选的 {ids.Count} 个资产加进哪个集合？（没有就新建）", "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var hit = _colSvc.List().FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        var colId = hit?.Id ?? _colSvc.Create(name.Trim(), CollectionTypes.Manual);
        if (colId <= 0) { StatusText = "加入集合失败（库不可用？）。"; return; }
        if (hit != null && hit.IsSmart) { StatusText = $"「{hit.Name}」是智能集合，成员由规则决定，⛔ 不能手工加。"; return; }

        var n = _colSvc.AddAssets(colId, ids);
        _opLog.Log("collection", "collection", colId, afterJson: $"{{\"assets\":{ids.Count},\"added\":{n},\"name\":\"{name.Trim()}\"}}");
        Reload();
        StatusText = hit == null
            ? $"已新建手动集合「{name.Trim()}」并加入 {n} 个资产。"
            : $"已把 {n} 个资产加进集合「{name.Trim()}」。";
    }

    // ==================== v3.34 / NT-018：详情卡编辑（显示名 / 评分 / 描述 / 标签 / 分类 / 集合） ====================

    /// <summary>详情卡的标签色片（点一下即选；「💾 保存」统一落库）。</summary>
    public ObservableCollection<PickItem> TagPicks { get; } = new();

    /// <summary>详情卡的分类色片。</summary>
    public ObservableCollection<PickItem> CategoryPicks { get; } = new();

    /// <summary>详情卡的集合色片。</summary>
    public ObservableCollection<PickItem> CollectionPicks { get; } = new();

    /// <summary>评分（0~5）可选项。</summary>
    public int[] RatingChoices { get; } = { 0, 1, 2, 3, 4, 5 };

    [ObservableProperty] private int _editRating;
    [ObservableProperty] private string _editDescription = "";

    /// <summary>该资产的关联（只读列出；增删归后续——本轮只在详情卡展示）。</summary>
    public ObservableCollection<Relation> Relations { get; } = new();

    public bool HasRelations => Relations.Count > 0;

    /// <summary>三种色片有没有内容（详情卡给"还没有…"提示用）。</summary>
    public bool HasTagPicks => TagPicks.Count > 0;
    public bool HasCategoryPicks => CategoryPicks.Count > 0;
    public bool HasCollectionPicks => CollectionPicks.Count > 0;

    /// <summary>点一下色片 = 打上 / 取消（⛔ 不立即落库，等「💾 保存」）。</summary>
    [RelayCommand]
    private void ToggleTag(PickItem? item) { if (item != null) item.IsSelected = !item.IsSelected; }

    [RelayCommand]
    private void ToggleCategory(PickItem? item) { if (item != null) item.IsSelected = !item.IsSelected; }

    [RelayCommand]
    private void ToggleCollection(PickItem? item) { if (item != null) item.IsSelected = !item.IsSelected; }

    private void BuildPicks(AssetRow target)
    {
        TagPicks.Clear();
        CategoryPicks.Clear();
        CollectionPicks.Clear();
        Relations.Clear();

        if (target.Id <= 0) return;   // 示例行：不给色片（卡片整体禁用）

        var tagIds = _tagSvc.ListTagIdsOfAsset(target.Id);
        foreach (var t in _tagSvc.List())
            TagPicks.Add(new PickItem(t.Id, t.Name, t.ChipBrush, tagIds.Contains(t.Id)));

        var catIds = _catSvc.ListCategoryIdsOfAsset(target.Id);
        foreach (var c in _catSvc.List())
            CategoryPicks.Add(new PickItem(c.Id, c.Name, new SolidColorBrush(Color.Parse(AssetPalette.For(c.Name))), catIds.Contains(c.Id)));

        var colIds = _colSvc.ListCollectionIdsOfAsset(target.Id);
        foreach (var c in _colSvc.List())
            CollectionPicks.Add(new PickItem(c.Id, c.Name, new SolidColorBrush(Color.Parse(AssetPalette.For(c.Name))), colIds.Contains(c.Id)));

        foreach (var rel in _relationSvc.ListFor(target.Id)) Relations.Add(rel);
        OnPropertyChanged(nameof(HasRelations));
        OnPropertyChanged(nameof(HasTagPicks));
        OnPropertyChanged(nameof(HasCategoryPicks));
        OnPropertyChanged(nameof(HasCollectionPicks));
    }

    /// <summary>
    /// 详情卡「💾 保存」：一次把**显示名 / 评分 / 描述 / 标签 / 分类 / 集合**全部落库，并写一条 `operation_logs`。
    /// ⛔ 不动磁盘文件（物理重命名是 NT-019 的「↗ 应用到文件」）。
    /// </summary>
    [RelayCommand]
    private void SaveAssetDetail()
    {
        if (_loadingDetail || Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }

        var row = Selected;
        var before = $"{{\"name\":\"{row.Name}\",\"rating\":{row.Rating}}}";

        var name = (DetailDisplayName ?? "").Trim();
        _assets.SetDisplayName(row.Id, name.Length == 0 ? null : name);
        row.DisplayName = name.Length == 0 ? null : name;
        row.Name = name.Length == 0 ? (row.Title ?? $"资产 #{row.Id}") : name;

        _assets.SetRatingAndDescription(row.Id, EditRating, string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription);
        row.Rating = EditRating;
        row.Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription;

        var tagIds = TagPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        var catIds = CategoryPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        var colIds = CollectionPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        _tagSvc.SetAssetTags(row.Id, tagIds);
        _catSvc.SetAssetCategories(row.Id, catIds);
        _colSvc.SetAssetCollections(row.Id, colIds);

        _opLog.Log("display_name", "asset", row.Id, beforeJson: before,
            afterJson: $"{{\"name\":\"{row.Name}\",\"rating\":{EditRating},\"tags\":{tagIds.Count},\"categories\":{catIds.Count},\"collections\":{colIds.Count}}}");

        ApplyView();
        StatusText = $"已保存「{row.Name}」的显示名 / 评分 / 描述 / 标签 / 分类 / 集合（⛔ 未动磁盘文件）。";
    }

    /// <summary>
    /// 详情卡「↗ 应用到文件」（NT-019）：⚠️ 与「💾 保存」完全不同 ——
    /// 「保存」只写**库里的显示名**（⛔ 不动磁盘）；这个才是**把名字真写到磁盘文件上**。
    /// <para>物理操作 → 走任务队列 + 日志先行；同名冲突按「追加序号」处理（⛔ 不覆盖，覆盖会丢文件）。</para>
    /// </summary>
    [RelayCommand]
    private async Task ApplyToFileAsync()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }

        var row = Selected;
        var loc = _assets.FindPrimaryLocation(row.Id) ?? _assets.ListLocations(row.Id).FirstOrDefault();
        if (loc == null) { StatusText = "这个资产没有登记文件位置，没法应用到文件。"; return; }
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }

        var current = Path.GetFileName(loc.Path);
        var suggested = row.DisplayName ?? row.Title ?? Path.GetFileNameWithoutExtension(current);
        var newName = await DialogHooks.InputAsync(
            $"把磁盘文件改成什么名字？\n当前：{current}\n（这是**真改磁盘文件名**；只改库里的显示名请用「💾 保存」）",
            suggested).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(newName)) { StatusText = "已取消（没改磁盘文件）。"; return; }

        var name = newName.Trim();
        _fileOps.Queue.Enqueue($"重命名 {current}", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                p.Report($"正在把「{current}」改成「{name}」…");
                var r = _fileOps.Rename(loc, name, ConflictPolicy.AppendNumber);
                p.Report(r.Message);
            }, ct).ConfigureAwait(false);

            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
        });

        StatusText = $"已把「{current}」的重命名排进任务队列（进度看「任务中心」）。";
    }

    /// <summary>详情卡「🗑 移入回收站」= **软删除**（只改 `status`，⛔ 不删行、⛔ 不动磁盘文件）。</summary>
    [RelayCommand]
    private async Task MoveToTrashAsync()
    {
        if (Selected == null) { StatusText = "先选中一个资产。"; return; }

        var row = Selected;
        if (!await DialogHooks.ConfirmAsync($"把「{row.Name}」移入回收站？（⛔ 只改库里的状态，**不动磁盘文件**；回收站里能恢复）").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        _assets.SetAssetStatus(row.Id, AssetStatuses.Trashed);
        _opLog.Log("trash", "asset", row.Id, beforeJson: "{\"status\":\"active\"}", afterJson: "{\"status\":\"trashed\"}");
        Reload();
        StatusText = $"已把「{row.Name}」移入回收站（磁盘文件没动）。";
    }

    // ==================== 跨页取件（NT-018：标签 / 分类页点资产 → 跳到这里并选中） ====================

    /// <summary>
    /// 取件点（由 View 在 <c>OnAttachedToVisualTree</c> 调 —— 🔴 **不是** `OnDataContextChanged`，规范 §4.8 硬规则 8）：
    /// 别的页面请求"打开某个资产"时，在这里选中它。
    /// </summary>
    public void ConsumePendingAsset()
    {
        var req = _navStore?.Take();
        if (req == null) return;

        var hit = Items.FirstOrDefault(r => r.Id == req.Value.AssetId);
        if (hit == null)
        {
            StatusText = "目标资产不在当前列表（可能已移入回收站）。";
            return;
        }

        Selected = hit;
        if (!IsDetailVisible)
        {
            IsDetailVisible = true;
            SaveDetailFlag();
            RaiseDetailChanged();
        }
        StatusText = $"已定位到资产「{hit.Name}」。";
    }

    // ==================== 详情面板（由 Shell 的 ContentControl 承载） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>详情内容：**同一个** <c>AssetDetailView</c> 实例（懒建），DataContext 指向本 VM。</summary>
    public object? DetailContent
    {
        get
        {
            if (_detailView == null)
            {
                _detailView = new AssetDetailView();
                _detailView.DataContext = this;
            }
            return _detailView;
        }
    }

    public event EventHandler? DetailChanged;

    /// <summary>
    /// 工具条末位的详情开关调这个（⛔ 别绑基类的 <c>ToggleDetailCommand</c> —— 它只翻
    /// <c>IsDetailVisible</c>，**不会**通知 Shell，右列就不会出现）。
    /// </summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible) EnsureDetailSelection();
        SaveDetailFlag();
        RaiseDetailChanged();
    }

    /// <summary>接口实现：展开详情（Shell 切回本标签、恢复上次开合状态时调）。</summary>
    public void OpenDetail()
    {
        EnsureDetailSelection();
        IsDetailVisible = true;
        SaveDetailFlag();
        RaiseDetailChanged();
    }

    /// <summary>接口实现：开合切换（与 <see cref="ToggleDetailPanel"/> 同一份逻辑）。</summary>
    public void ToggleDetail() => ToggleDetailPanel();

    private void RaiseDetailChanged() => DetailChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 选中项 → 详情 ====================

    partial void OnSelectedChanged(AssetRow? value)
    {
        UpdateDetailSample(value != null);   // v3.32 §4.8.3：没选中项 → 详情卡改用示例行（并整卡禁用）
        OnPropertyChanged(nameof(DetailRow));
        LoadDetail(value);
        OnPropertyChanged(nameof(HasSelection));
        RaiseDetailChanged();
    }

    private void LoadDetail(AssetRow? row)
    {
        _loadingDetail = true;
        try
        {
            Locations.Clear();
            // v3.32 §4.8.3：没选中项 → 用示例行（示例态），否则用真值
            var target = row ?? (HasSampleRow ? SampleRow : null);
            DetailDisplayName = target == null
                ? ""
                : target.DisplayName ?? target.Title ?? target.Name;
            EditRating = target?.Rating ?? 0;
            EditDescription = target?.Description ?? "";

            if (target != null && target.Id > 0) BuildPicks(target);

            if (ReferenceEquals(target, SampleRow))
                Locations.Add(new FileLocation { Path = SampleRow.FilePath, Status = "（示例位置）" });
            else if (target != null)
                foreach (var l in _assets.ListLocations(target.Id)) Locations.Add(l);
        }
        finally { _loadingDetail = false; }
    }

    /// <summary>
    /// 保存显示名（详情卡改名框**失焦**时由 View 调）：只写 <c>display_name</c>，
    /// ⛔ **绝不动磁盘文件名**（物理改名是显式操作，归 NT-019）。
    /// </summary>
    [RelayCommand]
    private void SaveDisplayName()
    {
        if (_loadingDetail || Selected == null) return;

        var name = (DetailDisplayName ?? "").Trim();
        _assets.SetDisplayName(Selected.Id, name.Length == 0 ? null : name);

        Selected.DisplayName = name.Length == 0 ? null : name;
        Selected.Name = name.Length == 0 ? (Selected.Title ?? $"资产 #{Selected.Id}") : name;

        ApplyView();
        StatusText = name.Length == 0
            ? "已清除显示名（回退到标题）。"
            : $"已把显示名改为「{name}」（⛔ 未动磁盘文件）。";
    }

    // ==================== 筛选条下拉的构建 ====================

    private void BuildStaticOptions()
    {
        TypeOptions.Clear();
        foreach (var (label, value) in AssetTypeTexts.Choices) TypeOptions.Add(new FilterOption(label, value));
        FilterType = TypeOptions[0];

        StatusOptions.Clear();
        StatusOptions.Add(new FilterOption("全部状态", ""));
        StatusOptions.Add(new FilterOption("正常", AssetStatuses.Active));
        StatusOptions.Add(new FilterOption("已丢失", AssetStatuses.Missing));
        StatusOptions.Add(new FilterOption("回收站", AssetStatuses.Trashed));
        StatusOptions.Add(new FilterOption("已归档", AssetStatuses.Archived));
        FilterStatus = StatusOptions[0];
    }

    private void RebuildStorageOptions()
    {
        var keep = FilterStorage?.Value ?? "";
        StorageOptions.Clear();
        StorageOptions.Add(new FilterOption("全部存储源", ""));
        foreach (var s in _assets.ListStorages())
            StorageOptions.Add(new FilterOption(s.Name, s.Id.ToString(CultureInfo.InvariantCulture)));
        FilterStorage = StorageOptions.FirstOrDefault(o => o.Value == keep) ?? StorageOptions[0];
    }

    private void RebuildTagOptions()
    {
        var keep = FilterTag?.Value ?? "";
        TagOptions.Clear();
        TagOptions.Add(new FilterOption("全部标签", ""));
        // NT-018 之前标签表是空的 → 下拉只有「全部标签」，属预期（⛔ 不是 bug）
        foreach (var (id, name) in _assets.ListTagOptions())
            TagOptions.Add(new FilterOption(name, id.ToString(CultureInfo.InvariantCulture)));
        FilterTag = TagOptions.FirstOrDefault(o => o.Value == keep) ?? TagOptions[0];
    }

    partial void OnSearchTextChanged(string value) => ApplyView();
    partial void OnFilterTypeChanged(FilterOption? value) => ApplyView();
    partial void OnFilterStorageChanged(FilterOption? value) => ApplyView();
    partial void OnFilterStatusChanged(FilterOption? value) => ApplyView();
    partial void OnFilterTagChanged(FilterOption? value) => ApplyView();

    // ==================== 过滤 / 排序 ====================

    private void ApplyView()
    {
        IEnumerable<AssetRow> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(r => r.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || r.FilePath.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || r.TagsText.Contains(text, StringComparison.OrdinalIgnoreCase));

        var type = FilterType?.Value ?? "";
        if (type.Length > 0) q = q.Where(r => string.Equals(r.Type, type, StringComparison.Ordinal));

        var status = FilterStatus?.Value ?? "";
        if (status.Length > 0) q = q.Where(r => string.Equals(r.Status, status, StringComparison.Ordinal));

        var storage = FilterStorage?.Value ?? "";
        if (storage.Length > 0)
            q = q.Where(r => r.StorageId.ToString(CultureInfo.InvariantCulture) == storage);

        var tagId = FilterTag?.Value ?? "";
        if (tagId.Length > 0)
        {
            // 标签是"逗号分隔串里含某一个" → 用下拉里显示的**名字**去 Contains（⛔ 别拿 id 比文本）
            var tagName = TagOptions.FirstOrDefault(o => o.Value == tagId)?.Label ?? "";
            if (tagName.Length > 0)
                q = q.Where(r => r.TagsText.Split(',', StringSplitOptions.TrimEntries)
                                           .Contains(tagName, StringComparer.OrdinalIgnoreCase));
        }

        // 表头右键的多项筛选（基类）：多条之间取「且」
        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Type" => Order(q, r => r.Type),
            "Tags" => Order(q, r => r.TagsText),
            "Categories" => Order(q, r => r.CategoriesText),
            "Collections" => Order(q, r => r.CollectionsText),
            "Size" => Order(q, r => r.Size),
            "Modified" => Order(q, r => r.Mtime ?? ""),
            "Storage" => Order(q, r => r.StorageName),
            _ => Order(q, r => r.Name)
        };

        var ordered = q.ToList();
        Items.Clear();
        foreach (var r in ordered) Items.Add(r);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        RefreshChecked();                          // 列表重建 → 勾选计数 / 表头三态跟上
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowSampleRow));   // v3.22：有真实数据后示例行消失
    }

    private IOrderedEnumerable<AssetRow> Order<TKey>(IEnumerable<AssetRow> q, Func<AssetRow, TKey> key)
        => SortDescending ? q.OrderByDescending(key) : q.OrderBy(key);

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
    }
}
