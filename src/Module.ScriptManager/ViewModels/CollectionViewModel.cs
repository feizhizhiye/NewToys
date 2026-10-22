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
/// 「脚本管理器 → 集合」页（NT-068 §三.7）：<b>主从两栏</b> —— 左 280px 集合列表 + 右成员表；
/// 详情卡走标准右侧声明式卡（<see cref="IDetailCardSource"/>，与全应用一致）。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>成员关系存在条目侧</b>（<see cref="ScriptItem.Collections"/>）：右栏是「全库清单 + 勾选」——
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

    // ==================== 左栏：集合列表 ====================

    public ObservableCollection<CollectionRow> Collections { get; } = new();

    [ObservableProperty] private CollectionRow? _selectedCollection;

    /// <summary>左栏搜索（名称 / 描述 / 标签）。</summary>
    [ObservableProperty] private string _collectionSearch = "";

    /// <summary>左栏分组：全部 / 置顶 / 最近使用。</summary>
    public ObservableCollection<CollectionFilterOption> GroupOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "全部", Value = "" },
        new CollectionFilterOption { Label = "置顶", Value = "pinned" },
        new CollectionFilterOption { Label = "最近使用", Value = "recent" },
    });

    [ObservableProperty] private CollectionFilterOption? _selectedGroup;

    /// <summary>左栏类型筛选（集合类别）。v3.81：加回「软件包」（包清单，发送到软件包页显示）。</summary>
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

    public bool HasCollections => Collections.Count > 0;

    partial void OnCollectionSearchChanged(string value) => RefreshCollections();
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
    /// 右键菜单「📤 发送到{目标页}」的动态文案（NT-082 微调）：按选中集合的类别实时变
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

    // ==================== 右栏：成员表 ====================

    public ObservableCollection<MemberRow> Members { get; } = new();

    /// <summary>成员搜索。</summary>
    [ObservableProperty] private string _memberSearch = "";

    /// <summary>
    /// 「显示不可采集」（卡 §三.5）：默认只列可采集项；打开后不可采集项灰显、⛔ 勾不上。
    /// </summary>
    [ObservableProperty] private bool _showNonCollectable;

    /// <summary>
    /// 「显示全部类型」（NT-073 D13）：默认候选池按当前集合的 kind 过滤；
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

    /// <summary>库的脚本根目录（NT-073；只对 kind=script 有意义，空 = 全局脚本目录）。</summary>
    [ObservableProperty] private string _editRootPath = "";

    /// <summary>★ 置顶（左栏五角星 / 信息面板集合下拉排最前）。</summary>
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
        new("Member", "加入", "0.7*", sortable: false),
        new("Title", "名称", "3*", sample: "磁盘使用"),
        new("Role", "类型", "1*", sample: "脚本"),
        new("Target", "目标", "4*", sortable: false, sample: "sysinfo/disk.ps1"),
    };

    public bool IsMemberVisible => true;
    public bool IsTitleVisible => true;
    public bool IsRoleVisible => true;
    public bool IsTargetVisible => true;

    protected override void RefreshView() => RefreshCollections();

    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not MemberRow m) return base.FieldText(row, columnKey);
        return columnKey switch
        {
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

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(HasSelectedCollection),
        EmptyText = "在左侧选中一个集合，可在这里改名称 / 图标 / 类别 / 描述 / 刷新策略 / 标签。集合只是「一组采集器的容器」，本身不跑任何东西。",
        Sections =
        {
            new DetailSection
            {
                Header = "集合",
                Fields =
                {
                    new DetailField
                    {
                        Label = "名称", ValuePath = nameof(EditName), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "图标", ValuePath = nameof(EditIcon), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur, Placeholder = "一个 emoji 或字符"
                    },
                    new DetailField
                    {
                        Label = "类别", Kind = FieldKind.Select,
                        OptionsPath = nameof(KindOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        OptionValuePath = nameof(CollectionFilterOption.Value), ValuePath = nameof(EditKind),
                        Hint = "决定这个集合归哪个页面（信息面板 / 脚本库 / 命令库 / 软件包）；改内置库的类别后，下次启动会自动补回一个默认库"
                    },
                    new DetailField
                    {
                        Label = "置顶", ValuePath = nameof(EditPinned), Kind = FieldKind.Check,
                        Hint = "★ 标记：左栏分组下拉选「置顶」时可单独筛出，信息面板的集合下拉里也排最前"
                    },
                    new DetailField
                    {
                        Label = "描述", ValuePath = nameof(EditDescription), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur, MultiLine = true, Rows = 52
                    },
                    new DetailField
                    {
                        Label = "脚本根目录", ValuePath = nameof(EditRootPath), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        VisibleWhen = nameof(IsScriptCollection),
                        Placeholder = "留空 = 用设置里的全局脚本目录",
                        Hint = "这个库的脚本文件放哪；相对路径按它解析"
                    },
                    new DetailField
                    {
                        Label = "默认视图", Kind = FieldKind.Select,
                        OptionsPath = nameof(ViewOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        OptionValuePath = nameof(CollectionFilterOption.Value), ValuePath = nameof(EditDefaultViewId),
                        VisibleWhen = nameof(IsInfoCollection), EnabledWhen = nameof(IsInfoCollection)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint, ValuePath = nameof(ViewCountText),
                        VisibleWhen = nameof(IsInfoCollection)
                    },
                    new DetailField
                    {
                        Label = "刷新策略", Kind = FieldKind.Select,
                        OptionsPath = nameof(RefreshOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        OptionValuePath = nameof(CollectionFilterOption.Value), ValuePath = nameof(EditRefreshMode),
                        EnabledWhen = nameof(IsInfoCollection),
                        Hint = "只有信息面板集合会刷新（脚本库 / 命令库只是索引）"
                    },
                    new DetailField
                    {
                        Label = "刷新间隔(秒)", ValuePath = nameof(EditRefreshSeconds), Kind = FieldKind.Text,
                        EnabledWhen = nameof(IsInfoCollection),
                        Hint = "只对「定时」有效；0 = 用最小间隔"
                    },
                    new DetailField
                    {
                        Label = "标签（逗号分隔）", ValuePath = nameof(EditTags), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(CollectionHint) },
                }
            }
        }
    };

    public bool HasSelectedCollection => SelectedCollection != null;

    /// <summary>只有「信息面板」类集合才有节 / 视图（其它集合不显示默认视图与视图提示）。</summary>
    public bool IsInfoCollection => SelectedCollection == null
        || ScriptCollectionKind.Normalize(SelectedCollection.Kind) == ScriptCollectionKind.Info;

    /// <summary>当前是不是脚本库类集合（决定「脚本根目录」字段显隐）。</summary>
    public bool IsScriptCollection => SelectedCollection != null
        && ScriptCollectionKind.Normalize(SelectedCollection.Kind) == ScriptCollectionKind.Script;

    /// <summary>视图数 + 一句话解释（集合卡「视图」段提示）。</summary>
    public string ViewCountText
    {
        get
        {
            if (_store == null || SelectedCollection == null) return "";

            var n = _store.Data.Views.Count(v => v.CollectionId == SelectedCollection.Id);
            return n == 0
                ? "还没有视图（打开信息面板时自动建一个「默认」）。"
                : $"共 {n} 个视图 · 视图决定每节显示哪些列、节怎么排；同一个脚本在不同集合可以显示不同的列。";
        }
    }

    public string CollectionHint => SelectedCollection == null
        ? ""
        : $"成员 {SelectedCollection.MemberCount} 个"
          + (SelectedCollection.Builtin ? " · 默认库（兜底接收无归属条目）" : "")
          + (SelectedCollection.IsInUse ? " · 在用，不可删除（可改名 / 归档）" : " · 可删除")
          + $" · 落盘 {_store?.FilePath ?? ""}";

    // ==================== 工具条 ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "➕ 新建集合", Classes = "ghost", CommandPath = nameof(NewCollectionCommand), ToolTip = "新建一个空集合，然后在右栏勾选成员" },
        new() { Text = "⧉ 复制", Classes = "ghost", CommandPath = nameof(DuplicateCollectionCommand) },
        new() { Text = "🗑 删除", Classes = "danger-btn", CommandPath = nameof(DeleteCollectionCommand), ToolTip = "只有「在用」的集合不能删（默认库一直算在用）；其余可删" },
        new() { Text = "📦 归档", Classes = "ghost", CommandPath = nameof(ArchiveCollectionCommand), ToolTip = "归档后默认不显示，可从「全部」里找回" },
        new() { Text = "🔍 扫描目录", Classes = "ghost", CommandPath = nameof(ScanIntoCollectionCommand), ToolTip = "扫描脚本目录，把新脚本入库并加入当前集合" },
        new() { Text = "📤 发送到对应页面", Classes = "ghost", CommandPath = nameof(SendToPageCommand), ToolTip = "按集合类别跳到对应页面并选中这个集合（信息面板 / 脚本库 / 命令库 / 软件包）" },
        new() { Text = "📤 导出", Classes = "ghost", CommandPath = nameof(ExportCommand), ToolTip = "把集合定义导出为 json" },
        new()
        {
            Kind = ToolbarItemKind.TextField, Width = 96, Placeholder = "搜索成员…",
            ValuePath = nameof(MemberSearch)
        },
        new()
        {
            Kind = ToolbarItemKind.Toggle, Text = "显示不可采集",
            ValuePath = nameof(ShowNonCollectable),
            ToolTip = "默认只列可采集条目；打开后不可采集项灰显（⛔ 勾不上）"
        },
        new()
        {
            Kind = ToolbarItemKind.Toggle, Text = "显示全部类型",
            ValuePath = nameof(ShowAllKinds),
            ToolTip = "默认候选池按当前集合的类别过滤（脚本库集合只列脚本）；打开后可跨类勾选"
        },
    };

    // ==================== 数据加载 ====================

    private void RefreshCollections()
    {
        if (_store == null) return;
        _loading = true;

        var keepId = SelectedCollection?.Id;
        var keyword = (CollectionSearch ?? "").Trim();
        var kindFilter = SelectedKindFilter?.Value ?? "";
        var group = SelectedGroup?.Value ?? "";

        // NT-072 / NT-073：各页面（信息面板 / 脚本库 / 命令库）正在使用的集合 ⇒ 标「在用」；
        // 沉底只对 info 集合（库页高频切库，沉底反而难找）；默认库恒「在用」，不参与沉底（否则会挤掉置顶）。
        int InUseRank(ScriptCollection c)
            => !c.Builtin && ScriptCollectionKind.Normalize(c.Kind) == ScriptCollectionKind.Info && IsActive(c) ? 1 : 0;

        bool IsActive(ScriptCollection c)
        {
            // 默认库（内置）= 兜底库：没有归属的条目会自动收进它 ⇒ 永远算「在用」，也因此不可删除。
            if (c.Builtin) return true;

            var id = _store!.GetLastCollectionId(ScriptCollectionKind.Normalize(c.Kind));
            return id.Length > 0 && string.Equals(c.Id, id, StringComparison.Ordinal);
        }

        var rows = _store.Data.Collections
            .Where(c => kindFilter.Length == 0 || ScriptCollectionKind.Normalize(c.Kind) == kindFilter)
            .Where(c => keyword.Length == 0 || Matches(c, keyword))
            .Where(c => group != "pinned" || c.Pinned)
            .OrderBy(InUseRank)                                // 使用中的集合排最后（置顶 / 归档都优先不了）
            .ThenBy(c => c.Archived)                           // 归档的排最后（仍可见，方便取消归档）
            .ThenByDescending(c => c.Pinned)
            .ThenByDescending(c => SafeDate(c.LastOpenedAt))
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (group == "recent")
            rows = rows.OrderByDescending(c => SafeDate(c.LastOpenedAt)).ThenBy(InUseRank).ToList();

        Collections.Clear();
        foreach (var c in rows)
        {
            var row = new CollectionRow(c)
            {
                MemberCount = _store.ItemsOf(c.Id).Count,
                IsInUse = IsActive(c),
            };
            Collections.Add(row);
        }

        SelectedCollection = Collections.FirstOrDefault(r => r.Id == keepId) ?? Collections.FirstOrDefault();
        _loading = false;

        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(HasSelectedCollection));
        OnPropertyChanged(nameof(CollectionHint));
    }

    /// <summary>
    /// NT-072：页面重新显示时重算「在用」标记与沉底顺序。
    /// <para>信息面板切集合只会改 <c>lastCollectionId</c>，本页 VM 收不到通知 ⇒ 由 View 在挂上视觉树时调一次。</para>
    /// </summary>
    public void RefreshUsageState() => RefreshCollections();

    private static bool Matches(ScriptCollection c, string keyword)
        => (c.Name ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
           || (c.Description ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
           || (c.Tags ?? new List<string>()).Any(t => t.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));

    private static DateTime SafeDate(string? iso)
        => DateTime.TryParse(iso, out var dt) ? dt : DateTime.MinValue;

    /// <summary>重建右栏成员表：候选池（按集合 kind 过滤）+ 是否属于当前集合（NT-073 D13）。</summary>
    private void LoadMemberTable()
    {
        if (_store == null) return;

        _suppressMember = true;
        Members.Clear();

        var colId = SelectedCollection?.Id ?? "";
        var kind = SelectedCollection == null ? "" : ScriptCollectionKind.Normalize(SelectedCollection.Kind);
        var keyword = (MemberSearch ?? "").Trim();
        var isPackageCol = kind == ScriptCollectionKind.Package;

        foreach (var item in _store.Data.Items)
        {
            // 🔴 v3.81（NT-082）：软件包条目 ↔ 软件包集合是**一对一**的容器关系 ——
            // 包条目只在「软件包」类集合的候选池里出现，其他集合完全看不到（反之亦然）。
            var isPackageItem = ScriptItemRole.Normalize(item.Role) == ScriptItemRole.Package;
            if (isPackageItem != isPackageCol) continue;

            if (!ShowAllKinds && kind.Length > 0 && !RoleMatchesKind(item.Role, kind)) continue;
            if (!ShowNonCollectable && !item.Collectable) continue;
            if (keyword.Length > 0
                && !(item.Title ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                && !(item.Target ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)) continue;

            Members.Add(new MemberRow(item, item.Collections.Contains(colId)));
        }

        _suppressMember = false;
        OnPropertyChanged(nameof(HasMembers));
    }

    /// <summary>条目角色是否属于这个集合 kind 的候选域（信息面板集合不限）。</summary>
    private static bool RoleMatchesKind(string? role, string kind)
    {
        var r = ScriptItemRole.Normalize(role);
        return ScriptCollectionKind.Normalize(kind) switch
        {
            ScriptCollectionKind.Script => r is ScriptItemRole.Script or ScriptItemRole.Collector,
            ScriptCollectionKind.Command => r == ScriptItemRole.Command,
            ScriptCollectionKind.Package => r == ScriptItemRole.Package,
            ScriptCollectionKind.Crawl => r is ScriptItemRole.Script or ScriptItemRole.Collector,
            _ => true,   // 信息面板集合：脚本 + 命令都能当采集器
        };
    }

    /// <summary>给每一行挂上勾选联动（重建时统一挂，避免漏挂）。</summary>
    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (MemberRow r in e.OldItems) r.PropertyChanged -= OnMemberRowPropertyChanged;
        if (e.NewItems != null)
            foreach (MemberRow r in e.NewItems) r.PropertyChanged += OnMemberRowPropertyChanged;
    }

    private void OnMemberRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MemberRow.IsMember)) return;
        if (_suppressMember || _loading || sender is not MemberRow row) return;
        if (_store == null || SelectedCollection == null) return;

        if (!row.CanToggle)
        {
            // 不可采集的行 ⛔ 勾不上：把勾选状态翻回去。
            _suppressMember = true;
            row.IsMember = !row.IsMember;
            _suppressMember = false;
            StatusText = string.IsNullOrEmpty(row.Reason) ? "这条不可采集，不能加入集合。" : $"这条不可采集：{row.Reason}";
            return;
        }

        if (_store.SetMembership(row.ItemId, SelectedCollection.Id, row.IsMember))
        {
            _store.Save();
            var col = SelectedCollection;
            col.MemberCount = _store.ItemsOf(col.Id).Count;
            StatusText = row.IsMember
                ? $"已把「{row.Title}」加入「{col.Name}」"
                : $"已把「{row.Title}」移出「{col.Name}」";
        }
    }

    // ==================== 详情卡回写 ====================

    private void LoadDetailEdits()
    {
        _loading = true;
        var col = SelectedCollection == null || _store == null ? null : _store.FindCollection(SelectedCollection.Id);

        EditName = col?.Name ?? "";
        EditIcon = col?.Icon ?? "";
        EditKind = ScriptCollectionKind.Normalize(col?.Kind);
        EditDescription = col?.Description ?? "";
        EditTags = col == null ? "" : string.Join(", ", col.Tags);
        EditRefreshMode = ScriptRefreshMode.Normalize(col?.RefreshMode);
        EditRefreshSeconds = Math.Max(0, col?.RefreshSeconds ?? 0).ToString();
        EditRootPath = col?.RootPath ?? "";
        EditPinned = col?.Pinned ?? false;

        EditDefaultViewId = col?.DefaultViewId ?? "";

        ViewOptions.Clear();
        ViewOptions.Add(new CollectionFilterOption { Label = "默认视图", Value = "" });
        if (col != null)
            foreach (var v in _store?.Data.Views.Where(v => v.CollectionId == col.Id) ?? Enumerable.Empty<ScriptView>())
                ViewOptions.Add(new CollectionFilterOption { Label = v.Name, Value = v.Id });

        _loading = false;
        OnPropertyChanged(nameof(HasSelectedCollection));
        OnPropertyChanged(nameof(CollectionHint));
        OnPropertyChanged(nameof(IsInfoCollection));
        OnPropertyChanged(nameof(IsScriptCollection));
        OnPropertyChanged(nameof(ViewCountText));
    }

    private void ApplyCollectionEdits()
    {
        if (_loading || _store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        // 🔴 集合不允许同名（用户 2026-10-19）：撞名 ⇒ 不落盘，输入框退回原名并提示。
        var name = string.IsNullOrWhiteSpace(EditName) ? col.Name : EditName.Trim();
        if (NameTaken(name, col.Id))
        {
            StatusText = $"已存在同名集合「{name}」，名称未修改。";
            _loading = true;
            EditName = col.Name;
            _loading = false;
            return;
        }

        col.Name = name;
        col.Icon = EditIcon ?? "";
        // 类别可改（用户 2026-10-19）：内置库改走后，下次启动由 Normalize 补一个新默认库。
        col.Kind = ScriptCollectionKind.Normalize(EditKind);
        col.Description = EditDescription ?? "";
        col.Tags = (EditTags ?? "").Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        col.RefreshMode = ScriptRefreshMode.Normalize(EditRefreshMode);
        col.RefreshSeconds = int.TryParse((EditRefreshSeconds ?? "").Trim(), out var sec) && sec > 0 ? sec : 0;
        col.RootPath = (EditRootPath ?? "").Trim().TrimEnd('\\', '/');
        col.DefaultViewId = EditDefaultViewId ?? "";
        col.Pinned = EditPinned;

        _store.Save();

        // 左栏行同步（⛔ 不整表重建，免得丢选中）
        SelectedCollection.Name = col.Name;
        SelectedCollection.Kind = col.Kind;
        SelectedCollection.Icon = col.Icon;
        SelectedCollection.Description = col.Description;
        SelectedCollection.Pinned = col.Pinned;
        OnPropertyChanged(nameof(CollectionHint));
        OnPropertyChanged(nameof(IsInfoCollection));
        OnPropertyChanged(nameof(IsScriptCollection));
        OnPropertyChanged(nameof(ViewCountText));
    }

    /// <summary>集合名是否被别的集合占用（大小写 / 首尾空格不同也算同名）。</summary>
    private bool NameTaken(string name, string? exceptId = null)
        => _store != null && _store.Data.Collections.Any(c =>
            !string.Equals(c.Id, exceptId ?? "", StringComparison.Ordinal)
            && string.Equals((c.Name ?? "").Trim(), name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>取一个不重名的名字：`新集合` → `新集合 2` → `新集合 3`……</summary>
    private string UniqueName(string baseName)
    {
        if (!NameTaken(baseName)) return baseName;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!NameTaken(candidate)) return candidate;
        }
        return $"{baseName} {Guid.NewGuid().ToString("N")[..4]}";
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void NewCollection()
    {
        if (_store == null) return;
        var col = new ScriptCollection
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = UniqueName("新集合"),
            Kind = ScriptCollectionKind.Info,
            Icon = "🗂",
            Order = _store.Data.Collections.Count,
            RefreshMode = ScriptRefreshMode.OnEnter,
            LastOpenedAt = Now(),
        };
        _store.Data.Collections.Add(col);
        _store.Save();
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault(r => r.Id == col.Id);
        StatusText = "已新建集合，在右栏勾选成员（即时生效）。";
    }

    [RelayCommand]
    private void DuplicateCollection()
    {
        if (_store == null || SelectedCollection == null) return;
        var src = _store.FindCollection(SelectedCollection.Id);
        if (src == null) return;

        var col = new ScriptCollection
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = UniqueName(src.Name + " 副本"),
            Kind = src.Kind,
            Icon = src.Icon,
            Description = src.Description,
            Order = _store.Data.Collections.Count,
            Pinned = false,
            Builtin = false,
            Tags = new List<string>(src.Tags),
            RefreshMode = src.RefreshMode,
            RefreshSeconds = src.RefreshSeconds,
            RootPath = src.RootPath,
            LastOpenedAt = Now(),
        };
        _store.Data.Collections.Add(col);

        // 成员一并复制（条目侧加一条集合 id）
        foreach (var item in _store.Data.Items.Where(i => i.Collections.Contains(src.Id)))
            item.Collections.Add(col.Id);

        // NT-069：视图也要一起复制 —— 视图承载「每节显示哪些列 / 节顺序」，不复制副本就变成全默认。
        foreach (var srcView in _store.Data.Views.Where(v => v.CollectionId == src.Id).ToList())
        {
            var newView = new ScriptView
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                CollectionId = col.Id,
                Name = srcView.Name,
                Layout = srcView.Layout,
                Filter = srcView.Filter,
                Sort = srcView.Sort,
                Group = srcView.Group,
                RefreshMode = srcView.RefreshMode,
                RefreshSeconds = srcView.RefreshSeconds,
                Collapsed = new List<string>(srcView.Collapsed ?? new List<string>()),
                SectionOrder = new List<string>(srcView.SectionOrder ?? new List<string>()),
                SectionColumns = (srcView.SectionColumns ?? new List<SectionViewColumns>()).Select(s => new SectionViewColumns
                {
                    ItemId = s.ItemId,
                    Hidden = new List<string>(s.Hidden ?? new List<string>()),
                    Titles = s.Titles == null ? new Dictionary<string, string>() : new Dictionary<string, string>(s.Titles),
                    Widths = s.Widths == null ? new Dictionary<string, string>() : new Dictionary<string, string>(s.Widths),
                }).ToList(),
                Columns = (srcView.Columns ?? new List<ScriptColumn>()).Select(c => new ScriptColumn
                {
                    Key = c.Key, Title = c.Title, Visible = c.Visible, Order = c.Order, Width = c.Width,
                }).ToList(),
            };
            _store.Data.Views.Add(newView);

            if (string.Equals(src.DefaultViewId, srcView.Id, StringComparison.Ordinal))
                col.DefaultViewId = newView.Id;
        }

        _store.Save();
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault(r => r.Id == col.Id);
        StatusText = $"已复制为「{col.Name}」。";
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync()
    {
        if (_store == null || SelectedCollection == null) return;
        var row = SelectedCollection;
        var col = _store.FindCollection(row.Id);
        if (col == null) return;

        // 🔴 「在用」是唯一不可删的条件（用户 2026-10-19）：默认库恒在用 ⇒ 自然受保护，⛔ 不再按 Builtin 特殊拦。
        var kind = ScriptCollectionKind.Normalize(col.Kind);
        var inUse = col.Builtin
                    || string.Equals(_store.GetLastCollectionId(kind), col.Id, StringComparison.Ordinal);
        if (inUse)
        {
            StatusText = $"「{col.Name}」正在被{ScriptCollectionKind.Label(kind)}使用，不能删除；可以改名或归档。";
            return;
        }

        var message = $"确定删除集合「{col.Name}」吗？\n只删集合定义，⛔ 不删里面的条目。";
        var ok = await DialogHooks.ConfirmAsync(message);
        if (!ok) return;

        // 先清成员关系（条目侧），再删集合。
        foreach (var item in _store.Data.Items) item.Collections.RemoveAll(c => c == col.Id);
        _store.Data.Collections.RemoveAll(c => c.Id == col.Id);

        // NT-073 D8：只属于这个库的条目 ⇒ 收回同 kind 的默认库（⛔ 不删条目 / 文件）。
        var def = _store.Data.Collections.FirstOrDefault(c => c.Builtin && !c.Archived
                                                              && ScriptCollectionKind.Normalize(c.Kind) == kind);
        if (def != null)
            foreach (var orphan in _store.Data.Items.Where(i => i.Collections.Count == 0))
                orphan.Collections.Add(def.Id);

        if (string.Equals(_store.GetLastCollectionId(kind), col.Id, StringComparison.Ordinal))
            _store.SetLastCollectionId(kind, "");

        _store.Save();

        RefreshCollections();
        StatusText = $"已删除集合「{col.Name}」。";
    }

    [RelayCommand]
    private void ArchiveCollection()
    {
        if (_store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        col.Archived = !col.Archived;
        _store.Save();
        RefreshCollections();
        StatusText = col.Archived ? $"已归档「{col.Name}」。" : $"已取消归档「{col.Name}」。";
    }

    /// <summary>
    /// 「📤 发送到对应页面」：按集合类别决定目标页（信息面板 / 脚本库 / 命令库 / 软件包），
    /// 先记下「在用」集合再跳过去 —— 目标页构造时按 <c>LastCollectionIdByKind</c> 自动选中同一个集合。
    /// <para>v3.81（NT-082）取代原来只认信息面板的 <c>SendToInfoPanelCommand</c>。</para>
    /// </summary>
    [RelayCommand]
    private void SendToPage()
    {
        if (_store == null || SelectedCollection == null)
        {
            StatusText = "先在左栏选一个集合。";
            return;
        }

        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        var kind = ScriptCollectionKind.Normalize(col.Kind);
        var pageKey = ScriptCollectionKind.PageKey(kind);
        var pageName = ScriptCollectionKind.Label(kind);

        _store.SetLastCollectionId(kind, col.Id);
        col.LastOpenedAt = Now();
        _store.Save();

        if (_nav == null) { StatusText = "无法跳转（导航服务不可用）。"; return; }
        if (_nav.CurrentPageKey == pageKey)
        {
            StatusText = $"「{col.Name}」已在{pageName}中。";
            return;
        }

        _nav.NavigateTo(pageKey);
        StatusText = $"已发送「{col.Name}」到{pageName}。";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        var path = await DialogHooks.SaveFileAsync("导出集合", _store.ConfigDir, $"collection-{col.Name}.json", "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(col,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
            File.WriteAllText(path, json);
            StatusText = $"已导出到 {path}";
        }
        catch (Exception ex)
        {
            StatusText = "导出失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 🔍 扫描目录：把脚本目录里的新脚本入库，并**加入当前集合**。
    /// <para>本页的扫描是"补进这个集合"，所以不再走库页那套候选勾选。</para>
    /// </summary>
    [RelayCommand]
    private async Task ScanIntoCollectionAsync()
    {
        if (_store == null || SelectedCollection == null) { StatusText = "先选中一个集合。"; return; }

        // NT-073：优先从当前集合的根目录（脚本库）开始扫。
        var colRoot = _store.RootOf(SelectedCollection.Id);
        var dir = await DialogHooks.PickFolderAsync("扫描脚本目录（新脚本会入库并加入当前集合）",
            colRoot.Length > 0 ? colRoot : (_settings?.EffectiveScriptsFolder ?? _svc?.ScriptsDirectory ?? ""));
        if (string.IsNullOrWhiteSpace(dir)) return;

        var candidates = ScriptScanner.Scan(dir, true, _store.Data.Items)
                                     .Where(c => c.State == ScriptCandidate.StateNew)
                                     .ToList();
        if (candidates.Count == 0) { StatusText = "没有新脚本可加入。"; return; }

        var col = _store.FindCollection(SelectedCollection.Id);
        foreach (var c in candidates)
        {
            var item = new ScriptItem
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Role = ScriptItemRole.Script,
                Title = Path.GetFileNameWithoutExtension(c.FullPath),
                Target = c.RelativePath,
                Capture = true,
                Readonly = true,
                RetentionMode = ScriptRetentionMode.Latest,
                Collectable = true,
                CollectableSource = ScriptCollectableSource.Auto,
            };
            item.Collections.Add(SelectedCollection.Id);
            _store.Data.Items.Add(item);
            CollectableEvaluator.Apply(item, dir);
        }

        _store.SyncBuiltinCollections();
        _store.Save();
        _svc?.Reload();

        RefreshCollections();
        LoadMemberTable();
        StatusText = $"已加入 {candidates.Count} 个脚本到「{col?.Name}」（同时入库）。";
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
