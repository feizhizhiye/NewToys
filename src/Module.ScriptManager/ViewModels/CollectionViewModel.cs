using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 「脚本管理器 → 集合」页（§三.7）：<b>主从两栏</b> —— 左 280px 集合列表 + 右成员表；
/// 详情卡走标准右侧声明式卡（<see cref="IDetailCardSource"/>，与全应用一致）。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>成员关系存在条目侧</b>（<see cref="ScriptItem.Collections"/>）：成员表是「全库清单 + 勾选」——
/// 勾 = 在集合内、取消勾 = 移出，<b>即时生效、无保存按钮</b>。⛔ 集合侧不存成员 id 列表。
/// </para>
/// <para>
/// ⛔ 本期不做层级分组（几十个集合靠「搜索 + 分组下拉 + 类型 / 标签筛选」扛，卡 §三.7）。
/// </para>
/// </remarks>
public partial class CollectionViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly IScriptStoreService? _store;
    private readonly IQuickActionService? _svc;
    private readonly INavigationService? _nav;
    private readonly IScriptManagerSettingsService? _settings;

    /// <summary>批量改属性时压住「回写 + 落盘」（初始化 / 切换集合时）。</summary>
    private bool _loading;

    /// <summary>压住成员勾选的联动（重建成员表时）。</summary>
    private bool _suppressMember;

    public CollectionViewModel() { }

    public CollectionViewModel(IScriptStoreService store, IQuickActionService svc, INavigationService nav,
        IScriptManagerSettingsService settings)
    {
        _store = store;
        _svc = svc;
        _nav = nav;
        _settings = settings;

        // 🔴 两个筛选框的默认值 = 「全部」：直接写字段（⛔ 走属性会各触发一次 RefreshCollections）。
        _selectedGroup = GroupOptions[0];
        _selectedKindFilter = KindFilterOptions[0];

        Members.CollectionChanged += OnMembersChanged;
        RefreshCollections();
    }

    // ==================== 集合列表（工具条下拉） ====================

    public ObservableCollection<CollectionRow> Collections { get; } = new();

    [ObservableProperty] private CollectionRow? _selectedCollection;

    /// <summary>分组：全部 / 置顶 / 最近使用。</summary>
    public ObservableCollection<CollectionFilterOption> GroupOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "全部", Value = "" },
        new CollectionFilterOption { Label = "置顶", Value = "pinned" },
        new CollectionFilterOption { Label = "最近使用", Value = "recent" },
    });

    [ObservableProperty] private CollectionFilterOption? _selectedGroup;

    /// <summary>类别筛选（集合类别）。v3.81：加回「软件包」（包清单，发送到软件包页显示）。</summary>
    public ObservableCollection<CollectionFilterOption> KindFilterOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "全部类型", Value = "" },
        new CollectionFilterOption { Label = "信息面板", Value = ScriptCollectionKind.Info },
        new CollectionFilterOption { Label = "脚本库", Value = ScriptCollectionKind.Script },
        new CollectionFilterOption { Label = "命令库", Value = ScriptCollectionKind.Command },
        new CollectionFilterOption { Label = "软件包", Value = ScriptCollectionKind.Package },
        new CollectionFilterOption { Label = "抓取集合", Value = ScriptCollectionKind.Crawl },
    });

    [ObservableProperty] private CollectionFilterOption? _selectedKindFilter;

    partial void OnSelectedGroupChanged(CollectionFilterOption? value) => RefreshCollections();
    partial void OnSelectedKindFilterChanged(CollectionFilterOption? value) => RefreshCollections();

    partial void OnSelectedCollectionChanged(CollectionRow? value)
    {
        LoadMemberTable();
        LoadDetailEdits();
        OnPropertyChanged(nameof(SendToPageMenuText));
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 右键菜单「📤 发送到{目标页}」的动态文案（微调）：按选中集合的类别实时变
    /// （信息面板 / 脚本库 / 命令库 / 软件包）；未选中时落信息面板（点击后命令会提示先选集合）。
    /// </summary>
    public string SendToPageMenuText
    {
        get
        {
            var kind = SelectedCollection == null
                ? ScriptCollectionKind.Info
                : ScriptCollectionKind.Normalize(SelectedCollection.Kind);
            return $"📤 发送到{ScriptCollectionKind.Label(kind)}";
        }
    }

    // ==================== 成员表 ====================

    public ObservableCollection<MemberRow> Members { get; } = new();

    /// <summary>成员搜索。</summary>
    [ObservableProperty] private string _memberSearch = "";

    /// <summary>
    /// 「显示不可采集」（卡 §三.5）：默认只列可采集项；打开后不可采集项灰显、⛔ 勾不上。
    /// </summary>
    [ObservableProperty] private bool _showNonCollectable;

    /// <summary>
    /// 「显示全部类型」（D13）：默认候选池按当前集合的 kind 过滤；
    /// 打开后可把别的 kind 的条目也勾进来（信息面板集合默认就允许混）。
    /// </summary>
    [ObservableProperty] private bool _showAllKinds;

    partial void OnMemberSearchChanged(string value) => LoadMemberTable();
    partial void OnShowNonCollectableChanged(bool value) => LoadMemberTable();
    partial void OnShowAllKindsChanged(bool value) => LoadMemberTable();

    public bool HasMembers => Members.Count > 0;

    // ==================== 详情卡编辑缓冲 ====================

    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editIcon = "";
    [ObservableProperty] private string _editKind = ScriptCollectionKind.Info;
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editTags = "";
    [ObservableProperty] private string _editRefreshMode = ScriptRefreshMode.OnEnter;
    [ObservableProperty] private string _editRefreshSeconds = "";

    /// <summary>库的脚本根目录（只对 kind=script 有意义，空 = 全局脚本目录）。</summary>
    [ObservableProperty] private string _editRootPath = "";

    /// <summary>★ 置顶（集合下拉后缀 ★ / 信息面板集合下拉排最前）。</summary>
    [ObservableProperty] private bool _editPinned;

    /// <summary>
    /// 默认视图 id（详情卡「默认视图」下拉的**值通道**）。
    /// <para>🔴 下拉别再绑「选项对象」：`CollectionFilterOption` 没有 == 语义，详情卡按 `ToString()` 比对 ⇒
    /// 所有选项长得一样，永远命中第一项（v3.77 修的「类别不自动读取」就是这个根因）⇒ 一律走**字符串值**。</para>
    /// </summary>
    [ObservableProperty] private string _editDefaultViewId = "";

    public ObservableCollection<CollectionFilterOption> KindOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "信息面板", Value = ScriptCollectionKind.Info },
        new CollectionFilterOption { Label = "脚本库", Value = ScriptCollectionKind.Script },
        new CollectionFilterOption { Label = "命令库", Value = ScriptCollectionKind.Command },
        new CollectionFilterOption { Label = "软件包", Value = ScriptCollectionKind.Package },
        new CollectionFilterOption { Label = "抓取集合", Value = ScriptCollectionKind.Crawl },
    });

    public ObservableCollection<CollectionFilterOption> ViewOptions { get; } = new()
    {
        new CollectionFilterOption { Label = "默认视图", Value = "" },
    };


    public ObservableCollection<CollectionFilterOption> RefreshOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "进页面刷新", Value = ScriptRefreshMode.OnEnter },
        new CollectionFilterOption { Label = "只手动刷新", Value = ScriptRefreshMode.Manual },
        new CollectionFilterOption { Label = "定时刷新", Value = ScriptRefreshMode.Timer },
    });

    partial void OnEditKindChanged(string value) => ApplyCollectionEdits();
    partial void OnEditRefreshModeChanged(string value) => ApplyCollectionEdits();
    partial void OnEditDefaultViewIdChanged(string value) => ApplyCollectionEdits();

    // 🔴 卡里的 OnBlur 字段只把值写回 VM，**落盘要在这里接**（否则改名 / 改描述只在内存里）。
    partial void OnEditNameChanged(string value) => ApplyCollectionEdits();
    partial void OnEditIconChanged(string value) => ApplyCollectionEdits();
    partial void OnEditDescriptionChanged(string value) => ApplyCollectionEdits();
    partial void OnEditTagsChanged(string value) => ApplyCollectionEdits();
    partial void OnEditRefreshSecondsChanged(string value) => ApplyCollectionEdits();
    partial void OnEditRootPathChanged(string value) => ApplyCollectionEdits();
    partial void OnEditPinnedChanged(bool value) => ApplyCollectionEdits();

    // ==================== 列表基础设施（ListPageViewModelBase 抽象成员） ====================

    protected override string PageKey => "script-collections";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("State", "状态", "0.9*", sortable: false),
        new("Title", "名称", "3*", sample: "磁盘使用"),
        new("Role", "类型", "1*", sample: "脚本"),
        new("Target", "目标", "4*", sortable: false, sample: "sysinfo/disk.ps1"),
    };

    public bool IsStateVisible => true;
    public bool IsTitleVisible => true;
    public bool IsRoleVisible => true;
    public bool IsTargetVisible => true;

    protected override void RefreshView() => RefreshCollections();

    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not MemberRow m) return base.FieldText(row, columnKey);
        return columnKey switch
        {
            "State" => m.StateText,
            "Title" => m.Title,
            "Role" => m.RoleText,
            "Target" => m.Target,
            _ => base.FieldText(row, columnKey),
        };
    }

    // ==================== 详情卡（标准声明式） ====================

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

}
