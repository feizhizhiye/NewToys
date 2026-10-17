using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 「脚本管理器 → 信息面板」页（NT-068 S5/S6；取代 NT-067 的 manifest 驱动模型）。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>一次只显示一个集合</b>：顶部只有一个「集合 ▾」选择框（工具条 <see cref="ToolbarItemKind.Combo"/>），
/// 切集合 = 整屏换（浏览器开一个页面的心智）。⛔ 不做 Chip 条 / 横向按钮墙 / 多集合并存（用户 2026-10-17 否决）。
/// </para>
/// <para>
/// 节 = 采集器（<c>capture=true</c> + <c>collectable=true</c> 的条目），节体用**这一采集器自己的列**
/// （<see cref="ScriptItem.Columns"/>，首次成功采集时按输出属性生成，§三.13）。
/// </para>
/// <para>
/// 分节模式<b>关分页</b>：底栏只给摘要（本次耗时 · 节数 · 总行数）。
/// </para>
/// </remarks>
public partial class ScriptInfoViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    /// <summary>节数上限（§三.8）。</summary>
    public const int MaxSections = 40;

    // ==================== 依赖 ====================
    private readonly IScriptStoreService? _store;
    private readonly IScriptManagerSettingsService? _settings;
    private readonly CollectorService? _collector;
    private readonly INavigationService? _nav;

    /// <summary>并发上限 4（沿 NT-067）。</summary>
    private readonly SemaphoreSlim _gate = new(4, 4);

    /// <summary>条目 → 上次采集时刻（定时刷新到期判定用）。</summary>
    private readonly Dictionary<string, DateTime> _lastRun = new(StringComparer.OrdinalIgnoreCase);

    private DispatcherTimer? _timer;

    /// <summary>压住「切集合 → 下拉回写」的循环。</summary>
    private bool _loadingSelection;

    /// <summary>本次有列 schema 变化（批末统一落盘）。</summary>
    private bool _columnsDirty;

    private string _storageWarning = "";

    /// <summary>当前集合的视图定义（折叠状态 / 刷新覆盖落在它里面）。</summary>
    private ScriptView? _view;

    // ==================== 声明式工具条（需求 §4.8.5） ====================
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Kind = ToolbarItemKind.Label, Text = "集合" },
        new()
        {
            Kind = ToolbarItemKind.Combo, Width = 240,
            ItemsPath = nameof(Collections),
            DisplayPath = nameof(InfoCollectionOption.Display),
            SelectedItemPath = nameof(SelectedCollection),
            ToolTip = "一次显示一个集合（★置顶 → 最近使用 → 名称）",
        },
        new() { Kind = ToolbarItemKind.Label, Text = "视图" },
        new()
        {
            Kind = ToolbarItemKind.Combo, Width = 140,
            ItemsPath = nameof(Views),
            DisplayPath = nameof(InfoViewOption.Display),
            SelectedItemPath = nameof(SelectedView),
            IsEnabledPath = nameof(HasViews),
            ToolTip = "同一集合的不同呈现（布局 / 折叠状态 / 刷新频率）",
        },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Text = "🔄 全部刷新", Classes = "ghost",
            CommandPath = nameof(RefreshAllCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "刷新当前集合里全部未折叠的节",
        },
        new()
        {
            Text = "⚙ 节设置", Classes = "ghost",
            CommandPath = nameof(ToggleSectionSettingsCommand),
            IsEnabledPath = nameof(HasSettingsSection),
            ToolTip = "解析器 / 超时 / 保留策略 / 自动刷新（针对当前选中的节）",
        },
        new()
        {
            Text = "📂 脚本目录", Classes = "ghost",
            CommandPath = nameof(OpenScriptDirCommand),
            ToolTip = "打开脚本目录（内置采集脚本在 sysinfo\\ 下）",
        },
    };

    // ==================== 构造 ====================
    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public ScriptInfoViewModel() { }

    public ScriptInfoViewModel(IColumnLayoutService cols, IScriptStoreService store,
        IScriptManagerSettingsService settings, CollectorService collector, INavigationService nav)
        : base(cols)
    {
        _store = store;
        _settings = settings;
        _collector = collector;
        _nav = nav;

        // 内置采集脚本 + 条目 + 内置信息集合（只加不改；失败不影响页面）
        try
        {
            var note = BuiltinCollectorSeeder.Seed(store, settings);
            if (note.StartsWith("⚠", StringComparison.Ordinal)) StatusText = note;
        }
        catch (Exception ex) { StatusText = "⚠ 内置采集器播种失败：" + ex.Message; }

        LoadLayout();
        ReloadCollections();
        RestoreLastCollection();

        if (_store is { IsLoaded: false })
            StatusText = "⚠ " + _store.LastError;
    }

    // ==================== 可绑定状态 ====================
    public ObservableCollection<InfoCollectionOption> Collections { get; } = new();

    public ObservableCollection<InfoViewOption> Views { get; } = new();

    public ObservableCollection<InfoSectionViewModel> Sections { get; } = new();

    [ObservableProperty] private InfoCollectionOption? _selectedCollection;
    [ObservableProperty] private InfoViewOption? _selectedView;
    [ObservableProperty] private InfoSectionViewModel? _settingsSection;
    [ObservableProperty] private bool _isSectionSettingsOpen;
    [ObservableProperty] private bool _isBusy;

    /// <summary>详情卡选中的行（跨节统一一个选中）。</summary>
    [ObservableProperty] private SysInfoRow? _selectedRow;

    /// <summary>底栏摘要（本次 412ms · 3 节 · 22 行）。</summary>
    [ObservableProperty] private string _footerSummary = "";

    /// <summary>空态文案 / 按钮文案。</summary>
    [ObservableProperty] private string _emptyMessage = "";
    [ObservableProperty] private string _emptyActionText = "";

    public bool HasSections => Sections.Count > 0;
    public bool HasViews => Views.Count > 0;
    public bool HasSettingsSection => SettingsSection != null;
    public bool HasEmptyAction => EmptyActionText.Length > 0;
    public bool IsNotBusy => !IsBusy;
    public bool HasSelection => SelectedRow != null;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSettingsSectionChanged(InfoSectionViewModel? value)
    {
        OnPropertyChanged(nameof(HasSettingsSection));
        LoadSectionSettings();
    }

    partial void OnSelectedRowChanged(SysInfoRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailSource));
        OnPropertyChanged(nameof(DetailRowKey));
        OnPropertyChanged(nameof(DetailStatus));
        OnPropertyChanged(nameof(DetailCells));
    }

    // ==================== 基类契约 ====================
    protected override string PageKey => "script-info";

    /// <summary>本页没有全局列（列按节各自持有）⇒ 基类列元数据留空。</summary>
    protected override IReadOnlyList<ListColumn> Columns => Array.Empty<ListColumn>();

    protected override void RefreshView()
    {
        OnPropertyChanged(nameof(HasSections));
        OnPropertyChanged(nameof(HasEmptyAction));
        UpdateFooter();
    }

    protected override void EnsureDetailSelection()
    {
        if (SelectedRow == null) SelectedRow = Sections.FirstOrDefault()?.Selected;
    }

    // ==================== 详情卡 ====================
    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;
    public event EventHandler? DetailChanged;

    public bool ShowDetailBody => HasSelection;

    public string DetailSource => OwnerSectionOf(SelectedRow)?.Title ?? "";
    public string DetailRowKey => SelectedRow?.RowKey ?? "";
    public string DetailStatus => SysInfoStatus.Label(SelectedRow?.Status ?? SysInfoStatus.Idle);

    public IReadOnlyList<SysInfoCell> DetailCells
        => SelectedRow?.Cells ?? (IReadOnlyList<SysInfoCell>)Array.Empty<SysInfoCell>();

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "点某个节里的一行，这里显示它的全部属性值。",
        Sections =
        {
            new DetailSection
            {
                Header = "选中行",
                Fields =
                {
                    new DetailField { Label = "采集器", ValuePath = nameof(DetailSource), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "行键", ValuePath = nameof(DetailRowKey), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "状态", ValuePath = nameof(DetailStatus), Kind = FieldKind.Text },
                    new DetailField
                    {
                        Kind = FieldKind.List,
                        OptionsPath = nameof(DetailCells),
                        ItemLabelPath = "Header",
                        ItemTextPath = "Text",
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "值来自采集器脚本 / 命令的 stdout（列 = 输出里的属性名）。",
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "📋 复制「属性 = 值」",
                Classes = "primary",
                ToolTip = "把选中行的各属性按「属性 = 值」复制到剪贴板",
                IsVisiblePath = nameof(HasSelection),
                Command = CopySelectedRowCommand
            },
            new DetailAction
            {
                Text = "📂 脚本目录",
                Classes = "ghost",
                ToolTip = "打开脚本目录",
                Command = OpenScriptDirCommand
            }
        }
    };

    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    // ==================== 集合 / 视图 ====================

    private void ReloadCollections()
    {
        if (_store == null) return;

        var keepId = SelectedCollection?.Id ?? "";
        _loadingSelection = true;
        try
        {
            Collections.Clear();

            var options = _store.Data.Collections
                .Where(c => !c.Archived)
                .Select(c => new InfoCollectionOption
                {
                    Id = c.Id,
                    Name = string.IsNullOrWhiteSpace(c.Name) ? "未命名集合" : c.Name,
                    Icon = c.Icon,
                    Kind = c.Kind,
                    Pinned = c.Pinned,
                    LastOpenedAt = c.LastOpenedAt,
                    MemberCount = _store.Data.Items.Count(i => i.Collections.Contains(c.Id)),
                })
                // 排序 = ★置顶 → 最近使用 → 名称（§三.8）
                .OrderByDescending(o => o.Pinned)
                .ThenByDescending(o => o.LastOpenedUtc)
                .ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var o in options) Collections.Add(o);

            if (keepId.Length > 0)
                SelectedCollection = Collections.FirstOrDefault(o => o.Id == keepId);
        }
        finally { _loadingSelection = false; }
    }

    /// <summary>首次进页面：恢复上次选中的集合（<c>lastCollectionId</c>）；没有就选第一个。</summary>
    private void RestoreLastCollection()
    {
        if (Collections.Count == 0) { UpdateEmpty(); return; }

        var last = _store?.Data.LastCollectionId ?? "";
        var target = Collections.FirstOrDefault(o => o.Id == last) ?? Collections[0];

        _loadingSelection = true;
        try { SelectedCollection = target; }
        finally { _loadingSelection = false; }

        LoadCollection(target);
    }

    partial void OnSelectedCollectionChanged(InfoCollectionOption? value)
    {
        if (_loadingSelection) return;
        LoadCollection(value);
    }

    partial void OnSelectedViewChanged(InfoViewOption? value)
    {
        if (_loadingSelection || _store == null) return;
        if (value == null) { _view = null; return; }

        _view = _store.Data.Views.FirstOrDefault(v => v.Id == value.Id);
        ApplyCollapsedFromView();
    }

    /// <summary>载入一个集合：视图 + 节（先出缓存，再按刷新策略跑一次）。</summary>
    private void LoadCollection(InfoCollectionOption? option)
    {
        Sections.Clear();
        Views.Clear();
        _view = null;
        SelectedRow = null;

        if (_store == null || option == null) { UpdateEmpty(); return; }

        var col = _store.FindCollection(option.Id);
        if (col == null) { UpdateEmpty(); return; }

        // ---- 视图（没有就建一个默认视图，保证折叠状态可落盘） ----
        var views = _store.Data.Views.Where(v => v.CollectionId == col.Id).ToList();
        if (views.Count == 0)
        {
            var created = new ScriptView { Id = "view-" + col.Id, CollectionId = col.Id, Name = "默认" };
            _store.Data.Views.Add(created);
            views.Add(created);
            _store.Save();
        }
        if (string.IsNullOrWhiteSpace(col.DefaultViewId)) col.DefaultViewId = views[0].Id;

        _loadingSelection = true;
        try
        {
            foreach (var v in views) Views.Add(new InfoViewOption(v));
            SelectedView = Views.FirstOrDefault(v => v.Id == col.DefaultViewId) ?? Views[0];
            _view = _store.Data.Views.FirstOrDefault(v => v.Id == SelectedView.Id);
        }
        finally { _loadingSelection = false; }

        // ---- 节：集合成员里能采集的条目 ----
        var members = _store.ItemsOf(col.Id)
            .Where(i => i.Role != ScriptItemRole.Package && i.Capture && i.Collectable)
            .Take(MaxSections)
            .ToList();

        foreach (var item in members) Sections.Add(CreateSection(item));

        ApplyCollapsedFromView();
        LoadCachedRows();
        SelectedRow = Sections.FirstOrDefault()?.Selected;

        // ---- 记住上次选中（重启恢复，验收 §五.7） ----
        col.LastOpenedAt = Now();
        _store.Data.LastCollectionId = col.Id;
        _store.Save();

        OnPropertyChanged(nameof(HasViews));
        UpdateEmpty();
        UpdateFooter();

        // 刷新策略：manual 不动；on-enter / timer 进页面刷一次（只跑自动白名单）
        if (EffectiveMode() != ScriptRefreshMode.Manual) _ = RefreshAllCoreAsync(autoOnly: true);
    }

    private InfoSectionViewModel CreateSection(ScriptItem item)
    {
        var section = new InfoSectionViewModel(item)
        {
            RefreshRequested = s => _ = RefreshSectionsAsync(new[] { s }),
            SettingsRequested = OpenSettingsFor,
            CollapseChanged = PersistCollapsed,
            ColumnToggled = (_, _) => { _columnsDirty = true; _store?.Save(); },
        };

        section.PropertyChanged += OnSectionPropertyChanged;
        return section;
    }

    /// <summary>节内选中变化 → 详情卡跟着（跨节只留一个选中）。</summary>
    private void OnSectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InfoSectionViewModel.Selected)) return;
        if (sender is not InfoSectionViewModel section || section.Selected == null) return;

        foreach (var other in Sections)
            if (!ReferenceEquals(other, section) && other.Selected != null)
            {
                other.Selected = null;
                break;
            }

        SelectedRow = section.Selected;
    }

    private InfoSectionViewModel? OwnerSectionOf(SysInfoRow? row)
        => row == null ? null : Sections.FirstOrDefault(s => s.ItemId == row.ItemId);

    /// <summary>点某一行（节内）⇒ 选中这一行，详情卡跟着换（视图 code-behind 调用）。</summary>
    public void SelectRow(SysInfoRow row)
    {
        var section = OwnerSectionOf(row);
        if (section != null) section.Selected = row;
        SelectedRow = row;
    }

    /// <summary>把视图里记录的折叠状态套到节上。</summary>
    private void ApplyCollapsedFromView()
    {
        var collapsed = _view?.Collapsed;
        foreach (var s in Sections)
            s.IsCollapsed = collapsed != null && collapsed.Contains(s.ItemId);
    }

    private void PersistCollapsed(InfoSectionViewModel section)
    {
        if (_view == null) return;

        if (section.IsCollapsed)
        {
            if (!_view.Collapsed.Contains(section.ItemId)) _view.Collapsed.Add(section.ItemId);
        }
        else _view.Collapsed.RemoveAll(id => id == section.ItemId);

        _store?.Save();
    }

    /// <summary>先出上次的观测（进页面不空白），再异步刷新。</summary>
    private void LoadCachedRows()
    {
        if (_collector == null) return;

        foreach (var s in Sections)
        {
            var (order, map) = _collector.LoadLatest(s.Item);
            if (order.Count == 0) continue;

            s.RebuildHeaders();
            s.SetRows(order.Select(k => (k, SysInfoStatus.Ok, (IReadOnlyDictionary<string, string>)map[k])));
            s.MarkOk(SysInfoStatus.Ok, DateTime.TryParse(s.Item.LastRunAt, out var t) ? t : DateTime.Now);
        }
    }

    // ==================== 刷新 ====================

    /// <summary>🔄 全部刷新（当前集合里全部未折叠的节）。</summary>
    [RelayCommand]
    private async Task RefreshAllAsync() => await RefreshAllCoreAsync(autoOnly: false);

    private async Task RefreshAllCoreAsync(bool autoOnly)
    {
        if (IsBusy || _collector == null) return;

        // 手动「全部刷新」= 用户明确要数据 ⇒ 连折叠的节也刷；自动刷新只跑未折叠的（§三.9 硬约束 5）。
        var targets = autoOnly ? AutoTargets() : Sections.ToList();
        if (targets.Count == 0) { UpdateFooter(); return; }

        await RefreshSectionsAsync(targets).ConfigureAwait(true);
    }

    /// <summary>定时 / 进页面刷新时能跑的节：未折叠 + 只读 + 开关打开 + 间隔到期（§三.9）。</summary>
    private List<InfoSectionViewModel> AutoTargets()
    {
        var now = DateTime.UtcNow;
        var list = new List<InfoSectionViewModel>();

        foreach (var s in Sections)
        {
            if (s.IsCollapsed) continue;
            if (!CollectorService.CanAutoRefresh(s.Item) || !s.Item.Auto) continue;

            var interval = CollectorService.ClampInterval(s.Item.IntervalSeconds);
            if (interval < CollectorService.MinIntervalSeconds) continue;
            if (_lastRun.TryGetValue(s.ItemId, out var last) && (now - last).TotalSeconds < interval) continue;

            list.Add(s);
        }
        return list;
    }

    /// <summary>跑一批节（并发上限 4；同一节正在跑就跳过、不排队）。</summary>
    private async Task RefreshSectionsAsync(IReadOnlyList<InfoSectionViewModel> targets)
    {
        if (_collector == null || targets.Count == 0) return;

        IsBusy = true;
        var sw = Stopwatch.StartNew();
        _columnsDirty = false;
        _storageWarning = "";

        try
        {
            var tasks = new List<Task>();
            foreach (var s in targets)
            {
                if (!s.IsRefreshing) tasks.Add(RunSectionAsync(s));
            }
            if (tasks.Count > 0) await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        finally
        {
            sw.Stop();
            IsBusy = false;

            if (_columnsDirty) _store?.Save();
            foreach (var s in Sections) s.RebuildHeaders();
            SelectedRow ??= Sections.FirstOrDefault(x => x.Selected != null)?.Selected;

            UpdateFooter(sw.ElapsedMilliseconds);
            OnPropertyChanged(nameof(HasSections));

            if (_storageWarning.Length > 0) StatusText = "⚠ " + _storageWarning;
        }
    }

    private async Task RunSectionAsync(InfoSectionViewModel section)
    {
        section.IsRefreshing = true;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(true);
            try
            {
                var outcome = await _collector!.CollectAsync(section.Item, CancellationToken.None).ConfigureAwait(true);
                if (outcome.Canceled) return;

                if (outcome.StorageError is { Length: > 0 }) _storageWarning = outcome.StorageError;
                if (outcome.ColumnsChanged) _columnsDirty = true;

                if (outcome.Rows != null)
                {
                    section.RebuildHeaders();
                    section.SetRows(outcome.Rows.Select(r => (r.RowKey, r.Status, ToDict(r.Attrs))));
                    section.MarkOk(outcome.Status, DateTime.Now);
                }
                else
                {
                    section.MarkError(outcome.Error ?? "采集失败");
                }

                _lastRun[section.ItemId] = DateTime.UtcNow;
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex)
        {
            section.MarkError(ex.Message);
        }
        finally
        {
            section.IsRefreshing = false;
        }
    }

    private static IReadOnlyDictionary<string, string> ToDict(IReadOnlyList<KeyValuePair<string, string>> attrs)
    {
        var map = new Dictionary<string, string>(attrs.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in attrs) map[kv.Key] = kv.Value;
        return map;
    }

    /// <summary>局部刷新一个节（节头 ⟳ / 右键）。</summary>
    [RelayCommand]
    private async Task RefreshSectionAsync(InfoSectionViewModel? section)
    {
        if (section == null) return;
        await RefreshSectionsAsync(new[] { section });
    }

    // ==================== 执行频率（§三.9 三层） ====================

    /// <summary>生效的刷新方式：视图覆盖优先，其次集合。</summary>
    private string EffectiveMode()
    {
        var viewMode = ScriptRefreshMode.NormalizeOverride(_view?.RefreshMode);
        if (viewMode.Length > 0) return viewMode;

        var col = CurrentCollection();
        return ScriptRefreshMode.Normalize(col?.RefreshMode);
    }

    /// <summary>生效的刷新间隔（秒）：视图覆盖优先，其次集合，最小 2 秒。</summary>
    private int EffectiveSeconds()
    {
        var viewSeconds = _view?.RefreshSeconds ?? 0;
        var col = CurrentCollection();
        var seconds = viewSeconds > 0 ? viewSeconds : col?.RefreshSeconds ?? 0;
        return Math.Max(seconds, CollectorService.MinIntervalSeconds);
    }

    private ScriptCollection? CurrentCollection()
        => _store?.FindCollection(SelectedCollection?.Id ?? "");

    /// <summary>页面进入可视树时调（切走即停表，§三.9 硬约束 4）。</summary>
    public void StartAutoRefresh()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        if (EffectiveMode() != ScriptRefreshMode.Manual) _ = RefreshAllCoreAsync(autoOnly: true);
    }

    /// <summary>页面离开可视树时调（暂停定时器；在跑的采集不强杀）。</summary>
    public void StopAutoRefresh()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e) => _ = TickAsync();

    /// <summary>1s tick：只有 <c>timer</c> 模式才动，到期后跑「未折叠 + 自动白名单」的节。</summary>
    private async Task TickAsync()
    {
        if (IsBusy || Sections.Count == 0) return;
        if (EffectiveMode() != ScriptRefreshMode.Timer) return;

        var seconds = EffectiveSeconds();
        if (_lastBatchAt != DateTime.MinValue && (DateTime.UtcNow - _lastBatchAt).TotalSeconds < seconds) return;

        await RefreshAllCoreAsync(autoOnly: true).ConfigureAwait(true);
    }

    private DateTime _lastBatchAt = DateTime.MinValue;

    private void UpdateFooter(long elapsedMs = -1)
    {
        if (elapsedMs >= 0) _lastBatchAt = DateTime.UtcNow;

        var rows = Sections.Sum(s => s.AllRows.Count);
        if (Sections.Count == 0) { FooterSummary = "分节模式 · 无采集器"; return; }

        var prefix = elapsedMs >= 0 ? "本次 " + InfoRowFactory.DurationText(elapsedMs) : "上次结果";
        FooterSummary = $"{prefix} · {Sections.Count} 节 · {rows} 行（分节模式不分页）";
    }

    // ==================== 空态 ====================

    private void UpdateEmpty()
    {
        if (Collections.Count == 0)
        {
            EmptyMessage = "还没有集合。集合 = 一组采集器，信息面板一次显示一个集合。";
            EmptyActionText = "🗂 去创建集合";
        }
        else if (Sections.Count == 0)
        {
            EmptyMessage = "这个集合还没有采集器。到「集合」页勾选条目，或在脚本库「🔍 扫描目录」入库。";
            EmptyActionText = "🗂 去添加";
        }
        else
        {
            EmptyMessage = "";
            EmptyActionText = "";
        }

        OnPropertyChanged(nameof(HasSections));
        OnPropertyChanged(nameof(HasEmptyAction));
    }

    /// <summary>空态按钮：都跳到「集合」页（唯一的成员编排入口）。</summary>
    [RelayCommand]
    private void OpenEmptyAction()
    {
        if (_nav == null) { StatusText = "⚠ 导航服务不可用"; return; }
        _nav.NavigateTo("script-collections");
    }

    // ==================== 节设置（解析器 / 超时 / 保留 / 自动刷新） ====================

    public ObservableCollection<InfoOption> ParserOptions { get; } = new(new[]
    {
        new InfoOption(ScriptParseMode.Json, "json（推荐）"),
        new InfoOption(ScriptParseMode.Line, "line（每行一个值）"),
        new InfoOption(ScriptParseMode.Csv, "csv（首行是属性名）"),
        new InfoOption(ScriptParseMode.Regex, "regex（本期等同 line）"),
    });

    public ObservableCollection<InfoOption> RetentionOptions { get; } = new(new[]
    {
        new InfoOption(ScriptRetentionMode.Latest, "latest（只留最新一次）"),
        new InfoOption(ScriptRetentionMode.Count, "count（保留最近 N 次）"),
        new InfoOption(ScriptRetentionMode.Days, "days（保留最近 N 天）"),
    });

    public ObservableCollection<InfoOption> CollectionRefreshOptions { get; } = new(new[]
    {
        new InfoOption(ScriptRefreshMode.Manual, "手动"),
        new InfoOption(ScriptRefreshMode.OnEnter, "进页面刷新"),
        new InfoOption(ScriptRefreshMode.Timer, "定时刷新"),
    });

    [ObservableProperty] private InfoOption? _settingsParser;
    [ObservableProperty] private string _settingsTimeout = "30";
    [ObservableProperty] private InfoOption? _settingsRetention;
    [ObservableProperty] private string _settingsRetentionN = "0";
    [ObservableProperty] private bool _settingsAuto;
    [ObservableProperty] private string _settingsInterval = "0";

    /// <summary>集合级刷新方式（下拉，改动即落盘）。</summary>
    [ObservableProperty] private InfoOption? _collectionRefresh;
    [ObservableProperty] private string _collectionRefreshSeconds = "0";

    /// <summary>只读条目才允许自动刷新（硬约束 1），否则下拉灰掉并给原因。</summary>
    public bool CanAuto => SettingsSection?.Item.Readonly == true;

    public string AutoHint => CanAuto
        ? "只有只读采集器能自动刷新（有副作用的条目不排期）。间隔 2~3600 秒。"
        : "本采集器不是只读条目 ⇒ 自动刷新不可用。";

    /// <summary>保留策略提示：latest 不支持趋势（§三.9 硬约束 6）。</summary>
    public string RetentionHint => SettingsSection?.Item.Readonly == true
        ? "只读采集器强制 latest（不保留历史 ⇒ 不提供趋势）。"
        : "latest = 只留最新一批；count / days 才留历史（本期不做趋势图，供后续使用）。";

    private void LoadSectionSettings()
    {
        var item = SettingsSection?.Item;
        if (item == null) return;

        SettingsParser = ParserOptions.FirstOrDefault(o => o.Value == ScriptParseMode.Normalize(item.ParseMode));
        SettingsTimeout = (item.Timeout <= 0 ? 30 : item.Timeout).ToString();
        SettingsRetention = RetentionOptions.FirstOrDefault(o => o.Value == ScriptRetentionMode.Normalize(item.RetentionMode));
        SettingsRetentionN = Math.Max(0, item.RetentionN).ToString();
        SettingsAuto = item.Auto;
        SettingsInterval = Math.Max(0, item.IntervalSeconds).ToString();

        var col = CurrentCollection();
        CollectionRefresh = CollectionRefreshOptions.FirstOrDefault(o => o.Value == ScriptRefreshMode.Normalize(col?.RefreshMode));
        CollectionRefreshSeconds = Math.Max(0, col?.RefreshSeconds ?? 0).ToString();

        OnPropertyChanged(nameof(CanAuto));
        OnPropertyChanged(nameof(AutoHint));
        OnPropertyChanged(nameof(RetentionHint));
    }

    private void OpenSettingsFor(InfoSectionViewModel section)
    {
        SettingsSection = section;
        IsSectionSettingsOpen = true;
    }

    /// <summary>⚙ 节设置：展开 / 收起（收起时保留上次的节）。</summary>
    [RelayCommand]
    private void ToggleSectionSettings()
    {
        if (!IsSectionSettingsOpen && SettingsSection == null)
            SettingsSection = Sections.FirstOrDefault();

        IsSectionSettingsOpen = !IsSectionSettingsOpen;
    }

    /// <summary>保存节设置 + 集合刷新策略。</summary>
    [RelayCommand]
    private void SaveSectionSettings()
    {
        var item = SettingsSection?.Item;
        if (item == null || _store == null) return;

        item.ParseMode = ScriptParseMode.Normalize(SettingsParser?.Value);
        item.Timeout = int.TryParse((SettingsTimeout ?? "").Trim(), out var t) && t > 0 ? Math.Min(t, 300) : 30;

        // 只读条目强制 latest（硬约束）
        item.RetentionMode = item.Readonly
            ? ScriptRetentionMode.Latest
            : ScriptRetentionMode.Normalize(SettingsRetention?.Value);
        item.RetentionN = item.RetentionMode == ScriptRetentionMode.Latest
            ? 0
            : Math.Max(0, int.TryParse((SettingsRetentionN ?? "").Trim(), out var n) ? n : 0);

        item.Auto = CollectorService.CanAutoRefresh(item) && SettingsAuto;
        item.IntervalSeconds = CollectorService.ClampInterval(
            int.TryParse((SettingsInterval ?? "").Trim(), out var i) ? i : 0);

        var col = CurrentCollection();
        if (col != null)
        {
            col.RefreshMode = ScriptRefreshMode.Normalize(CollectionRefresh?.Value);
            col.RefreshSeconds = Math.Max(0, int.TryParse((CollectionRefreshSeconds ?? "").Trim(), out var s) ? s : 0);
        }

        _store.Save();
        LoadSectionSettings();
        StatusText = $"已保存「{SettingsSection?.Title}」的采集设置。";
    }

    // ==================== 操作 ====================

    /// <summary>📋 复制选中行的「属性 = 值」。</summary>
    [RelayCommand]
    private async Task CopySelectedRowAsync()
    {
        if (SelectedRow is not { } row) { StatusText = "先在节里选中一行。"; return; }
        await CopyTextAsync(RowText(row));
    }

    /// <summary>📋 复制全部（全部节，制表符分隔）。</summary>
    [RelayCommand]
    private async Task CopyAllAsync()
    {
        var sb = new StringBuilder();
        foreach (var s in Sections)
        {
            if (s.AllRows.Count == 0) continue;

            sb.AppendLine("## " + s.Title);
            sb.AppendLine(string.Join('\t', s.HeaderCells.Select(h => h.Title)));
            foreach (var r in s.AllRows)
                sb.AppendLine(string.Join('\t', r.Cells.Select(c => c.Text)));
            sb.AppendLine();
        }

        if (sb.Length == 0) { StatusText = "还没有数据。"; return; }
        await CopyTextAsync(sb.ToString());
    }

    private static string RowText(SysInfoRow row)
        => string.Join(Environment.NewLine, row.Cells.Where(c => c.IsVisible).Select(c => $"{c.Title} = {c.Text}"));

    private async Task CopyTextAsync(string text)
    {
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制到剪贴板。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>✏ 打开采集器脚本（命令型条目没有文件）。</summary>
    [RelayCommand]
    private void EditSectionScript(InfoSectionViewModel? section)
    {
        if (section == null) return;
        if (CollectorService.IsCommandItem(section.Item)) { StatusText = "这是命令行采集器，没有脚本文件。"; return; }

        var path = CollectableEvaluator.ResolveScriptPath(section.Item.Target, _settings?.EffectiveScriptsFolder ?? "");
        if (path.Length == 0 || !File.Exists(path)) { StatusText = "⚠ 找不到脚本文件：" + section.Item.Target; return; }
        OpenWithShell(path, "脚本");
    }

    /// <summary>📂 打开脚本目录。</summary>
    [RelayCommand]
    private void OpenScriptDir()
    {
        try
        {
            var dir = _settings?.EffectiveScriptsFolder ?? "";
            if (dir.Length == 0) { StatusText = "⚠ 脚本目录不可用"; return; }
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            StatusText = "已打开脚本目录：" + dir;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    private void OpenWithShell(string path, string what)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusText = $"已打开{what}：{path}";
        }
        catch (Exception ex) { StatusText = $"打开{what}失败：" + ex.Message; }
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
