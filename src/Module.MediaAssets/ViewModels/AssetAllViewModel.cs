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

// Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「全部资产」页—— 本模块的**样板列表页**：标签 / 集合 / 存储源 / 任务 / 回收站页都照这个
/// 模式复刻（列显隐 / 排序 / 筛选由基类承载，详情面板由 **Shell** 承载）。
/// <para>
/// 🔴 v3.69：5 个类型页（视频 / 音乐 / 书籍 / 图片 / 文档）已**合并进本页** ——
/// 由筛选栏的**类型控件**驱动列表数据 / 列集合 / 详情卡字段集 / 状态栏前缀（需求 §6.6「类型切换」）。
/// </para>
/// <para>
/// 三个要点：
/// <list type="number">
///   <item>本页是**索引视图**：显示名这类逻辑改动直接改库；**重命名 / 移动 / 删除磁盘文件**由文件操作服务负责，本页一个都不做。</item>
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
    private readonly MetadataService _meta = null!;

    /// <summary>应用级配置（只读：取 <c>StoreDirectory</c> 当导入/导出默认目录）。</summary>
    private readonly IAppConfigService? _app;

    /// <summary>v3.69：列布局服务（只用来记「用户显式设过」的列键；显隐 / 落盘本身在基类）。</summary>
    private IColumnLayoutService? _columnLayout;

    /// <summary>v3.69：用户显式设过（已落盘）的列键 —— 类型驱动的默认显隐**不覆盖**它们（二者不互相覆盖）。</summary>
    private readonly HashSet<string> _userColumnKeys = new();

    private readonly List<AssetRow> _all = new();

    /// <summary>详情卡正在回填（改名框失焦保存要跳过这一次，⛔ 别把"显示"当成"用户改了"）。</summary>
    private bool _loadingDetail;

    public ObservableCollection<AssetRow> Items { get; } = new();

    /// <summary>当前选中资产的**全部文件位置**（详情卡列出；一个资产可能有多份拷贝）。</summary>
    public ObservableCollection<FileLocation> Locations { get; } = new();

    // ===== 移动 / 复制目标存储源（防穿越由 FileOperationService 保证） =====
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

    // 视频专属列表头（带排序箭头）
    [ObservableProperty] private string _durationHeader = "时长";
    [ObservableProperty] private string _resolutionHeader = "分辨率";
    [ObservableProperty] private string _codecHeader = "编码";

    // ==================== 列显隐（XAML 只能绑具名属性；状态在基类，落 config\columnlayout.json） ====================

    public bool IsNameVisible => Vis("Name");
    public bool IsTypeVisible => Vis("Type");
    public bool IsTagsVisible => Vis("Tags");
    public bool IsCategoriesVisible => Vis("Categories");
    public bool IsCollectionsVisible => Vis("Collections");
    public bool IsSizeVisible => Vis("Size");
    public bool IsModifiedVisible => Vis("Modified");
    public bool IsStorageVisible => Vis("Storage");

    // 视频专属列可见性（默认隐藏，仅视频页开启）
    public bool IsDurationVisible => Vis("Duration");
    public bool IsResolutionVisible => Vis("Resolution");
    public bool IsCodecVisible => Vis("Codec");

    protected override string PageKey => "assets-all";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // Sample 要给"典型内容宽度"（v3.22）：示例宽 ≤ 表头宽的列会被锁成表头宽、不可拖
        new("Name", "名称", "2.6*", sample: "The.Matrix.1999.1080p.mkv"),
        new("Type", "类型", "0.8*", sample: "视频"),
        new("Tags", "标签", "1.4*", sample: "科幻, 收藏"),
        new("Categories", "分类", "1.2*", sample: "电影 / 动作"),
        new("Collections", "集合", "1.2*", sample: "周末片单"),
        new("Size", "大小", "0.9*", sample: "4.2 GB"),
        new("Modified", "修改时间", "1.3*", sample: "2026-10-13 21:04"),
        new("Storage", "存储源", "1.2*", sample: "移动硬盘 D"),

        // 🔴 视频专属列（试点）—— 默认隐藏；v3.69 起由**筛选类型**驱动
        //    （选「视频」时可见，⛔ 不落盘；用户右键改过的偏好优先，见 ApplyTypeColumnDefaults）。
        //    键名 ≠ 属性名（Duration→DurationText），已在 FieldText / SortColumn 显式映射。
        new("Duration", "时长", "0.9*", sample: "02:14:33"),
        new("Resolution", "分辨率", "0.9*", sample: "1920×1080"),
        new("Codec", "编码", "0.9*", sample: "h264")
    };

    /// <summary>基类的翻页 / 排序 / 筛选变化都转到这里 —— 库态按新状态重取一页，草稿态内存重算。</summary>
    protected override void RefreshView()
    {
        if (IsDraft) { ApplyViewMemory(); return; }
        LoadPageFromDb();
    }

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
    public AssetRow? DetailRow => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
    /// <summary>导入 / 导出默认目录：本模块的 <c>store\assets</c>（目录尚未建出 ⇒ 退回 <c>store\</c>）。</summary>
    public string ImportExportDefaultDir => ModuleStoreDir(_app?.StoreDirectory);

    /// <summary>把 <c>store\</c> 收窄到本模块子目录；子目录不存在时退回传入的根（对话框起始位置必须有效）。</summary>
    private static string ModuleStoreDir(string? storeDirectory)
    {
        if (string.IsNullOrEmpty(storeDirectory)) return "";
        var dir = Path.Combine(storeDirectory, AssetsDb.Key);
        return Directory.Exists(dir) ? dir : storeDirectory;
    }

    // ==================== 列表 JSON 导入 / 导出 ====================

    /// <summary>
    /// 📤 导出**当前列表**（<c>_all</c>，即当前模式的全量、⛔ 不含筛选后的可见集）为 JSON 文本。
    /// 组织信息（标签 / 分类 / 集合）按**名字**导出（⛔ 不存内部 id）——换机 / 合并都能用。
    /// <para>⚠️ 库态 <c>_all</c> = **当前页**（分页批量口径统一为当前页）⇒ 导出即当前页。</para>
    /// </summary>
    public string ExportAssetsJson()
    {
        var scope = IsDraft ? "draft-preview" : "library";
        return _impExp.Export(_all, scope);
    }

    // ==================== 多库融合（并库 / 另存 / 勾选删除） ====================

    /// <summary>
    /// 「🗄 合并库文件」：预览 → 确认摘要 → 合并进主库（走任务队列，进度在「任务中心」看）。
    /// 🔴 源库**只读**：ATTACH 后只 SELECT，⛔ 一个字节都不改源文件。
    /// </summary>
    [RelayCommand]
    private async Task MergeLibraryAsync(string? dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath)) { StatusText = "未选择库文件。"; return; }
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }

        try
        {
            var preview = _merge.Preview(dbPath);
            if (!preview.Ok) { StatusText = preview.Message; return; }

            // 🔴 2026-10-16：「导入之前的 NewToys.db 崩溃」—— 旧版 NewToys 的应用库里**没有**媒体资产表
            //    （MediaAssets 是后加的模块）。这种文件不是资产库 ⇒ 直接拒绝，⛔ 不进确认 / 合并。
            if (!preview.HasAssetTables)
            {
                StatusText = $"「{Path.GetFileName(dbPath)}」不是媒体资产库（找不到 assets 等表）——" +
                             "导入只支持媒体资产库的 .db；json 列表请选「📥 导入」。";
                return;
            }

            var missing = preview.MissingTables.Count > 0
                ? $"\n⚠️ 源库缺 {preview.MissingTables.Count} 张表（{string.Join("、", preview.MissingTables)}）——按老库兼容处理，缺的跳过。"
                : "";
            // ⛔ 确认后不带 ConfigureAwait(false)：后面的 Enqueue / 导航 / StatusText 都必须在 UI 线程上执行
            //    （在后台线程导航会当场把任务中心页的控件建成"别的线程的对象"，随后一刷新就崩）
            if (!await DialogHooks.ConfirmAsync(
                    $"把库文件合并进当前资产库？\n{dbPath}\n\n该库规模：{preview.SummaryText}{missing}\n\n" +
                    "按路径 / 内容去重（重复并入第二次基本全跳过）；源库不会被修改。"))
            {
                StatusText = "已取消合并。";
                return;
            }

            _queue.Enqueue($"合并库 {Path.GetFileName(dbPath)}", async (ct, p) =>
            {
                var result = await Task.Run(() => _merge.Merge(dbPath, p, ct), ct).ConfigureAwait(false);
                // 🔴 合并后的刷新跑在 UI 线程：刷新本身抛异常会变成未处理异常直接崩程序 ⇒ 必须兜住
                Dispatcher.UIThread.Post(() =>
                {
                    try { Reload(); }
                    catch (Exception ex) { StatusText = "合并后刷新列表失败：" + ex.Message; }
                });
                p.Report(result.Ok ? result.Message : "失败：" + result.Message);
            });
            StatusText = "已把合并任务排进队列——正在打开「任务中心」看进度。";
            _navStore.NavigateTo("assets-tasks");
        }
        catch (Exception ex)
        {
            // 兜底：合并链上任何未预期异常（确认框 / 导航 / 入队）都不许把程序带崩
            StatusText = "合并失败：" + ex.Message;
        }
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
            if (!await DialogHooks.ConfirmAsync($"「{targetPath}」已存在——覆盖它？（⛔ 不可恢复）").ConfigureAwait(true))
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
    /// 「🗑 移入回收站」：选中行批量软删除（进回收站可恢复；⛔ 不动磁盘）。
    /// </summary>
    [RelayCommand]
    private async Task TrashCheckedAsync()
    {
        var rows = BatchRows();
        if (rows.Count == 0) { StatusText = "先选中要移除的资产（Ctrl / Shift 可多选，Ctrl+A 全选当前页）。"; return; }

        if (!await DialogHooks.ConfirmAsync(
                $"把选中的 {rows.Count} 个资产移入回收站？\n" +
                "⛔ 只改库里的状态，**不动磁盘文件**；回收站里能恢复，也能永久删除。").ConfigureAwait(true))
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
        TaskQueue? queue = null, IOptionsStore? opts = null, MetadataService? metadata = null)
        : base(cols, opts)
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
        _meta = metadata!;

        _columnLayout = cols;

        LoadLayout();                       // 列显隐 + 详情栏开合（基类）
        CaptureUserColumnPrefs();           // v3.69：记下用户显式设过的列 ⇒ 类型默认不覆盖它们
        BuildStaticOptions();               // 类型 / 状态（固定值；设 FilterType ⇒ 触发类型列默认 + 状态栏前缀）
        Reload();                           // 存储源 / 标签下拉要读库 → 放在读库之后
        LoadDetail(null);

        _booted = true;                     // ctor 期间的筛选默认值不触发重取（Reload 已统一加载）
        StatusText = StatusSummary();
    }

    /// <summary>构造期卫哨 —— BuildStaticOptions 设的筛选默认值⛔不各触发一次全页重取。</summary>
    private bool _booted;

    // ==================== v3.69：类型切换（取代原「类型页薄壳」）====================

    /// <summary>
    /// 状态栏前缀**随筛选类型切换**（v3.69，需求 §6.6「类型切换」）：视频：/ 音乐：/ 书籍：/ 图片：/ 文档：；
    /// 「全部」= 空串。由本属性按 <see cref="FilterType"/> 推导（类型页合并后不再有子类覆写）。
    /// </summary>
    private string StatusPrefix => FilterType?.Value switch
    {
        AssetTypes.Video => "视频：",
        AssetTypes.Music => "音乐：",
        AssetTypes.Book => "书籍：",
        AssetTypes.Image => "图片：",
        AssetTypes.Document => "文档：",
        _ => ""
    };

    /// <summary>当前筛选类型 = 视频（v3.69）：驱动视频专属列与详情卡字段的显隐。</summary>
    public bool IsVideoType => string.Equals(FilterType?.Value, AssetTypes.Video, StringComparison.Ordinal);

    /// <summary>
    /// 类型驱动的列默认显隐 —— 视频 = 「时长 / 分辨率 / 编码」三列可见，其余类型隐藏。
    /// <para>🔴 ⛔ **不落盘**（<c>SetColumnVisible</c> 只改内存）；用户右键改过的偏好（落盘的那些键）
    /// **不被覆盖** —— 需求 §6.6「二者不互相覆盖」。</para>
    /// </summary>
    private void ApplyTypeColumnDefaults()
    {
        ApplyTypeColumnDefault("Duration", IsVideoType);
        ApplyTypeColumnDefault("Resolution", IsVideoType);
        ApplyTypeColumnDefault("Codec", IsVideoType);
    }

    private void ApplyTypeColumnDefault(string key, bool visible)
    {
        if (_userColumnKeys.Contains(key)) return;   // 用户显式设过 ⇒ 偏好优先
        SetColumnVisible(key, visible);
    }

    /// <summary>v3.69：把「用户显式设过（落盘）的列键」记下来（类型驱动的默认不覆盖它们）。</summary>
    private void CaptureUserColumnPrefs()
    {
        _userColumnKeys.Clear();
        if (_columnLayout == null) return;
        foreach (var k in _columnLayout.LoadPage(PageKey).Keys) _userColumnKeys.Add(k);
    }

    /// <summary>一次读库的最大行数（v3.34）：分页后本页不再整表读，
    /// 该上限仅剩同模块其它页（回收站 / 分类 / 集合）在用。</summary>
    public const int QueryLimit = 2000;

    /// <summary>本页为分页样板页 —— 库模式走 SQL 侧筛/排/分页（⛔ 不再整表入内存）。</summary>
    protected override bool PagingEnabled => true;

    /// <summary>
    /// 草稿态同样分页 —— 2026-10-16 用户定：**扫描预览也要能翻页 + 显示底栏跳转链**。
    /// ⛔ 因此不再 override <c>IsPagingSuspended</c>（基类默认 false = 不挂起）：
    /// 草稿态在 <see cref="ApplyViewMemory"/> 里用 LINQ 的 Skip/Take 做**内存切片**（比 SQL 更简单）。
    /// </summary>

    /// <summary>列表概况文案（状态栏用）：类型前缀（v3.69 起随筛选类型切换）+ 总数（COUNT(*)）+ 当前页位置。</summary>
    protected string StatusSummary()
    {
        if (IsDraft)
            return $"{StatusPrefix}预览结果 {TotalCount} 个（⛔ 还没入库，点「💾 加入库」保存）";

        // 查询被 catch 吞掉时总数与列表会不一致（总数有值、列表空）⇒ 必须把原因摆到状态栏
        var err = _assets?.LastQueryError ?? "";
        if (err.Length > 0) return $"⚠ 资产查询失败：{err}（列表因此显示为空）";

        if (!IsDbAvailable)
            return $"{StatusPrefix}资产库不可用（数据库打不开）——本页只读，不会崩。";

        return $"{StatusPrefix}共 {TotalCount} 个资产"
               + (TotalCount == 0 ? "（点「📂 导入」添加存储源或文件）" : "")
               + TableFilterHint()
               + (PagingEnabled && PageCount > 1 ? $"（{PageText}）" : "");
    }

    /// <summary>
    /// 表头右键「🔎 筛选」的条件没有独立入口 ⇒ 拼进状态栏；
    /// 否则它和四个下拉是「且」叠加，用户看到空列表时无从判断是谁在筛。
    /// </summary>
    private string TableFilterHint()
        => ActiveFilterCount == 0 ? "" : $"（表头筛选 {ActiveFilterCount} 条生效）";

    // ==================== 加载 ====================

    [RelayCommand]
    private void Refresh()
    {
        Reload();
        StatusText = IsDbAvailable
            ? $"{StatusPrefix}已重读资产库：共 {TotalCount} 个资产"
            : $"{StatusPrefix}资产库不可用（数据库打不开）。";
    }

    private void Reload()
    {
        // 🔴 后台线程误调会在这里碰 UI 对象（Items.Clear / Selected setter → Shell 工具条）→ 直接抛跨线程异常崩窗口。
        //    统一回投 UI 线程；调用方不必各自记得切线程（对话框 await 之后最容易踩）。
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Reload); return; }

        // 重新从库里读 ⇒ 扫描预览的草稿作废：⛔ 否则并入 / 导入 / 重读之后还停在草稿态，
        // 列表继续显示预览行、状态栏还写「还没入库」。
        IsDraft = false;
        _all.Clear();
        if (!IsDbAvailable) { TotalCount = 0; ApplyView(); return; }
        RebuildStorageOptions();
        RebuildTagOptions();
        RebuildTargetStorages();
        LoadPageFromDb();
    }

    /// <summary>
    /// 按当前筛选 / 排序 / 分页状态从库里取一页。
    /// 🔴 计数与列表传**同一个** <see cref="AssetQuery"/>（WHERE 只在 SQL 侧写一份）⇒ 总数与列表永不错位。
    /// </summary>
    private void LoadPageFromDb()
    {
        _all.Clear();
        var before = PageIndex;
        var q = BuildQuery();
        TotalCount = _assets.CountAssets(q);      // 独立 COUNT(*)（⛔ 不用当前页条数充当总数）
        ClampPageIndex();                          // 删除 / 筛选后总数变少 ⇒ 页码收敛（越界时自动重取）
        if (PageIndex != before) return;           // 已收敛 ⇒ 嵌套的 RefreshView 按新页码重取过了，⛔ 外层别再取一遍
        AddRows(_assets.QueryPage(q with { Offset = (PageIndex - 1) * PageSize, Limit = PageSize }));
        ApplyView();
        // 🔴 查询异常在服务层被吞成空页（⛔ 不让异常穿透），这里必须把它显出来 ——
        //    否则 SQL 一报错表现就是「列表恒空 / 搜不到」，用户只能猜「搜索是不是失效了」。
        if (_assets.LastQueryError.Length > 0) StatusText = "⚠ 查询失败：" + _assets.LastQueryError;
    }

    /// <summary>把页面上的筛选 / 排序状态打成 <see cref="AssetService.AssetQuery"/>（计数与取页共用）。</summary>
    private AssetService.AssetQuery BuildQuery() => new(
        Search: SearchText,
        Type: FilterType?.Value,
        Status: FilterStatus?.Value,
        StorageId: FilterStorage?.Value,
        // 标签下拉存的是 id（「全部标签」= 空串），SQL 侧按**名字**匹配（⛔ 别拿 id 比文本）
        // 🔴 判空必须按 Value：只看 FilterTag 非 null 会把占位项的 Label「全部标签」当标签名传下去
        //    ⇒ EXISTS(tg.name='全部标签') 恒不成立，整页永远 0 行。
        TagName: string.IsNullOrEmpty(FilterTag?.Value) ? null : FilterTag!.Label,
        Clauses: Filters.Where(f => f.IsActive).ToList(),
        SortColumn: SortColumn,
        SortDescending: SortDescending);

    // ==================== 导入：文件夹 / 文件（由 View 弹选择框后调用） ====================

    /// <summary>
    /// 草稿态：列表里装的**不是库里的数据**，而是「预览扫描」的结果（还没入库）。
    /// 用户看完决定：💾 加入库（<see cref="CommitDraftCommand"/>）或 🧹 清除列表（<see cref="ClearListCommand"/>）。
    /// </summary>
    [ObservableProperty] private bool _isDraft;

    /// <summary>
    /// 草稿态⇄库态切换 ⇒ <see cref="ListPageViewModelBase.IsPagingSuspended"/> 变了，
    /// 底栏显隐（HasFooterContent）/ 页码跳组（HasPaging）必须重算 —— ⛔ 没有这一跳，
    /// 「扫描预览（挂起不显示）→ 加入库 / 清除（恢复分页）」后底栏显隐停在旧值。
    /// </summary>
    partial void OnIsDraftChanged(bool value)
    {
        OnPropertyChanged(nameof(HasFooterContent));
        OnPropertyChanged(nameof(HasPaging));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanFilterByStorageOrTag));
        if (!value) return;

        // 进入草稿态：预览行没有库存储源 id、没有标签关联 ⇒ 这两个条件留着只会筛出 0 条
        if (StorageOptions.Count > 0) FilterStorage = StorageOptions[0];
        if (TagOptions.Count > 0) FilterTag = TagOptions[0];
    }

    /// <summary>
    /// 存储源 / 标签两个下拉只对**库里的资产**有意义：草稿行（扫描预览）只有类型 / 状态 / 文件名，
    /// <see cref="AssetRow.StorageId"/> 恒为 0、标签串为空 ⇒ 草稿态一选必空，直接置灰。
    /// </summary>
    public bool CanFilterByStorageOrTag => !IsDraft;

    /// <summary>
    /// 宿主页挂载时强制重算一次底栏显隐 —— 兜住「View 绑定建立早于 VM 数据就绪」
    /// 的时序漏推（绑定求值一次后若无属性变更事件，底栏显隐会停在旧值）。
    /// </summary>
    public void RefreshFooterVisibility()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(HasPaging));
        OnPropertyChanged(nameof(HasFooterContent));
    }

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

}
