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
        // 工具条里⛔ 不再放只读文字标签（「集合」「视图」）—— 一律用**悬浮提示**说明这一项是什么。
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
            Text = "🔄 刷新", Classes = "ghost",
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

}
