using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
/// 「回收站」页（NT-019）：`assets.status='trashed'` 的资产列表。
/// <para>
/// ⚠️ 分清两层：
/// <list type="bullet">
///   <item><b>软删除 / 恢复</b>（「🗑 移入回收站」/「♻ 恢复」）= **只改库状态**，逻辑操作，⛔ 不走任务队列；</item>
///   <item><b>永久删除 / 清空</b> = **真删磁盘文件**，物理操作 → 走任务队列 + 日志先行 + 二次确认。</item>
/// </list>
/// </para>
/// </summary>
public partial class AssetTrashViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly AssetService _assets = null!;
    private readonly FileOperationService _fileOps = null!;
    private readonly OperationLogService _opLog = null!;
    private readonly List<AssetRow> _all = new();

    public ObservableCollection<AssetRow> Items { get; } = new();

    /// <summary>选中资产的全部文件位置（详情卡列出）。</summary>
    public ObservableCollection<FileLocation> Locations { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private AssetRow? _selected;
    [ObservableProperty] private string _searchText = "";

    // ===== 表头 =====
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _typeHeader = "类型";
    [ObservableProperty] private string _sizeHeader = "大小";
    [ObservableProperty] private string _modifiedHeader = "修改时间";
    [ObservableProperty] private string _storageHeader = "存储源";

    public bool IsNameVisible => Vis("Name");
    public bool IsTypeVisible => Vis("Type");
    public bool IsSizeVisible => Vis("Size");
    public bool IsModifiedVisible => Vis("Modified");
    public bool IsStorageVisible => Vis("Storage");

    // ==================== 工具条（NT-055 三轮 / 需求 §4.8.5：由 Shell 统一渲染） ====================

    /// <summary>
    /// 工具条：页内 ⛔ 不再自写 <c>Border.toolbar</c>（含自绘的 <c>detail-toggle</c>），
    /// 改由 Shell 渲染本描述符；详情开关由 Shell 的**浮层**统一给（本页实现 <c>IDetailPanelProvider</c>）。
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "♻ 恢复",
            Classes = "ghost",
            CommandPath = nameof(RestoreCommand),
            IsEnabledPath = nameof(HasSelection),
            ShowToolTipOnDisabled = true,
            ToolTip = "恢复回「正常」状态（⛔ 只改库里的状态，磁盘文件本来就还在）"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            Placeholder = "搜索名称 / 路径…",
            ValuePath = nameof(SearchText)
        },
        new()
        {
            Text = "🗑 永久删除选中",
            Classes = "danger-btn",
            CommandPath = nameof(PurgeSelectedCommand),
            IsEnabledPath = nameof(HasSelection),
            ShowToolTipOnDisabled = true,
            ToolTip = "真删磁盘文件 + 删索引行（二次确认；⛔ 不进回收站、无法还原）"
        },
        new()
        {
            Text = "🧹 清空回收站",
            Classes = "danger-btn",
            CommandPath = nameof(EmptyTrashCommand),
            ToolTip = "把回收站里所有资产永久删除（二次确认）"
        },
        // 🔄 命名规则（需求 §4.8 硬规则 10）：重读 = 读数据库
        new()
        {
            Text = "🔄 重读回收站",
            CommandPath = nameof(RefreshTrashCommand),
            ToolTip = "重新读取资产库里的回收站状态行（⛔ 不扫盘）"
        },
    };

    protected override string PageKey => "assets-trash";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2.6*", sample: "The.Matrix.1999.1080p.mkv"),
        new("Type", "类型", "0.8*", sample: "视频"),
        new("Size", "大小", "0.9*", sample: "4.2 GB"),
        new("Modified", "修改时间", "1.3*", sample: "2026-10-13 21:04"),
        new("Storage", "存储源", "1.2*", sample: "移动硬盘 D")
    };

    protected override string? MandatoryColumnKey => "Name";

    protected override void RefreshView() => ApplyView();

    public bool IsDbAvailable => _assets?.IsAvailable ?? false;
    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;
    public bool ShowSampleRow => HasSampleRow && Items.Count == 0;
    public bool ShowDetailBody => HasSelection;
    public bool HasLocations => Locations.Count > 0;

    /// <summary>详情卡当前显示的资产（没选中 → 示例行，只读展示）。</summary>
    public AssetRow DetailRow => Selected ?? SampleRow;

    private static readonly AssetRow SampleRow = new()
    {
        Name = "The.Matrix.1999.1080p.mkv",
        Type = AssetTypes.Video,
        Status = AssetStatuses.Trashed,
        FilePath = @"D:\Movies\The.Matrix.1999.1080p.mkv",
        Size = 4_509_715_660,
        Mtime = "2026-10-13 21:04:00",
        StorageName = "移动硬盘 D"
    };

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public AssetTrashViewModel() { }

    public AssetTrashViewModel(AssetService assets, FileOperationService fileOps, OperationLogService opLog, IColumnLayoutService cols)
        : base(cols)
    {
        _assets = assets;
        _fileOps = fileOps;
        _opLog = opLog;

        LoadLayout();
        Reload();
        StatusText = StatusSummary();
    }

    // ==================== 加载 ====================

    [RelayCommand]
    private void RefreshTrash()
    {
        Reload();
        StatusText = IsDbAvailable ? $"已重读回收站：共 {Items.Count} 个" : "资产库不可用（数据库打不开）。";
    }

    private void Reload()
    {
        _all.Clear();
        if (IsDbAvailable)
        {
            // includeTrashed = true 才会把 status='trashed' 的行带回来；再按状态筛出回收站这一档
            foreach (var r in _assets.QueryRows(null, true, AssetAllViewModel.QueryLimit))
                if (string.Equals(r.Status, AssetStatuses.Trashed, StringComparison.Ordinal)) _all.Add(r);
        }

        var keep = Selected?.Id ?? 0;
        ApplyView();
        if (keep > 0) Selected = Items.FirstOrDefault(r => r.Id == keep) ?? Selected;
    }

    private string StatusSummary()
        => IsDbAvailable
            ? $"回收站里共 {Items.Count} 个资产（⛔ 磁盘文件没动，只是库里标了 trashed）"
            : "资产库不可用（数据库打不开）—— 本页只读，不会崩。";

    private void ApplyView()
    {
        IEnumerable<AssetRow> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(r => r.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || r.FilePath.Contains(text, StringComparison.OrdinalIgnoreCase));

        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Type" => Order(q, r => r.Type),
            "Size" => Order(q, r => r.Size),
            "Modified" => Order(q, r => r.Mtime ?? ""),
            "Storage" => Order(q, r => r.StorageName),
            _ => Order(q, r => r.Name)
        };

        Items.Clear();
        foreach (var r in q) Items.Add(r);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowSampleRow));
    }

    private IOrderedEnumerable<AssetRow> Order<TKey>(IEnumerable<AssetRow> q, Func<AssetRow, TKey> key)
        => SortDescending ? q.OrderByDescending(key) : q.OrderBy(key);

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        TypeHeader = "类型" + SortArrow("Type");
        SizeHeader = "大小" + SortArrow("Size");
        ModifiedHeader = "修改时间" + SortArrow("Modified");
        StorageHeader = "存储源" + SortArrow("Storage");
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    // ==================== 软删除的撤销：恢复（逻辑操作） ====================

    /// <summary>把选中资产**恢复**回正常状态（⛔ 只改库里的状态，磁盘文件本来就没动）。</summary>
    [RelayCommand]
    private void Restore()
    {
        if (Selected == null) { StatusText = "先选中一个资产。"; return; }

        var row = Selected;
        _assets.SetAssetStatus(row.Id, AssetStatuses.Active);
        _opLog.Log("restore", "asset", row.Id, beforeJson: "{\"status\":\"trashed\"}", afterJson: "{\"status\":\"active\"}");
        Reload();
        StatusText = $"已恢复「{row.Name}」（库里状态改回正常）。";
    }

    // ==================== 真删：永久删除 / 清空（物理操作 → 任务队列） ====================

    /// <summary>永久删除选中资产（**真删磁盘文件** + 删库里的索引行；二次确认后走任务队列）。</summary>
    [RelayCommand]
    private async Task PurgeSelectedAsync()
    {
        if (Selected == null) { StatusText = "先选中一个资产。"; return; }

        var row = Selected;
        var n = IsDbAvailable ? _assets.ListLocations(row.Id).Count : 0;
        if (!await DialogHooks.ConfirmAsync(
                $"**永久删除**「{row.Name}」？\n" +
                $"会真删磁盘上的 {n} 个文件位置（⛔ 不进回收站、**无法还原**），并删掉库里的索引行。\n" +
                $"（只想撤销软删除 → 用左边的「♻ 恢复」）").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        EnqueuePurge(new List<AssetRow> { row });
    }

    /// <summary>清空回收站 = 对里面每个资产执行永久删除（二次确认；一次排进队列）。</summary>
    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        if (_all.Count == 0) { StatusText = "回收站已经是空的。"; return; }

        var rows = _all.ToList();
        var files = 0;
        if (IsDbAvailable) foreach (var r in rows) files += _assets.ListLocations(r.Id).Count;

        if (!await DialogHooks.ConfirmAsync(
                $"清空回收站？共 {rows.Count} 个资产 / 约 {files} 个文件位置。\n" +
                $"会**真删磁盘文件**（⛔ 不进回收站、**无法还原**），并删掉库里的索引行。").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        EnqueuePurge(rows);
    }

    /// <summary>把「永久删除」排进任务队列（物理操作走队列 → 任务中心可见、可取消）。</summary>
    private void EnqueuePurge(IReadOnlyCollection<AssetRow> rows)
    {
        _fileOps.Queue.Enqueue($"永久删除 {rows.Count} 个资产", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                var done = 0;
                var failed = 0;
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    p.Report($"永久删除 {row.Name} …");

                    var locs = _assets.ListLocations(row.Id);
                    foreach (var l in locs)
                    {
                        if (l.Status == LocationStatuses.Deleted) continue;   // 之前已经删过盘上的文件
                        var r = _fileOps.PermanentDelete(l);
                        if (r.Ok) { _assets.DeleteLocation(l.Id); done++; } else failed++;
                    }
                    _assets.DeleteAsset(row.Id);
                    _opLog.Log("purge", "asset", row.Id, afterJson: $"{{\"files\":{locs.Count},\"failed\":{failed}}}");
                }
                p.Report($"完成：删除 {done} 个文件，失败 {failed} 个");
            }, ct).ConfigureAwait(false);
        });

        StatusText = $"已把 {rows.Count} 个资产的永久删除排进任务队列（进度看「任务中心」）。";
        Dispatcher.UIThread.Post(Reload);
    }

    // ==================== 单条动作（右键） ====================

    /// <summary>在资源管理器里定位该文件（⛔ 不打开文件本体 —— 这条已经在回收站了）。</summary>
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

    // ==================== 详情面板（Shell 承载） ====================

    partial void OnSelectedChanged(AssetRow? value)
    {
        OnPropertyChanged(nameof(DetailRow));
        OnPropertyChanged(nameof(HasSelection));
        LoadDetail(value);
        RaiseDetailChanged();
    }

    private void LoadDetail(AssetRow? row)
    {
        Locations.Clear();
        if (row != null && IsDbAvailable)
            foreach (var l in _assets.ListLocations(row.Id)) Locations.Add(l);
        OnPropertyChanged(nameof(HasLocations));
        OnPropertyChanged(nameof(ShowDetailBody));
    }

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图（NT-054 迁移：原来的 <c>TrashDetailView</c> 已删）。</summary>
    public object? DetailContent => null;

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（NT-054 / 需求 §4.5.1，**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的 <c>TrashDetailView</c> 1:1 对应：资产只读信息 + 受影响的文件位置列表 + 两个卡内动作。</para>
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
                    new DetailField { Label = "名称", ValuePath = "DetailRow.Name", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "状态", ValuePath = "DetailRow.StatusText", Kind = FieldKind.Text },
                    new DetailField { Label = "类型", ValuePath = "DetailRow.TypeText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "大小", ValuePath = "DetailRow.SizeText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "修改时间", ValuePath = "DetailRow.ModifiedText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "存储源", ValuePath = "DetailRow.StorageName", Kind = FieldKind.Text },
                    new DetailField { Label = "删除时间", ValuePath = "DetailRow.UpdatedAtText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Kind = FieldKind.Divider },
                    // 受影响的文件位置（永久删除会真删这些）：原 ItemsControl 两行一项 → List Kind
                    new DetailField
                    {
                        Label = "文件位置（永久删除会真删这些）",
                        Kind = FieldKind.List,
                        OptionsPath = nameof(Locations),
                        ItemTextPath = "Path",
                        ItemDetailPath = "Status"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（这个资产没有登记任何文件位置）",
                        VisibleWhen = "!" + nameof(HasLocations)
                    },
                    new DetailField { Kind = FieldKind.Divider }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "♻ 恢复",
                Classes = "primary",
                ToolTip = "改回「正常」状态（⛔ 只改库里的状态，磁盘文件本来就还在）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = RestoreCommand
            },
            new DetailAction
            {
                Text = "🗑 永久删除",
                Classes = "danger-btn",
                ToolTip = "真删磁盘文件 + 删索引行（二次确认；⛔ 不进回收站、无法还原）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = PurgeSelectedCommand
            }
        }
    };

    public event EventHandler? DetailChanged;

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
