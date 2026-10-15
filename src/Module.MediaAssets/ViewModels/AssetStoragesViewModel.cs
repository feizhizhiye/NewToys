using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
/// 「存储源」页（NT-020）：local / removable / nas 的登记 / 编辑 / 移除（⛔ 只移索引）+ 扫描入口。
/// <para>
/// 三个要点：
/// <list type="number">
///   <item>**离线语义**：页面每次重读都逐个体探测根目录可访问性并写回 <c>online</c>；
///         离线存储源在扫描时被跳过，其下 file_locations ⛔ 不被标 missing（重新上线 + 扫描即恢复）；</item>
///   <item>**移除只删索引**：二次确认里给明"将删除 N 个文件位置（资产记录保留）"，⛔ 绝不碰磁盘文件；</item>
///   <item>**扫描走任务队列**：入队后跳「任务中心」看进度 / 取消；队列跑完（变为空闲）自动回读统计。</item>
/// </list>
/// </para>
/// </summary>
public partial class AssetStoragesViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly AssetService _assets = null!;
    private readonly StorageService _storages = null!;
    private readonly ScanService _scan = null!;
    private readonly TaskQueue _queue = null!;
    private readonly INavigationService? _nav;
    private readonly List<StorageRow> _all = new();

    public ObservableCollection<StorageRow> Items { get; } = new();

    // ==================== 详情卡：统计 + 最近错误 ====================

    public ObservableCollection<StorageTypeStat> DetailTypeStats { get; } = new();
    public ObservableCollection<StorageErrorRow> DetailErrors { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private StorageRow? _selected;
    [ObservableProperty] private string _searchText = "";

    // ===== 详情卡的统计字段（选中项变化时回填） =====
    [ObservableProperty] private string _detailFileCount = "";
    [ObservableProperty] private string _detailSize = "";
    [ObservableProperty] private string _detailMissing = "";
    [ObservableProperty] private string _detailRootPath = "";
    [ObservableProperty] private string _detailType = "";
    [ObservableProperty] private string _detailStatus = "";
    [ObservableProperty] private string _detailCreated = "";
    [ObservableProperty] private string _detailUpdated = "";

    // ===== 表头 =====
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _rootHeader = "根路径";
    [ObservableProperty] private string _typeHeader = "类型";
    [ObservableProperty] private string _statusHeader = "状态";
    [ObservableProperty] private string _filesHeader = "文件数";
    [ObservableProperty] private string _lastScanHeader = "最近扫描";

    // ===== 列显隐 =====
    public bool IsNameVisible => Vis("Name");
    public bool IsRootVisible => Vis("Root");
    public bool IsTypeVisible => Vis("Type");
    public bool IsStatusVisible => Vis("Status");
    public bool IsFilesVisible => Vis("Files");
    public bool IsLastScanVisible => Vis("LastScan");

    // ==================== 工具条（NT-055 三轮 / 需求 §4.8.5：由 Shell 统一渲染） ====================

    /// <summary>
    /// 工具条：页内 ⛔ 不再自写 <c>Border.toolbar</c>（含自绘的 <c>detail-toggle</c>），
    /// 改由 Shell 渲染本描述符；详情开关由 Shell 的**浮层**统一给（本页实现 <c>IDetailPanelProvider</c>）。
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "➕ 添加存储源",
            Classes = "ghost",
            CommandPath = nameof(AddStorageCommand),
            IsEnabledPath = nameof(IsDbAvailable),
            ToolTip = "登记一个目录为存储源（⛔ 不扫描；扫描是下面的显式动作）"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            Placeholder = "搜索名称 / 根路径…",
            ValuePath = nameof(SearchText)
        },
        new()
        {
            Text = "🔍 扫描选中",
            CommandPath = nameof(ScanSelectedCommand),
            IsEnabledPath = nameof(CanScanSelected),
            ShowToolTipOnDisabled = true,
            ToolTip = "把选中的存储源扫一遍并直接入库（进度在「任务中心」看）"
        },
        new()
        {
            Text = "🔍 扫描全部",
            CommandPath = nameof(ScanAllCommand),
            IsEnabledPath = nameof(IsDbAvailable),
            ToolTip = "所有存储源扫一遍（离线的自动跳过）"
        },
        // 🔄 命名规则（需求 §4.8 硬规则 10）：重读 = 读数据库 + 逐个体探测在线状态
        new()
        {
            Text = "🔄 重读存储源",
            CommandPath = nameof(RefreshStoragesCommand),
            ToolTip = "重新读取存储源表，并逐个探测根目录是否可访问（在线 / 离线）"
        }
    };

    /// <summary>
    /// ➕ 添加存储源：选一个目录 → 登记（⛔ 不扫描）。
    /// <para>NT-055 三轮：原 <c>AssetStoragesView.axaml.cs</c> 的 Click（要 TopLevel / StorageProvider）
    /// 搬进 VM，选择器走 <see cref="DialogHooks"/>（VM ⛔ 不引 Avalonia UI 类型）。</para>
    /// </summary>
    [RelayCommand]
    private async Task AddStorageAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择要登记为存储源的目录");
        if (string.IsNullOrWhiteSpace(dir)) return;
        AddStorage(dir);
    }

    protected override string PageKey => "assets-storages";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2.2*", sample: "移动硬盘 D"),
        new("Root", "根路径", "3*", sample: @"D:\Movies"),
        new("Type", "类型", "0.9*", sample: "移动"),
        new("Status", "状态", "0.9*", sample: "在线"),
        new("Files", "文件数", "0.9*", sample: "1,204"),
        new("LastScan", "最近扫描", "1.4*", sample: "2026-10-14 05:20")
    };

    protected override string? MandatoryColumnKey => "Name";

    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    /// <summary>列键 → 值（⛔ 列键与属性名不一致的必须显式写，否则筛选项静默失效）。</summary>
    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not StorageRow s) return base.FieldText(row, columnKey);
        return columnKey switch
        {
            "Name" => s.Name,
            "Root" => s.RootPath,
            "Type" => s.TypeText,
            "Status" => s.StatusText,
            "Files" => s.FileCountText,
            "LastScan" => s.LastScanText,
            _ => base.FieldText(row, columnKey)
        };
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;
    public bool ShowSampleRow => HasSampleRow && Items.Count == 0;
    public bool ShowDetailBody => HasSelection || IsDetailSample;
    public bool IsDbAvailable => _assets?.IsAvailable ?? false;

    /// <summary>「扫描选中」可用态（Avalonia 绑定不支逻辑运算符，⛔ 别在 XAML 里写 And）。</summary>
    public bool CanScanSelected => HasSelection && IsDbAvailable;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    /// <summary>详情卡的数据源：有选中项用真值，没选中用**示例行**（示例态整卡禁用）。</summary>
    public StorageRow DetailStorage => Selected ?? SampleStorage;

    private static readonly StorageRow SampleStorage = new()
    {
        Id = 0,
        Name = "移动硬盘 D",
        RootPath = @"D:\Movies",
        Type = StorageTypes.Removable,
        Online = true,
        FileCount = 1204,
        LastScanAt = "2026-10-14 05:20:00"
    };

    public AssetStoragesViewModel() { }

    public AssetStoragesViewModel(AssetService assets, StorageService storages, ScanService scan, TaskQueue queue,
        IColumnLayoutService cols, INavigationService? nav = null)
        : base(cols)
    {
        _assets = assets;
        _storages = storages;
        _scan = scan;
        _queue = queue;
        _nav = nav;

        LoadLayout();
        SetQuickFilters(new[]
        {
            ("类型：本地", "Type", "本地"),
            ("类型：移动", "Type", "移动"),
            ("类型：NAS", "Type", "NAS"),
            ("状态：在线", "Status", "在线"),
            ("状态：离线", "Status", "离线")
        });
        Reload();
        UpdateDetailSample(false);
        LoadDetail(null);
        StatusText = StatusSummary();

        // 队列跑完（变为空闲）→ 自动回读：扫描改了文件数 / 缺失数，详情卡与列表都要跟上。
        // ⚠️ TaskQueue.Changed 来自后台线程 → 必须 Post 回 UI 线程。
        var wasBusy = _queue.IsBusy;
        _queue.Changed += (_, _) =>
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var busy = _queue.IsBusy;
                    if (!busy && wasBusy) Reload();
                    wasBusy = busy;
                });
            }
            catch { /* 设计期 / 已关闭 */ }
        };
    }

    // ==================== 加载 ====================

    [RelayCommand]
    private void RefreshStorages()
    {
        Reload();
        StatusText = IsDbAvailable ? $"已重读存储源：共 {Items.Count} 个" : "资产库不可用（数据库打不开）。";
    }

    private void Reload()
    {
        _all.Clear();
        if (IsDbAvailable) foreach (var s in _storages.ListRows()) _all.Add(s);

        var keep = Selected?.Id ?? 0;
        ApplyView();
        if (keep > 0) Selected = Items.FirstOrDefault(s => s.Id == keep) ?? Selected;
    }

    private string StatusSummary()
        => IsDbAvailable
            ? $"共 {Items.Count} 个存储源" + (Items.Count == 0 ? "（点「➕ 添加存储源」登记一个目录）" : "")
            : "资产库不可用（数据库打不开）—— 本页只读，不会崩。";

    private void ApplyView()
    {
        IEnumerable<StorageRow> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(s => s.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || s.RootPath.Contains(text, StringComparison.OrdinalIgnoreCase));

        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Root" => Order(q, s => s.RootPath),
            "Type" => Order(q, s => s.TypeText),
            "Status" => Order(q, s => s.Online),
            "Files" => Order(q, s => s.FileCount),
            "LastScan" => Order(q, s => s.LastScanAt),
            _ => Order(q, s => s.Name)
        };

        Items.Clear();
        foreach (var s in q) Items.Add(s);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanScanSelected));
        OnPropertyChanged(nameof(ShowSampleRow));
    }

    private IOrderedEnumerable<StorageRow> Order<TKey>(IEnumerable<StorageRow> q, Func<StorageRow, TKey> key)
        => SortDescending ? q.OrderByDescending(key) : q.OrderBy(key);

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        RootHeader = "根路径" + SortArrow("Root");
        TypeHeader = "类型" + SortArrow("Type");
        StatusHeader = "状态" + SortArrow("Status");
        FilesHeader = "文件数" + SortArrow("Files");
        LastScanHeader = "最近扫描" + SortArrow("LastScan");
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    // ==================== 添加 / 编辑 / 移除（逻辑操作，⛔ 不动磁盘） ====================

    /// <summary>「➕ 添加存储源」：View 选完目录后调这里——登记即可（⛔ 不扫描，扫描是显式动作）。</summary>
    public void AddStorage(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }

        var existing = _assets.FindStorageByRoot(dir);
        if (existing != null)
        {
            StatusText = $"「{dir}」已经登记过了（名称：{existing.Name}）。";
            Selected = Items.FirstOrDefault(s => s.Id == existing.Id);
            return;
        }

        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) name = dir;

        var s = new StorageSource { Name = name, RootPath = dir, Type = StorageTypes.Local, Online = StorageService.ProbeOnline(dir) };
        var id = _assets.UpsertStorage(s);
        Reload();
        Selected = Items.FirstOrDefault(x => x.Id == id);
        StatusText = id > 0
            ? $"已登记「{name}」——点「🔍 扫描选中」把它扫进库（或先「✏ 重命名」改个名字）。"
            : "登记失败（数据库不可用？）。";
    }

    /// <summary>重命名（行右键）：只改库里的名称，⛔ 不动磁盘。</summary>
    [RelayCommand]
    private async Task RenameStorageAsync()
    {
        if (Selected == null) { StatusText = "先选中一个存储源。"; return; }

        var row = Selected;
        var name = await DialogHooks.InputAsync("重命名存储源", row.Name).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == row.Name) { StatusText = "已取消（名字没变）。"; return; }

        _assets.UpsertStorage(new StorageSource
        {
            Id = row.Id, Name = name.Trim(), RootPath = row.RootPath, Type = row.Type, Online = row.Online
        });
        Reload();
        StatusText = $"已把「{row.Name}」改名为「{name.Trim()}」（⛔ 未动磁盘）。";
    }

    /// <summary>切换类型（行右键）：本地 → 移动 → NAS → 本地 循环。</summary>
    [RelayCommand]
    private void CycleType()
    {
        if (Selected == null) { StatusText = "先选中一个存储源。"; return; }

        var row = Selected;
        var next = row.Type switch
        {
            StorageTypes.Local => StorageTypes.Removable,
            StorageTypes.Removable => StorageTypes.Nas,
            _ => StorageTypes.Local
        };
        _assets.UpsertStorage(new StorageSource
        {
            Id = row.Id, Name = row.Name, RootPath = row.RootPath, Type = next, Online = row.Online
        });
        Reload();
        StatusText = $"「{row.Name}」的类型已改为 {row.TypeText} → {new StorageRow { Type = next }.TypeText}。";
    }

    /// <summary>上线 / 下线（行右键）：写回 <c>online</c>。下线 = 扫描跳过它（其下位置不标 missing）。</summary>
    [RelayCommand]
    private void ToggleOnline()
    {
        if (Selected == null) { StatusText = "先选中一个存储源。"; return; }

        var row = Selected;
        var next = !row.Online;
        _storages.SetOnline(row.Id, next);
        Reload();
        StatusText = next
            ? $"「{row.Name}」已标记为在线——点「🔍 扫描选中」校准其下文件状态。"
            : $"「{row.Name}」已标记为离线——扫描将跳过它（⛔ 不会把它下面的文件标成丢失）。";
    }

    /// <summary>移除存储源（行右键；二次确认）：**只删索引**，⛔ 绝不碰磁盘文件。</summary>
    [RelayCommand]
    private async Task RemoveStorageAsync()
    {
        if (Selected == null) { StatusText = "先选中一个存储源。"; return; }

        var row = Selected;
        var locations = _storages.CountLocations(row.Id);
        var warn = $"移除存储源「{row.Name}」（{row.RootPath}）？\n" +
                   $"只删索引：该源下的 {locations} 个文件位置将删除，资产记录保留（重新登记 + 扫描即可找回）。\n" +
                   "⛔ 磁盘上的文件一个都不会动。";
        if (!await DialogHooks.ConfirmAsync(warn).ConfigureAwait(false)) { StatusText = "已取消。"; return; }

        _assets.DeleteStorage(row.Id);
        Reload();
        StatusText = $"已移除存储源「{row.Name}」（删了 {locations} 个文件位置索引；磁盘文件未动）。";
    }

    // ==================== 扫描（走任务队列 → 跳任务中心看进度） ====================

    /// <summary>「🔍 扫描选中」：把选中存储源扫一遍（直接入库），进度在「任务中心」看。</summary>
    [RelayCommand]
    private void ScanSelected()
    {
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }
        if (Selected == null) { StatusText = "先选中一个存储源。"; return; }

        var row = Selected;
        // 离线也允许扫——但先探测：根目录其实可达（用户手动标了离线 / 硬盘又插上了）→ 顺手改回在线；
        // 真的读不到 → 明确拒绝，⛔ 不入队一个必然被跳过的任务。
        if (!row.Online)
        {
            if (!StorageService.ProbeOnline(row.RootPath))
            {
                StatusText = $"「{row.Name}」当前离线且根目录读不到（{row.RootPath}）——先插上硬盘 / 连上 NAS，或点「⬆ 上线」强制标记。";
                return;
            }
            _storages.SetOnline(row.Id, true);
            Reload();
            StatusText = $"「{row.Name}」根目录又可访问了——已自动改回在线，开始扫描。";
        }

        EnqueueScan(row.Id, $"扫描存储源「{row.Name}」");
        _nav?.NavigateTo("assets-tasks");
    }

    /// <summary>「🔍 扫描全部」：所有存储源扫一遍（在线的才会真正遍历；离线的跳过）。</summary>
    [RelayCommand]
    private void ScanAll()
    {
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }
        if (_all.Count == 0) { StatusText = "还没有登记任何存储源。"; return; }

        EnqueueScan(null, "扫描全部存储源");
        _nav?.NavigateTo("assets-tasks");
    }

    /// <summary>入队一次扫描（任务中心可取消 / 看进度；完成后页面经 <see cref="TaskQueue.Changed"/> 自动回读）。</summary>
    private void EnqueueScan(long? storageId, string title)
    {
        _queue.Enqueue(title, async (ct, p) =>
        {
            var job = await _scan.ScanAsync(storageId, p, ct).ConfigureAwait(false);
            p.Report(job.Status switch
            {
                ScanStatuses.Cancelled => "已取消",
                _ => $"完成：新增 {job.AddedFiles} / 更新 {job.UpdatedFiles} / 丢失 {job.MissingFiles} / 失败 {job.ErrorCount}"
            });
        });
        StatusText = $"已把「{title}」排进任务队列——正在打开「任务中心」看进度。";
    }

    // ==================== 详情卡 ====================

    partial void OnSelectedChanged(StorageRow? value)
    {
        UpdateDetailSample(value != null);
        OnPropertyChanged(nameof(DetailStorage));
        LoadDetail(value);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanScanSelected));
        RaiseDetailChanged();
    }

    private void LoadDetail(StorageRow? row)
    {
        DetailTypeStats.Clear();
        DetailErrors.Clear();

        var target = row ?? (HasSampleRow ? SampleStorage : null);
        DetailRootPath = target?.RootPath ?? "";
        DetailType = target?.TypeText ?? "";
        DetailStatus = target?.StatusText ?? "";
        DetailCreated = target?.CreatedAt ?? "";
        DetailUpdated = target?.UpdatedAt ?? "";

        if (target == null || target.Id <= 0)
        {
            // 示例态 / 无选中：统计位给示例值（与列表示例行同一批数据）
            DetailFileCount = target == null ? "" : target.FileCountText + " 个";
            DetailSize = target == null ? "" : AssetRow.FormatSize(4_509_715_660);
            DetailMissing = target == null ? "" : "无缺失";
            OnPropertyChanged(nameof(HasTypeStats));
            OnPropertyChanged(nameof(HasErrors));
            return;
        }

        var stats = _storages.Stats(target.Id);
        DetailFileCount = stats.FileCountText;
        DetailSize = stats.SizeText;
        DetailMissing = stats.MissingText;
        foreach (var (type, count, size) in stats.ByType)
            DetailTypeStats.Add(new StorageTypeStat(AssetTypeTexts.Of(type), count, AssetRow.FormatSize(size)));
        foreach (var (path, message, at) in _storages.RecentErrors(target.Id))
            DetailErrors.Add(new StorageErrorRow(path, message, at));

        OnPropertyChanged(nameof(HasTypeStats));
        OnPropertyChanged(nameof(HasErrors));
    }

    public bool HasTypeStats => DetailTypeStats.Count > 0;
    public bool HasErrors => DetailErrors.Count > 0;

    // ==================== 详情面板（Shell 承载） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图（NT-054 迁移：原来的 <c>StorageDetailView</c> 已停用）。</summary>
    public object? DetailContent => null;

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（NT-054 / 需求 §4.5.1，**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的 <c>StorageDetailView</c> 对应：基本信息 / 统计 / 按类型分布 / 最近扫描错误 / 时间戳，**无卡内动作**（编辑走行右键）。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        SamplePath = nameof(IsDetailSample),
        EmptyText = "在左侧列表里选中一个存储源，这里显示它的详情与统计。",
        SampleText = "（示例数据）在左侧选中一个存储源后，这里显示它的真实信息。",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    // 原来卡首是动态标题 `{Binding DetailStorage.Name}`；描述符的分区标题是静态串 ⇒ 改成一个「名称」字段
                    new DetailField { Label = "名称", ValuePath = "DetailStorage.Name", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "根路径", ValuePath = nameof(DetailRootPath), Kind = FieldKind.Text, Selectable = true },
                    // 原来「类型 / 状态」是**同行并排** → 按 NT-054 已拍板口径拆成两行
                    new DetailField { Label = "类型", ValuePath = nameof(DetailType), Kind = FieldKind.Text },
                    new DetailField { Label = "状态", ValuePath = nameof(DetailStatus), Kind = FieldKind.Text },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField { Label = "文件位置数", ValuePath = nameof(DetailFileCount), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "已用大小", ValuePath = nameof(DetailSize), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "缺失位置", ValuePath = nameof(DetailMissing), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "按类型分布",
                        Kind = FieldKind.List,
                        OptionsPath = nameof(DetailTypeStats),
                        ItemTextPath = "TypeText",
                        ItemSubPath = "CountText",
                        ItemDetailPath = "SizeText"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（还没有文件位置）",
                        VisibleWhen = "!" + nameof(HasTypeStats)
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "最近扫描错误",
                        Kind = FieldKind.List,
                        OptionsPath = nameof(DetailErrors),
                        ItemTextPath = "Message",
                        ItemDetailPath = "Path"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（没有记录错误）",
                        VisibleWhen = "!" + nameof(HasErrors)
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField { Label = "登记时间", ValuePath = nameof(DetailCreated), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "最近更新", ValuePath = nameof(DetailUpdated), Kind = FieldKind.Text, Selectable = true }
                }
            }
        }
    };

    public event EventHandler? DetailChanged;

    /// <summary>工具条末位开关：⛔ 别绑基类 <c>ToggleDetailCommand</c>（它不通知 Shell，右列不会出现）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible) EnsureDetailSelection();
        SaveDetailFlag();
        RaiseDetailChanged();
    }

    public void OpenDetail()
    {
        EnsureDetailSelection();
        IsDetailVisible = true;
        SaveDetailFlag();
        RaiseDetailChanged();
    }

    public void ToggleDetail() => ToggleDetailPanel();

    private void RaiseDetailChanged() => DetailChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>详情卡「按类型分布」的一行。</summary>
public sealed class StorageTypeStat
{
    public string TypeText { get; }
    public int Count { get; }
    public string SizeText { get; }

    public StorageTypeStat(string typeText, int count, string sizeText)
    {
        TypeText = typeText;
        Count = count;
        SizeText = sizeText;
    }

    public string CountText => Count.ToString("N0", CultureInfo.InvariantCulture) + " 个";
}

/// <summary>详情卡「最近错误」的一行。</summary>
public sealed class StorageErrorRow
{
    public string Path { get; }
    public string Message { get; }
    public string CreatedAt { get; }

    public StorageErrorRow(string path, string message, string createdAt)
    {
        Path = path;
        Message = message;
        CreatedAt = createdAt;
    }
}
