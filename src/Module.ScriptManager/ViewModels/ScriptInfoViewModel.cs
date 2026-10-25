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
/// 「脚本管理器 → 信息面板」页（/S6；取代 的 manifest 驱动模型）。
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

    /// <summary>并发上限 4（沿 ）。</summary>
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
        // v3.77：工具条里⛔ 不再放只读文字标签（「集合」「视图」）—— 一律用**悬浮提示**说明这一项是什么。
        new()
        {
            Kind = ToolbarItemKind.Combo, Width = 240,
            ItemsPath = nameof(Collections),
            DisplayPath = nameof(InfoCollectionOption.Display),
            SelectedItemPath = nameof(SelectedCollection),
            ToolTip = "集合：一次显示一个（★置顶 → 最近使用 → 名称）",
        },
        new()
        {
            Kind = ToolbarItemKind.Combo, Width = 140,
            ItemsPath = nameof(Views),
            DisplayPath = nameof(InfoViewOption.Display),
            SelectedItemPath = nameof(SelectedView),
            IsEnabledPath = nameof(HasViews),
            ToolTip = "视图：同一集合的不同呈现（布局 / 折叠状态 / 刷新频率）",
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
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(SectionTitleText));
        OnPropertyChanged(nameof(SectionTargetText));
        OnPropertyChanged(nameof(SectionStateText));
        LoadSectionSettings();
        RebuildSectionColumnChips();
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedRowChanged(SysInfoRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
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
        if (SettingsSection == null) SettingsSection = Sections.FirstOrDefault();
    }

    // ==================== 详情卡（针对「节」，） ====================
    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;
    public event EventHandler? DetailChanged;

    /// <summary>有选中节才显示卡正文。⛔ 卡里**不放行数据**（行属于节体表格，别在这里重复一遍）。</summary>
    public bool ShowDetailBody => HasSettingsSection;

    public string SectionTitleText => SettingsSection?.Title ?? "";

    /// <summary>当前节要跑的东西（脚本路径 / 命令行）。</summary>
    public string SectionTargetText => SettingsSection?.Item.Target ?? "";

    /// <summary>节状态摘要：状态 · 上次采集 · 行数。</summary>
    public string SectionStateText
    {
        get
        {
            if (SettingsSection is not { } s) return "";
            var parts = new List<string> { s.StatusLabel };
            if (s.CapturedText.Length > 0) parts.Add(s.CapturedText);
            parts.Add(s.RowCountText);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>节的显示列 chips（点一下切显隐，只影响当前视图）。</summary>
    public ObservableCollection<SectionColumnChip> SectionColumnChips { get; } = new();

    public string SectionColumnsHint => SectionColumnChips.Count == 0
        ? "还没有列 —— 先刷新这一节。"
        : "点一下切换这一列在当前视图里的显隐；要改表头文字就改上面的「表头名称」（⛔ 都不改脚本本体、不影响其它集合）。";

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "点左边任意一节（或右键它）把这里切到那一节；右键「⚙ 显示这一节的详情 / 设置」可重新打开这张卡。",
        Sections =
        {
            new DetailSection
            {
                Header = "节",
                Fields =
                {
                    new DetailField { Label = "采集器", ValuePath = nameof(SectionTitleText), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "目标", ValuePath = nameof(SectionTargetText), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "状态", ValuePath = nameof(SectionStateText), Kind = FieldKind.Text, Selectable = true },
                }
            },
            new DetailSection
            {
                Header = "采集参数（所有集合共用）",
                Fields =
                {
                    new DetailField
                    {
                        Label = "解析器", Kind = FieldKind.Select,
                        OptionsPath = nameof(ParserOptions), OptionDisplayPath = nameof(InfoOption.Label),
                        ValuePath = nameof(SettingsParser)
                    },
                    new DetailField { Label = "超时(秒)", ValuePath = nameof(SettingsTimeout), Kind = FieldKind.Text },
                    new DetailField
                    {
                        Label = "保留", Kind = FieldKind.Select,
                        OptionsPath = nameof(RetentionOptions), OptionDisplayPath = nameof(InfoOption.Label),
                        ValuePath = nameof(SettingsRetention)
                    },
                    new DetailField
                    {
                        Label = "保留数量", ValuePath = nameof(SettingsRetentionN), Kind = FieldKind.Text,
                        Hint = "count = 保留次数；days = 保留天数"
                    },
                    new DetailField
                    {
                        Label = "自动刷新", ValuePath = nameof(SettingsAuto), Kind = FieldKind.Check,
                        Commit = EditCommitMode.Instant, EnabledWhen = nameof(CanAuto)
                    },
                    new DetailField
                    {
                        Label = "间隔(秒)", ValuePath = nameof(SettingsInterval), Kind = FieldKind.Text,
                        EnabledWhen = nameof(CanAuto), Hint = "2~3600；0 = 只手动"
                    },
                }
            },
            new DetailSection
            {
                Header = "本视图的显示",
                Fields =
                {
                    new DetailField
                    {
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(SectionColumnChips),
                        ChipTextPath = nameof(SectionColumnChip.Title),
                        ChipSelectedPath = nameof(SectionColumnChip.Visible),
                        ChipCommandPath = nameof(ToggleSectionColumnCommand)
                    },
                    new DetailField
                    {
                        Label = "表头名称",
                        Kind = FieldKind.EditableList,
                        OptionsPath = nameof(SectionColumnChips),
                        ItemLabelPath = nameof(SectionColumnChip.Key),
                        ItemTextPath = nameof(SectionColumnChip.Title),
                        ItemCommandPath = nameof(CommitColumnTitleCommand),
                        Hint = "改完点别处即生效；只改当前视图的表头，⛔ 不改脚本本体、不影响其它集合。"
                    },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(SectionColumnsHint) },
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "💾 保存参数",
                Classes = "primary",
                ToolTip = "把上面的采集参数与本视图的列宽写进 script.json",
                Command = SaveSectionSettingsCommand
            },
            new DetailAction
            {
                Text = "⬆ 上移", Classes = "ghost",
                ToolTip = "在当前视图里把这一节往前挪",
                Command = MoveSectionUpCommand
            },
            new DetailAction
            {
                Text = "⬇ 下移", Classes = "ghost",
                ToolTip = "在当前视图里把这一节往后挪",
                Command = MoveSectionDownCommand
            },
            new DetailAction
            {
                Text = "📂 脚本目录", Classes = "ghost",
                ToolTip = "打开脚本目录",
                Command = OpenScriptDirCommand
            }
        }
    };

    /// <summary>按当前节重建「显示列」chips（列标题取视图覆盖后的值）。</summary>
    private void RebuildSectionColumnChips()
    {
        SectionColumnChips.Clear();

        if (SettingsSection != null)
            foreach (var col in SettingsSection.Columns)
                SectionColumnChips.Add(new SectionColumnChip
                {
                    Key = col.Key,
                    Title = InfoRowFactory.Title(col),
                    Visible = col.Visible,
                });

        OnPropertyChanged(nameof(SectionColumnsHint));
    }

    /// <summary>
    /// 卡里「表头名称」失焦 ⇒ 把这一节的列标题写进当前视图并重投影本节（只影响当前视图）。
    /// </summary>
    [RelayCommand]
    private void CommitColumnTitle(SectionColumnChip? chip)
    {
        // 没有视图就没有「视图级覆盖」的落点（直接改会退回默认列显隐）⇒ 不处理
        if (chip == null || SettingsSection == null || _view == null) return;

        var titles = SectionColumnChips
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .Select(c => new KeyValuePair<string, string>(
                c.Key,
                string.Equals(c.Key, chip.Key, StringComparison.OrdinalIgnoreCase) ? chip.Title : c.Title));

        ViewColumnProjection.SetTitles(_view, SettingsSection.Item, titles);
        _store?.Save();

        SettingsSection.ApplyColumns(ViewColumnProjection.Resolve(SettingsSection.Item, _view));
        var applied = SettingsSection.Columns.FirstOrDefault(c => string.Equals(c.Key, chip.Key, StringComparison.OrdinalIgnoreCase));
        StatusText = applied == null
            ? "表头已更新（只影响当前视图）。"
            : $"表头「{chip.Key}」已改为「{InfoRowFactory.Title(applied)}」（只影响当前视图）。";
    }

    /// <summary>卡里点 chip ⇒ 切这一列在当前视图的显隐（写视图 + 即时重投影本节）。</summary>
    [RelayCommand]
    private void ToggleSectionColumn(SectionColumnChip? chip)
    {
        if (chip == null || SettingsSection == null) return;

        ViewColumnProjection.Toggle(SettingsSection.Item, _view, chip.Key);
        _store?.Save();

        SettingsSection.ApplyColumns(ViewColumnProjection.Resolve(SettingsSection.Item, _view));
        RebuildSectionColumnChips();
        // chip.Visible 是点击**前**的值 ⇒ 文案取反。
        StatusText = $"已{(chip.Visible ? "隐藏" : "显示")}列「{chip.Title}」（只影响当前视图）。";
    }

    /// <summary>
    /// 点节（节头 / 节点任意处）⇒ 把详情卡切到这一节；**不主动打开面板**（和列表页点行一个心智）。
    /// 需要打开面板时走右键菜单「⚙ 显示这一节的详情 / 设置」。
    /// </summary>
    public void SelectSection(InfoSectionViewModel? section)
    {
        if (section == null || ReferenceEquals(SettingsSection, section)) return;
        SettingsSection = section;
    }

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
                .Where(c => !c.Archived && ScriptCollectionKind.Normalize(c.Kind) == ScriptCollectionKind.Info)
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

        var last = _store?.GetLastCollectionId(ScriptCollectionKind.Info) ?? "";
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

        // 视图决定「每节显示哪些列 / 节怎么排」，切换后整页按新视图重投影。
        ApplyViewProjection();
    }

    /// <summary>载入一个集合：视图 + 节（先出缓存，再按刷新策略跑一次）。</summary>
    private void LoadCollection(InfoCollectionOption? option)
    {
        Sections.Clear();
        Views.Clear();
        _view = null;
        SelectedRow = null;
        // 切集合 ⇒ 之前的节已不在列表里，节设置面板必须清空（否则会对着已移除的节改参数）。
        SettingsSection = null;

        if (_store == null || option == null) { UpdateEmpty(); return; }

        var col = _store.FindCollection(option.Id);
        if (col == null) { UpdateEmpty(); return; }

        // ---- 视图（没有就建一个默认视图，保证折叠状态可落盘） ----
        var views = _store.Data.Views.Where(v => v.CollectionId == col.Id).ToList();
        if (views.Count == 0)
        {
            var created = new ScriptView { Id = "view-" + col.Id, CollectionId = col.Id, Name = "默认" };
            ViewColumnProjection.InitializeForCollection(created,
                _store.ItemsOf(col.Id).Where(i => i.Role != ScriptItemRole.Package));
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

        // ---- 节：集合成员里能采集的条目（先截断上限，再按视图的 sectionOrder 排序） ----
        var members = _store.ItemsOf(col.Id)
            .Where(i => i.Role != ScriptItemRole.Package && i.Capture && i.Collectable)
            .Take(MaxSections)
            .ToList();
        members = ViewColumnProjection.Order(members, _view);

        foreach (var item in members) Sections.Add(CreateSection(item));

        ApplyCollapsedFromView();
        LoadCachedRows();
        SelectedRow = Sections.FirstOrDefault()?.Selected;

        // 详情卡针对「节」⇒ 进页面先落在第一节，卡里立刻有内容（右键别的节可切）。
        SettingsSection = Sections.FirstOrDefault();

        // ---- 记住上次选中（重启恢复，验收 §五.7） ----
        col.LastOpenedAt = Now();
        _store.SetLastCollectionId(ScriptCollectionKind.Info, col.Id);
        _store.Save();

        OnPropertyChanged(nameof(HasViews));
        UpdateEmpty();
        UpdateFooter();

        // 刷新策略：manual 不动；on-enter / timer 进页面刷一次（只跑自动白名单）
        if (EffectiveMode() != ScriptRefreshMode.Manual) _ = RefreshAllCoreAsync(autoOnly: true);
    }

    private InfoSectionViewModel CreateSection(ScriptItem item)
    {
        var section = new InfoSectionViewModel(item, ViewColumnProjection.Resolve(item, _view))
        {
            RefreshRequested = s => _ = RefreshSectionsAsync(new[] { s }),
            DetailRequested = OpenSettingsFor,
            CollapseChanged = PersistCollapsed,
            ColumnToggled = (s, col) =>
            {
                // 显隐记在**当前视图**，不再回写脚本 schema（否则一个集合切列会连累全部集合）。
                ViewColumnProjection.Toggle(s.Item, _view, col.Key);
                _store?.Save();
            },
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

    /// <summary>按当前视图重投影全部节：列（显隐 / 标题 / 宽度）+ 折叠 + 顺序。</summary>
    private void ApplyViewProjection()
    {
        foreach (var s in Sections)
            s.ApplyColumns(ViewColumnProjection.Resolve(s.Item, _view));

        ApplyCollapsedFromView();
        ReorderSections();
        RebuildSectionColumnChips();
    }

    /// <summary>按 <see cref="ScriptView.SectionOrder"/> 就地重排 <see cref="Sections"/>（不重建节 ⇒ 数据 / 选中不丢）。</summary>
    private void ReorderSections()
    {
        if (Sections.Count < 2) return;

        var ordered = ViewColumnProjection.Order(Sections.Select(s => s.Item), _view);
        var desired = new List<InfoSectionViewModel>(ordered.Count);
        foreach (var item in ordered)
        {
            var section = Sections.FirstOrDefault(s => s.ItemId == item.Id);
            if (section != null) desired.Add(section);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var at = Sections.IndexOf(desired[i]);
            if (at >= 0 && at != i) Sections.Move(at, i);
        }
    }

    /// <summary>⬆ 把当前「节设置」对应的节往前挪一位。</summary>
    [RelayCommand]
    private void MoveSectionUp() => MoveSection(-1);

    /// <summary>⬇ 往后挪一位。</summary>
    [RelayCommand]
    private void MoveSectionDown() => MoveSection(1);

    private void MoveSection(int delta)
    {
        if (_view == null || SettingsSection == null) return;

        if (!ViewColumnProjection.Move(_view, Sections.Select(s => s.Item), SettingsSection.ItemId, delta))
        {
            StatusText = "已经到头了。";
            return;
        }

        _store?.Save();
        ReorderSections();
        StatusText = "已调整节的顺序（只影响当前视图）。";
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
                    // 列 schema 变了（脚本吐出新的 key）⇒ 重新投影本节，让新列按视图规则出现。
                    if (outcome.ColumnsChanged)
                        section.ApplyColumns(ViewColumnProjection.Resolve(section.Item, _view));
                    else
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
        OpenDetailPanelCommand.Execute(null);
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

        // 列宽属于「显示方式」⇒ 记在当前视图（脚本 schema 只留默认值）。
        if (_view != null && SettingsSection != null)
        {
            ViewColumnProjection.SetWidths(_view, SettingsSection.Item,
                SettingsSection.HeaderCells.Select(h => new KeyValuePair<string, string>(h.Attr, h.Width)));
        }

        _store.Save();

        // 宽度改变要重建列定义才生效（ApplyColumns 保留 RowKey / 选中）。
        if (SettingsSection != null)
            SettingsSection.ApplyColumns(ViewColumnProjection.Resolve(SettingsSection.Item, _view));

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
