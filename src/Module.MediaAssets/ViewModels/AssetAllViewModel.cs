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

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _app?.VideoDbFolder ?? "";

    /// <summary>资产库是否可用（不可用时页面照常显示 + 状态栏提示，⛔ 不崩）。</summary>
    public bool IsDbAvailable => _assets?.IsAvailable ?? false;

    public AssetAllViewModel() { }

    public AssetAllViewModel(AssetService assets, ScanService scan, IColumnLayoutService cols, IAppConfigService? app = null)
        : base(cols)
    {
        _assets = assets;
        _scan = scan;
        _app = app;

        LoadLayout();                       // 列显隐 + 详情栏开合（基类）
        BuildStaticOptions();               // 类型 / 状态（固定值）
        Reload();                           // 存储源 / 标签下拉要读库 → 放在读库之后

        StatusText = IsDbAvailable
            ? $"共 {Items.Count} 个资产" + (Items.Count == 0 ? "（点「📂 导入」添加存储源或文件）" : "")
            : "资产库不可用（数据库打不开）——本页只读，不会崩。";
    }

    // ==================== 加载 ====================

    [RelayCommand]
    private void Refresh()
    {
        Reload();
        StatusText = IsDbAvailable ? $"已刷新：共 {Items.Count} 个资产" : "资产库不可用（数据库打不开）。";
    }

    private void Reload()
    {
        _all.Clear();
        if (!IsDbAvailable) { ApplyView(); return; }
        foreach (var r in _assets.QueryRows(limit: 2000)) _all.Add(r);
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
        foreach (var r in rows) _all.Add(r);
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
        if (!IsDraft) return;
        _all.Clear();
        Selected = null;
        IsDraft = false;
        Reload();                       // 回到库视图
        StatusText = "已清除预览结果，可以换个文件夹重新扫描。";
    }

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
            DetailDisplayName = row == null
                ? ""
                : row.DisplayName ?? row.Title ?? row.Name;
            if (row != null)
                foreach (var l in _assets.ListLocations(row.Id)) Locations.Add(l);
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
