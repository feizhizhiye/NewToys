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
using NewToys.Shared.Details;
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
public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
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
    private readonly AssetMergeService _merge = null!;
    private readonly TaskQueue _queue = null!;
    private readonly IAppConfigService? _app;
    private readonly List<AssetRow> _all = new();

    /// <summary>详情卡正在回填（改名框失焦保存要跳过这一次，⛔ 别把"显示"当成"用户改了"）。</summary>
    private bool _loadingDetail;

    public ObservableCollection<AssetRow> Items { get; } = new();

    /// <summary>当前选中资产的**全部文件位置**（详情卡列出；一个资产可能有多份拷贝）。</summary>
    public ObservableCollection<FileLocation> Locations { get; } = new();

    // ===== NT-030：移动 / 复制目标存储源（防穿越由 FileOperationService 保证） =====
    public ObservableCollection<StorageSource> TargetStorages { get; } = new();
    [ObservableProperty] private long _selectedTargetStorageId;

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

    // NT-031：视频专属列表头（带排序箭头）
    [ObservableProperty] private string _durationHeader = "时长";
    [ObservableProperty] private string _resolutionHeader = "分辨率";
    [ObservableProperty] private string _codecHeader = "编码";

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

    // NT-031：视频专属列可见性（默认隐藏，仅视频页开启）
    public bool IsDurationVisible => Vis("Duration");
    public bool IsResolutionVisible => Vis("Resolution");
    public bool IsCodecVisible => Vis("Codec");

    protected override string PageKey => "assets-all";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // 🔴 v3.34 / NT-018：首列 = **勾选列**（Pick）——
        //    加了首列后，表头 / 行模板里**所有 Grid.Column 索引都要 +1**（⛔ 别漏一处）。
        new("Pick", "", "34", sortable: false),
        // Sample 要给"典型内容宽度"（v3.22）：示例宽 ≤ 表头宽的列会被锁成表头宽、不可拖
        new("Name", "名称", "2.6*", sample: "The.Matrix.1999.1080p.mkv"),
        new("Type", "类型", "0.8*", sample: "视频"),
        new("Tags", "标签", "1.4*", sample: "科幻, 收藏"),
        new("Categories", "分类", "1.2*", sample: "电影 / 动作"),
        new("Collections", "集合", "1.2*", sample: "周末片单"),
        new("Size", "大小", "0.9*", sample: "4.2 GB"),
        new("Modified", "修改时间", "1.3*", sample: "2026-10-13 21:04"),
        new("Storage", "存储源", "1.2*", sample: "移动硬盘 D"),

        // 🔴 NT-031：视频专属列（试点）—— 默认隐藏，仅视频页（assets-video）经构造显隐逻辑开启。
        //    键名 ≠ 属性名（Duration→DurationText），已在 FieldText / SortColumn 显式映射。
        new("Duration", "时长", "0.9*", sample: "02:14:33"),
        new("Resolution", "分辨率", "0.9*", sample: "1920×1080"),
        new("Codec", "编码", "0.9*", sample: "h264")
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
            "Duration" => a.DurationText,
            "Resolution" => a.ResolutionText,
            "Codec" => a.CodecText,
            _ => base.FieldText(row, columnKey)
        };
    }

    /// <summary>数值比较（「大于 / 小于」）：只有大小列有数值语义（字节数）。</summary>
    protected override double? FieldNumber(object row, string columnKey)
        => row is AssetRow a && columnKey == "Size" ? a.Size : base.FieldNumber(row, columnKey);

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>详情卡的数据源：有选中项时显示它（v3.60 §4.8.1：⛔ 没选中不填示例值）。</summary>
    public AssetRow DetailRow => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
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

    // ==================== NT-026：多库融合（并库 / 另存 / 勾选删除） ====================

    /// <summary>
    /// 「🗄 合并库文件」：预览 → 确认摘要 → 合并进主库（走任务队列，进度在「任务中心」看）。
    /// 🔴 源库**只读**：ATTACH 后只 SELECT，⛔ 一个字节都不改源文件。
    /// </summary>
    [RelayCommand]
    private async Task MergeLibraryAsync(string? dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath)) { StatusText = "未选择库文件。"; return; }
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }

        var preview = _merge.Preview(dbPath);
        if (!preview.Ok) { StatusText = preview.Message; return; }

        var missing = preview.MissingTables.Count > 0
            ? $"\n⚠️ 源库缺 {preview.MissingTables.Count} 张表（{string.Join("、", preview.MissingTables)}）——按老库兼容处理，缺的跳过。"
            : "";
        if (!await DialogHooks.ConfirmAsync(
                $"把库文件合并进当前资产库？\n{dbPath}\n\n该库规模：{preview.SummaryText}{missing}\n\n" +
                "按路径 / 内容去重（重复并入第二次基本全跳过）；源库不会被修改。").ConfigureAwait(false))
        {
            StatusText = "已取消合并。";
            return;
        }

        _queue.Enqueue($"合并库 {Path.GetFileName(dbPath)}", async (ct, p) =>
        {
            var result = await Task.Run(() => _merge.Merge(dbPath, p, ct), ct).ConfigureAwait(false);
            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
            p.Report(result.Ok ? result.Message : "失败：" + result.Message);
        });
        StatusText = "已把合并任务排进队列——正在打开「任务中心」看进度。";
        _navStore.NavigateTo("assets-tasks");
    }

    /// <summary>
    /// 「💿 另存库文件」：把当前主库**另存为**一个新 .db（VACUUM INTO 在线备份，一致快照）。
    /// 目标已存在 → 二次确认后覆盖（⛔ 不静默覆盖）。
    /// </summary>
    [RelayCommand]
    private async Task SaveLibraryAsAsync(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) { StatusText = "未指定保存位置。"; return; }
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }

        if (File.Exists(targetPath))
        {
            if (!await DialogHooks.ConfirmAsync($"「{targetPath}」已存在——覆盖它？（⛔ 不可恢复）").ConfigureAwait(false))
            {
                StatusText = "已取消保存（换个文件名即可）。";
                return;
            }
            try { File.Delete(targetPath); }
            catch (Exception ex) { StatusText = "无法覆盖旧文件：" + ex.Message; return; }
        }

        var (ok, msg) = _merge.SaveAs(targetPath);
        StatusText = msg;
    }

    /// <summary>
    /// 「🗑 移入回收站（选中 N）」：NT-026 的"减"——勾选行批量软删除（进回收站可恢复；⛔ 不动磁盘）。
    /// </summary>
    [RelayCommand]
    private async Task TrashCheckedAsync()
    {
        var rows = _all.Where(r => r.IsChecked).ToList();
        if (rows.Count == 0) { StatusText = "先勾选要移除的资产（行首勾选框，或表头 ☑ 全选）。"; return; }

        if (!await DialogHooks.ConfirmAsync(
                $"把勾选的 {rows.Count} 个资产移入回收站？\n" +
                "⛔ 只改库里的状态，**不动磁盘文件**；回收站里能恢复，也能永久删除。").ConfigureAwait(false))
        {
            StatusText = "已取消。";
            return;
        }

        foreach (var r in rows)
        {
            _assets.SetAssetStatus(r.Id, AssetStatuses.Trashed);
            _opLog.Log("trash", "asset", r.Id, beforeJson: "{\"status\":\"active\"}", afterJson: "{\"status\":\"trashed\"}");
        }
        Reload();
        StatusText = $"已把 {rows.Count} 个资产移入回收站（磁盘文件没动）。";
    }

    /// <summary>资产库是否可用（不可用时页面照常显示 + 状态栏提示，⛔ 不崩）。</summary>
    public bool IsDbAvailable => _assets?.IsAvailable ?? false;

    public AssetAllViewModel() { }

    public AssetAllViewModel(AssetService assets, ScanService scan, IColumnLayoutService cols,
        IAppConfigService? app = null,
        TagService? tagSvc = null, CategoryService? catSvc = null, CollectionService? colSvc = null,
        RelationService? relationSvc = null, OperationLogService? opLog = null, AssetNavigationStore? navStore = null,
        FileOperationService? fileOps = null, AssetImportExport? impExp = null, AssetMergeService? merge = null,
        TaskQueue? queue = null)
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
        _merge = merge!;
        _queue = queue!;

        // NT-031：视频专属列默认显隐 —— 视频页（assets-video）默认可见（不进字典 = 默认可见），
        // 其余页默认隐藏（显式置 false）。LoadLayout 之后读回用户已保存的偏好会覆盖这里的默认。
        if (PageKey != "assets-video")
        {
            SetColumnVisible("Duration", false);
            SetColumnVisible("Resolution", false);
            SetColumnVisible("Codec", false);
        }

        LoadLayout();                       // 列显隐 + 详情栏开合（基类）
        BuildStaticOptions();               // 类型 / 状态（固定值）
        Reload();                           // 存储源 / 标签下拉要读库 → 放在读库之后
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
        RebuildTargetStorages();
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
        StatusText = "已清空列表显示（数据仍在库里，点「🔄 重读」找回）。";
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

    /// <summary>行被勾了几条（工具条「🏷 加标签」「📚 加集合」按它操作）。</summary>
    public int CheckedCount => _all.Count(r => r.IsChecked);

    public bool HasChecked => CheckedCount > 0;

    // v3.41（用户定调）：⛔ 工具条不再放「未勾选 / 已勾选 N 个」指示文字 ——
    // 「需先勾选」这件事改用按钮自己的 ToolTip（+ ToolTip.ShowOnDisabled）表达，勾选数由列表左列勾选框直接可见。

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

    /// <summary>工具条「📚 加集合」：把**勾选的行**加进一个集合（输入集合名，不存在就建一个手动集合）。</summary>
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

    // ===== NT-058：评分改成「一排 1~5 小按钮」（点一下即生效并落盘，与图标组同一口径） =====

    /// <summary>评分可点按钮（1~5；<see cref="PickItem.Id"/> 就是分值）。</summary>
    public ObservableCollection<PickItem> RatingPicks { get; } = new();

    /// <summary>评分按钮的分值（与 <see cref="RatingPicks"/> 一一对应）。</summary>
    private static readonly int[] RatingValues = { 1, 2, 3, 4, 5 };

    /// <summary>把 <see cref="RatingPicks"/> 的选中态同步成当前评分（点选 / 点赞 / 点踩 / 换选中项后都要调）。</summary>
    private void SyncRatingPicks(int rating)
    {
        for (var i = 0; i < RatingPicks.Count; i++) RatingPicks[i].IsSelected = RatingValues[i] == rating;
    }

    [ObservableProperty] private int _editRating;

    // ===== NT-058：状态改**单选下拉**（预设三档；索引 ↔ 状态 key 的映射只在这里） =====

    /// <summary>状态下拉的显示文案（顺序 = <see cref="StatusKeys"/>）。</summary>
    public string[] StatusChoices { get; } = { "正常", "已归档", "文件缺失" };

    /// <summary>状态下拉的实际值（存库；<see cref="AssetStatuses"/>）。</summary>
    private static readonly string[] StatusKeys =
        { AssetStatuses.Active, AssetStatuses.Archived, AssetStatuses.Missing };

    [ObservableProperty] private int _editStatusIndex;
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

    // ===== NT-058：详情卡**图标动作组**（收藏 / 评分 ±1 / 保存 / 刷新 / 删除） =====
    // 🔴 与「💾 保存」的分工：收藏 / 评分是**单项即时生效即落盘**（点一下就写库，⛔ 不用再点保存）；
    //    显示名 / 描述 / 标签 / 分类 / 集合仍是**编辑态 + 显式保存**（多字段表单）。

    /// <summary>「★ 收藏」：切换收藏（即时落盘）。</summary>
    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }
        var next = !Selected.IsFavorite;
        if (!_assets.SetFavorite(Selected.Id, next)) { StatusText = "写库失败（资产库不可用）。"; return; }
        Selected.IsFavorite = next;
        OnPropertyChanged(nameof(DetailRow));   // 图标 .active 态跟着变（渲染器订阅的是页面 VM）
        StatusText = next ? $"已收藏「{Selected.Name}」。★" : $"已取消收藏「{Selected.Name}」。";
    }

    /// <summary>「👍 点赞」：评分 +1（0~5 封顶，即时落盘）。</summary>
    [RelayCommand]
    private void RateUp() => ChangeRating(1);

    /// <summary>「👎 点踩」：评分 -1（0~5 封底，即时落盘）。</summary>
    [RelayCommand]
    private void RateDown() => ChangeRating(-1);

    /// <summary>「1~5 评分按钮」：直接设成这个值（即时落盘）。</summary>
    [RelayCommand]
    private void SetRating(PickItem? item)
    {
        if (item == null) return;
        ChangeRating(0, (int)item.Id);
    }

    private void ChangeRating(int delta, int? absolute = null)
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }
        var next = Math.Clamp(absolute ?? Selected.Rating + delta, 0, 5);
        if (next == Selected.Rating)
        {
            StatusText = absolute.HasValue
                ? $"当前就是 {next} 分。"
                : delta > 0 ? "已经是最高分 5 了。" : "已经是最低分 0 了。";
            return;
        }
        // ⚠️ 描述一起写是这个方法的要求 ⇒ 传**库里的现值**（不是编辑态 EditDescription），
        //    否则会把用户还没保存的草稿覆盖掉 / 或把已保存的描述清掉。
        if (!_assets.SetRatingAndDescription(Selected.Id, next, Selected.Description))
        { StatusText = "写库失败（资产库不可用）。"; return; }
        Selected.Rating = next;
        EditRating = next;                      // 详情卡编辑态同步
        SyncRatingPicks(next);                  // 1~5 按钮的选中态同步
        OnPropertyChanged(nameof(DetailRow));
        StatusText = $"评分 → {next}（已写入资产库）。";
    }

    private void BuildPicks(AssetRow target)
    {
        TagPicks.Clear();
        CategoryPicks.Clear();
        CollectionPicks.Clear();
        Relations.Clear();

        // 评分按钮（1~5）：**固定五项**，选中态 = 当前评分（⛔ 不用色块 → Brush 给透明）
        RatingPicks.Clear();
        for (var i = 0; i < RatingValues.Length; i++)
            RatingPicks.Add(new PickItem(RatingValues[i], $"{RatingValues[i]}", Brushes.Transparent, target.Rating == RatingValues[i]));

        if (target.Id <= 0) return;   // 未入库（Id<=0）：不给色片

        var tagIds = _tagSvc.ListTagIdsOfAsset(target.Id);
        // NT-058：标签**按命名空间分组**显示 ⇒ 先按 ns 排序（分组靠"键连续"，乱序会拆出很多组）
        foreach (var t in _tagSvc.List().OrderBy(t => t.Namespace ?? "").ThenBy(t => t.Name, StringComparer.CurrentCulture))
            TagPicks.Add(new PickItem(t.Id, t.Name, t.ChipBrush, tagIds.Contains(t.Id), t.Namespace));

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

        // 状态（单选下拉 → 存库的 key）
        var status = StatusKeys[Math.Clamp(EditStatusIndex, 0, StatusKeys.Length - 1)];
        if (!string.Equals(row.Status, status, StringComparison.Ordinal))
        {
            _assets.SetAssetStatus(row.Id, status);
            row.Status = status;
        }

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

    /// <summary>
    /// 列表右键「⛔ 从磁盘删除」（NT-058）：**本程序唯一**直接删磁盘文件的入口。
    /// <para>
    /// 🔴 三重防误触（这是索引库里**破坏性最大**的操作，故意把点击成本做高）：
    /// ① 不进详情卡 / 不上工具条，**只在行右键菜单里**；② 二次确认（文案写明"不进回收站、不可还原"）；
    /// ③ 真删走**任务队列**（任务中心可见），⛔ 不在 UI 线程上直接删。
    /// </para>
    /// <para>只想从库里移除（文件留着）→ 用详情卡图标组的「🗑 移入回收站」。</para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteFromDiskAsync()
    {
        if (Selected == null) { StatusText = "先选中一个资产。"; return; }

        var row = Selected;
        var loc = PrimaryLocationOf(row);
        if (loc == null) { StatusText = "这个资产没有登记文件位置，没法从磁盘删除。"; return; }

        var name = Path.GetFileName(loc.Path);
        if (!await DialogHooks.ConfirmAsync(
                $"⛔ 从磁盘**永久删除**这个文件？\n\n{name}\n\n· 不进系统回收站、无法还原；\n· 库里这条文件索引也会一起删掉（还有其它位置的不会删）；\n· 只想从库里移除、文件留着 ⇒ 用详情卡的「🗑 移入回收站」。").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        _fileOps.Queue.Enqueue($"从磁盘删除 {name}", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                p.Report($"正在永久删除「{name}」…");
                var r = _fileOps.PermanentDelete(loc);
                p.Report(r.Message);
                if (!r.Ok) return;
                _assets.DeleteLocation(loc.Id);
                // 这条资产的**所有**位置都删光了 ⇒ 资产本体也没意义了（连带挂接一起清）
                if (_assets.ListLocations(row.Id).Count == 0) _assets.DeleteAsset(row.Id);
            }, ct).ConfigureAwait(false);
            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
        });
        StatusText = $"已把「{name}」的永久删除排进任务队列（进度看「任务中心」）。";
    }

    // ==================== NT-030：移动 / 复制（目标 = 已登记存储源根；防穿越由 FileOperationService 保证） ====================

    private FileLocation? PrimaryLocationOf(AssetRow row)
        => _assets.FindPrimaryLocation(row.Id) ?? _assets.ListLocations(row.Id).FirstOrDefault();

    [RelayCommand]
    private async Task MoveToTargetAsync()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }
        var loc = PrimaryLocationOf(Selected);
        if (loc == null) { StatusText = "这个资产没有登记文件位置，没法移动 / 复制。"; return; }
        var target = TargetStorages.FirstOrDefault(s => s.Id == SelectedTargetStorageId);
        if (target == null) { StatusText = "先选一个目标存储源。"; return; }
        if (!target.Online) { StatusText = $"目标存储源「{target.Name}」当前离线。"; return; }
        if (!await DialogHooks.ConfirmAsync(
                $"把「{Path.GetFileName(loc.Path)}」移动到「{target.Name}」？\n⛔ 只移到已登记存储源根内（不越界写盘）；同名按「追加序号」处理。").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        _fileOps.Queue.Enqueue($"移动到 {target.Name}", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                p.Report($"正在移动到「{target.Name}」…");
                p.Report(_fileOps.Move(loc, target.RootPath, ConflictPolicy.AppendNumber).Message);
            }, ct).ConfigureAwait(false);
            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
        });
        StatusText = $"已把移动排进任务队列（进度看「任务中心」）。";
    }

    [RelayCommand]
    private async Task CopyToTargetAsync()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }
        var loc = PrimaryLocationOf(Selected);
        if (loc == null) { StatusText = "这个资产没有登记文件位置，没法复制。"; return; }
        var target = TargetStorages.FirstOrDefault(s => s.Id == SelectedTargetStorageId);
        if (target == null) { StatusText = "先选一个目标存储源。"; return; }
        if (!target.Online) { StatusText = $"目标存储源「{target.Name}」当前离线。"; return; }
        if (!await DialogHooks.ConfirmAsync(
                $"把「{Path.GetFileName(loc.Path)}」复制到「{target.Name}」？\n⛔ 只复制到已登记存储源根内；同名按「追加序号」处理。").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        _fileOps.Queue.Enqueue($"复制到 {target.Name}", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                p.Report($"正在复制到「{target.Name}」…");
                p.Report(_fileOps.Copy(loc, target.RootPath, ConflictPolicy.AppendNumber).Message);
            }, ct).ConfigureAwait(false);
            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
        });
        StatusText = $"已把复制排进任务队列（进度看「任务中心」）。";
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

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图（NT-054 迁移：原来的 <c>AssetDetailView</c> 已停用）。</summary>
    public object? DetailContent => null;

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（NT-054 / 需求 §4.5.1，**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的 <c>AssetDetailView</c> 1:1 对应；三组 chips 带**色块**（`ChipBrushPath`）、
    /// 目标是「编辑 + 保存」这套卡内动作。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一个资产，这里显示它的详情。",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    // ===== 顶部：文件名（磁盘上的真名，只读；要改库里的显示名用卡底部的输入框） =====
                    new DetailField
                    {
                        Label = "文件名",
                        ValuePath = "DetailRow.FileName",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true
                    },
                    new DetailField { Label = "类型", ValuePath = "DetailRow.TypeText", Kind = FieldKind.Text, Selectable = true },
                    // 状态：**单选下拉**（预设三档；改完点「💾 保存」落库）
                    new DetailField
                    {
                        Label = "状态",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(StatusChoices),
                        IndexPath = nameof(EditStatusIndex)
                    },
                    new DetailField { Label = "大小", ValuePath = "DetailRow.SizeText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "修改时间", ValuePath = "DetailRow.ModifiedText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "存储源", ValuePath = "DetailRow.StorageName", Kind = FieldKind.Text, Selectable = true },
                    new DetailField
                    {
                        Label = "文件路径",
                        ValuePath = "DetailRow.FilePath",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    // ⛔ 已删：原「重命名 / 移动 / 复制 / 删除磁盘文件在 NT-019 落地」这句提示 —— 那些操作早已落地，
                    //    提示留着只是噪声（NT-058 样板：固定项区只留**事实**，不放实现进度）。
                    // ===== 三组 chips（标签 / 分类 / 集合）：点一下打上 / 取消，每项带一个颜色块 =====
                    new DetailField
                    {
                        Label = "标签（点一下打上 / 取消；按命名空间分组）",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(TagPicks),
                        ChipTextPath = "Name",
                        ChipBrushPath = "Brush",
                        ChipSelectedPath = "IsSelected",
                        ChipGroupPath = "Group",
                        ChipCommandPath = nameof(ToggleTagCommand)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（还没有标签：到「标签管理」页新建，或用工具条的「🏷 加标签」）",
                        VisibleWhen = "!" + nameof(HasTagPicks)
                    },
                    new DetailField
                    {
                        Label = "分类（点一下加 / 去掉）",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(CategoryPicks),
                        ChipTextPath = "Name",
                        ChipBrushPath = "Brush",
                        ChipSelectedPath = "IsSelected",
                        ChipCommandPath = nameof(ToggleCategoryCommand)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（还没有分类：到「分类管理」页新建）",
                        VisibleWhen = "!" + nameof(HasCategoryPicks)
                    },
                    new DetailField
                    {
                        Label = "集合（点一下加 / 去掉；⚡ 智能集合不参与手工设置）",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(CollectionPicks),
                        ChipTextPath = "Name",
                        ChipBrushPath = "Brush",
                        ChipSelectedPath = "IsSelected",
                        ChipCommandPath = nameof(ToggleCollectionCommand)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（还没有集合：到「集合管理」页新建）",
                        VisibleWhen = "!" + nameof(HasCollectionPicks)
                    },
                    // ===== NT-058 样板顺序：**固定只读项 → 标签 / 分类 / 集合 → 动作按钮组 → 评分 → 描述** =====
                    // ===== 图标动作组（高频动作收成一行，替掉原来一排撑满宽度的文字按钮） =====
                    new DetailField
                    {
                        Kind = FieldKind.IconBar,
                        IconActions =
                        {
                            new DetailIconAction
                            {
                                IconKey = "IconStar",
                                ToolTip = "收藏 / 取消收藏（即时写库）",
                                CommandPath = nameof(ToggleFavoriteCommand),
                                IsActivePath = "DetailRow.IsFavorite",
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconThumbUp",
                                ToolTip = "点赞：评分 +1（0~5，即时写库）",
                                CommandPath = nameof(RateUpCommand),
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconThumbDown",
                                ToolTip = "点踩：评分 -1（0~5，即时写库）",
                                CommandPath = nameof(RateDownCommand),
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction { IsSeparator = true },
                            new DetailIconAction
                            {
                                IconKey = "IconSave",
                                ToolTip = "保存：把显示名 / 描述 / 状态 / 标签 / 分类 / 集合写进资产库（⛔ 不动磁盘文件）",
                                CommandPath = nameof(SaveAssetDetailCommand),
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconRefresh",
                                ToolTip = "刷新：重读资产库（列表 + 详情）",
                                CommandPath = nameof(RefreshCommand),
                                IsEnabledPath = "!" + nameof(IsBusy)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconTrash",
                                ToolTip = "移入回收站：软删除（只改库里状态，⛔ 不动磁盘文件；回收站页可恢复）",
                                CommandPath = nameof(MoveToTrashCommand),
                                IsEnabledPath = nameof(HasSelection),
                                ConfirmText = "把这条资产移入回收站？\n（只改库里的状态，磁盘文件不动；可在「回收站」页恢复）",
                                IsDanger = true
                            }
                        }
                    },
                    // ===== 评分：一排 1~5 小按钮（点一下**即时落盘**；要 0 分用按钮组的「👎 点踩」） =====
                    new DetailField
                    {
                        Label = "评分",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(RatingPicks),
                        ChipTextPath = "Name",
                        ChipSelectedPath = "IsSelected",
                        ChipCommandPath = nameof(SetRatingCommand)
                    },
                    new DetailField
                    {
                        Label = "描述",
                        ValuePath = nameof(EditDescription),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        MultiLine = true,
                        Rows = 52,
                        Placeholder = "随手记点什么（可留空）"
                    },
                    // ⛔ 原「移动到 / 复制到（目标存储源）」整块已删（NT-058）：
                    //    这两个是**低频的重操作**，详情卡样板只留「查看 + 改元数据」，不再摆两个物理量操作入口。
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "关联（字幕 / 封面 / 版本…）",
                        Kind = FieldKind.List,
                        OptionsPath = nameof(Relations),
                        ItemLabelPath = "TypeText",
                        ItemTextPath = "TargetName"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（暂无关联；关联的增删在本轮之后落地）",
                        VisibleWhen = "!" + nameof(HasRelations)
                    },
                    // ⛔ 原「文件位置」List 已删：它与上面的「文件路径」是同一份信息（磁盘路径），
                    //    只读展示一次就够（NT-058 详情卡样板：去重 + 直接读取的信息一律只读）。
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "⚠ 这个文件在磁盘上找不到了（最近一次扫描没见到它）。",
                        VisibleWhen = "DetailRow.IsMissing"
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "操作日志（谁在什么时候改了什么）随 NT-018 / NT-019 一起落地，届时会在这里给出入口。"
                    },
                    // ===== 卡底部：改名输入框（⛔ 只改库里的显示名；真改磁盘文件名走下面的「应用到文件」） =====
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "显示名（只改库里的名字，⛔ 不动磁盘文件；改完点「💾 保存」）",
                        ValuePath = nameof(DetailDisplayName),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Placeholder = "留空 = 用扫描时得到的标题"
                    }
                }
            }
        },
        Actions =
        {
            // ⛔ 「💾 保存」「🗑 移入回收站」已上提到**顶部图标动作组**（NT-058），这里不再重复。
            new DetailAction
            {
                Text = "↗ 应用到文件",
                Classes = "ghost",
                ToolTip = "把名字**真写到磁盘文件上**（物理重命名：走任务队列 + 日志先行；同名冲突按「追加序号」，⛔ 不覆盖）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = ApplyToFileCommand
            },
            // ⛔ 「📁 移动到此源」「📋 复制到此源」已删（NT-058）：物理搬运归任务中心 / 后续入口，
            //    详情卡样板只保留「↗ 应用到文件」这一个**写磁盘**的显式动作。
        }
    };

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
            var target = row;
            DetailDisplayName = target == null
                ? ""
                : target.DisplayName ?? target.Title ?? target.Name;
            EditRating = target?.Rating ?? 0;
            EditDescription = target?.Description ?? "";
            // 状态下拉：索引 ↔ 状态 key（取不到就落到第 0 项「正常」）
            var si = target == null ? -1 : Array.IndexOf(StatusKeys, target.Status);
            EditStatusIndex = si < 0 ? 0 : si;

            if (target != null && target.Id > 0) BuildPicks(target);

            if (target != null)
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

    private void RebuildTargetStorages()
    {
        TargetStorages.Clear();
        if (!IsDbAvailable) { SelectedTargetStorageId = 0; return; }
        foreach (var s in _assets.ListStorages()) TargetStorages.Add(s);
        SelectedTargetStorageId = TargetStorages.FirstOrDefault(s => s.Online)?.Id
                                  ?? TargetStorages.FirstOrDefault()?.Id ?? 0;
    }

    partial void OnSearchTextChanged(string value) => ApplyView();
    partial void OnFilterTypeChanged(FilterOption? value) => ApplyView();
    partial void OnFilterStorageChanged(FilterOption? value) => ApplyView();
    partial void OnFilterStatusChanged(FilterOption? value) => ApplyView();
    partial void OnFilterTagChanged(FilterOption? value) => ApplyView();

    // ==================== 工具条「⋯ 更多」开合（v3.41 / 用户定调） ====================

    // ==================== 声明式工具条 + 选择器命令（NT-055 / 需求 §4.8.5） ====================
    // 🔴 v3.53：本页工具条改由 **Shell 的 ToolbarHost 统一渲染**（折叠 / 「⋯」/ 详情开关浮层都在那边实现），
    //    页内 `Border.toolbar` 与原来的 `ApplyToolbarLayout()` / `IsToolbarExpanded` 一并删除。
    //    顺序 = 优先级顺序（越靠前越不会被折进「⋯」）。
    // ⚠️ 5 个类型薄壳页（assets-video/music/book/image/document）**继承本类** ⇒ 一次覆盖 6 个页面。

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Kind = ToolbarItemKind.SplitButton, Text = "📁 扫描", CommandPath = nameof(ScanFolderCommand),
                IsEnabledPath = "!" + nameof(IsBusy), ToolTip = "扫目录 / 扫文件到列表（⛔ 只进内存、不入库）",
                Items = new ToolbarMenuItemDescriptor[]
                {
                    new() { Text = "📁 文件夹…", CommandPath = nameof(ScanFolderCommand), ToolTip = "扫一个目录到列表（⛔ 只进内存、不入库）" },
                    new() { Text = "📄 文件…", CommandPath = nameof(ScanFilesCommand), ToolTip = "只扫选中的几个文件（⛔ 只进内存、不入库）" },
                } },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "路径（回车预览）", ValuePath = nameof(ImportPath),
                EnterCommandPath = nameof(PreviewPathCommand), ToolTip = "填文件夹路径，回车即预览扫描（也可用左侧「📁 扫描」选目录）" },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索…", ValuePath = nameof(SearchText), ToolTip = "按名称 / 路径 / 标签搜索" },
        new() { Text = "➕ 登记", CommandPath = nameof(RegisterFolderCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "加入待扫文件夹列表（⛔ 不扫描），攒几个后点「🔄 扫描全部」" },
        new() { Text = "🔄 扫描全部", CommandPath = nameof(ScanAllCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "把所有已登记的存储源扫一遍，直接入库" },
        new() { Text = "🏷 加标签", CommandPath = nameof(AddTagToCheckedCommand), IsEnabledPath = nameof(HasChecked), ShowToolTipOnDisabled = true,
                ToolTip = "先在列表里勾选行，再点这里打标签（输入标签名，不存在就新建）" },
        new() { Text = "📚 加集合", CommandPath = nameof(AddToCollectionCheckedCommand), IsEnabledPath = nameof(HasChecked), ShowToolTipOnDisabled = true,
                ToolTip = "先在列表里勾选行，再点这里加进集合（输入集合名，不存在就新建一个手动集合）" },
        new() { Text = "🗑 移入回收站", CommandPath = nameof(TrashCheckedCommand), IsEnabledPath = nameof(HasChecked), ShowToolTipOnDisabled = true,
                ToolTip = "先在列表里勾选行，再点这里移入回收站（只改库状态，磁盘文件不动；可恢复）" },
        new() { Text = "💾 加入库", CommandPath = nameof(CommitDraftCommand), IsEnabledPath = nameof(IsDraft),
                ToolTip = "把当前预览结果写入资产库（⛔ 不动磁盘文件）" },
        new() { Text = "🧹 清除列表", CommandPath = nameof(ClearListCommand),
                ToolTip = "草稿态：丢弃预览结果；库视图：清空当前显示（数据仍在库里，刷新即找回）" },
        new() { Kind = ToolbarItemKind.Toggle, Text = "含子目录", ValuePath = nameof(Recursive), ToolTip = "关掉就只扫当前这一层" },
        new() { Text = "🔄 重读", CommandPath = nameof(RefreshCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "重新读取资产库（⛔ 不扫描磁盘）" },
        new() { Kind = ToolbarItemKind.SplitButton, Text = "🗄 并入…", CommandPath = nameof(MergeDbCommand), IsEnabledPath = nameof(IsDbAvailable),
                ToolTip = "把另一个 newtoys.db 合并进当前库（按路径/内容去重；源库只读；进度看任务中心）",
                Items = new ToolbarMenuItemDescriptor[]
                {
                    new() { Text = "🔎 打开当前库文件", CommandPath = nameof(OpenDbFileCommand), ToolTipPath = nameof(DatabasePath) },
                } },
        new() { Text = "💿 另存为…", CommandPath = nameof(PickSaveLibraryAsCommand), IsEnabledPath = nameof(IsDbAvailable),
                ToolTip = "把当前资产库另存成一个 .db（备份 / 交付用）" },
        new() { Text = "📤 导出", CommandPath = nameof(ExportAssetsCommand), ToolTip = "把当前列表导成 json（含标签 / 分类 / 集合的名字）" },
        new() { Text = "📥 导入", CommandPath = nameof(ImportAssetsCommand), ToolTip = "选一个导出的 json 追加合并进库（按路径去重，⛔ 不静默覆盖）" },
        new() { Text = "🧠 存为智能集合", IsEnabled = false, ToolTip = "智能集合需要规则求值（未实装）" },
    };

    /// <summary>「📁 扫描」主按钮 / 「📁 文件夹…」：选目录 → **预览**（⛔ 只进内存、不入库）。</summary>
    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择要扫描的目录", ImportExportDefaultDir);
        if (string.IsNullOrWhiteSpace(dir)) return;
        await PreviewFolderAsync(dir).ConfigureAwait(false);
    }

    /// <summary>「📄 文件…」：只预览选中的几个文件（⛔ 同样不入库）。</summary>
    [RelayCommand]
    private async Task ScanFilesAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要扫描的文件", ImportExportDefaultDir, true, "*");
        if (files.Count == 0) return;
        await PreviewFilesAsync(files).ConfigureAwait(false);
    }

    /// <summary>「➕ 登记」：选一个目录加进待扫列表（⛔ 不扫描，等「🔄 扫描全部」）。</summary>
    [RelayCommand]
    private async Task RegisterFolderAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择要登记的目录", ImportExportDefaultDir);
        if (string.IsNullOrWhiteSpace(dir)) return;
        RegisterFolder(dir);
    }

    /// <summary>「🗄 并入…」主按钮：选一个外部 .db → VM 预览 + 确认 + 入队合并。</summary>
    [RelayCommand]
    private async Task MergeDbAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要并入当前库的 newtoys.db", ImportExportDefaultDir, false, "*.db;*.db3;*.sqlite");
        var path = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return;
        MergeLibraryCommand.Execute(path);
    }

    /// <summary>
    /// 「💿 另存为…」：选一个目标 .db → 调既有 <see cref="SaveLibraryAsCommand"/> 另存（已存在会问覆盖）。
    /// <para>⚠️ 方法名带 <c>Pick</c> 前缀是**必须的**：既有的 <c>SaveLibraryAsAsync(string?)</c> 已占用
    /// <c>SaveLibraryAsCommand</c> 这个名字，同名再加一个 [RelayCommand] 方法会报 **MVVMTK0023**（同类型内命令名必须唯一）。</para>
    /// </summary>
    [RelayCommand]
    private async Task PickSaveLibraryAsAsync()
    {
        var path = await DialogHooks.SaveFileAsync("保存资产库为", ImportExportDefaultDir, "newtoys.db", "*.db");
        if (string.IsNullOrWhiteSpace(path)) return;
        SaveLibraryAsCommand.Execute(path);
    }

    /// <summary>「📤 导出」：把当前列表导成 json（含标签 / 分类 / 集合的名字）。</summary>
    [RelayCommand]
    private async Task ExportAssetsAsync()
    {
        var path = await DialogHooks.SaveFileAsync("导出资产列表", ImportExportDefaultDir, "media-assets.json", "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            File.WriteAllText(path, ExportAssetsJson());
            StatusText = $"已导出当前列表到：{path}";
        }
        catch (Exception ex) { StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>「📥 导入」：选一个导出的 json 追加合并进库（按路径去重，⛔ 不静默覆盖）。</summary>
    [RelayCommand]
    private async Task ImportAssetsAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要导入的资产 json", ImportExportDefaultDir, false, "*.json");
        var src = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(src)) return;
        try
        {
            var result = ImportAssetsJson(File.ReadAllText(src));
            StatusText = result.Message;
        }
        catch (Exception ex) { StatusText = "导入失败：" + ex.Message; }
    }

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
            "Duration" => Order(q, r => r.DurationSeconds),
            "Resolution" => Order(q, r => r.ResolutionText),
            "Codec" => Order(q, r => r.CodecText),
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
        DurationHeader = "时长" + SortArrow("Duration");
        ResolutionHeader = "分辨率" + SortArrow("Resolution");
        CodecHeader = "编码" + SortArrow("Codec");
    }
}
