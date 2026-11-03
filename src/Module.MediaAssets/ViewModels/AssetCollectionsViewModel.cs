using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
/// 「集合管理」页：与标签 / 分类页同构的**列表页**（名称 / 类型 / 成员数 / 创建时间）+ 详情卡。
/// <para>成员顺序 = <c>collection_assets.sort_order</c>：详情卡里点一行选中成员，再用「⬆ 上移 / ⬇ 下移」写库
/// （⛔ 未做鼠标拖放）。智能集合的成员由规则决定，⛔ 不许手工增删 / 排序。</para>
/// <para>⚠️ 智能集合（`rule_json`）本轮只区分类型 / 图标与存规则文本，**规则求值未实装**。</para>
/// </summary>
public partial class AssetCollectionsViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly CollectionService _cols = null!;
    private readonly AssetService _assets = null!;
    private readonly OperationLogService _log = null!;
    private readonly AssetNavigationStore _store = null!;
    private readonly INavigationService? _nav;

    private readonly List<CollectionRow> _all = new();
    private readonly List<AssetRow> _allRows = new();

    private bool _loadingDetail;

    public ObservableCollection<CollectionRow> Items { get; } = new();

    /// <summary>详情卡里的成员（按 sort_order；点一下选中，供上移 / 下移 / 移出）。</summary>
    public ObservableCollection<AssetMember> DetailMembers { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private CollectionRow? _selected;
    [ObservableProperty] private AssetMember? _selectedMember;
    [ObservableProperty] private string _searchText = "";

    // ===== 详情卡编辑字段 =====
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editDescription = "";

    // ===== 表头 =====
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _typeHeader = "类型";
    [ObservableProperty] private string _membersHeader = "成员数";
    [ObservableProperty] private string _createdHeader = "创建时间";

    public bool IsNameVisible => Vis("Name");
    public bool IsTypeVisible => Vis("Type");
    public bool IsMembersVisible => Vis("Members");
    public bool IsCreatedVisible => Vis("Created");

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "➕ 新建集合",
            Classes = "ghost",
            CommandPath = nameof(NewCollectionCommand),
            ToolTip = "新建一个手动集合（成员自己往里加、可排顺序）"
        },
        new()
        {
            Text = "➕ 智能集合",
            CommandPath = nameof(NewSmartCommand),
            ToolTip = "新建一个智能集合（本轮只存规则文本，规则求值未实装）"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            Placeholder = "搜索集合名 / 说明…",
            ValuePath = nameof(SearchText)
        },
        new()
        {
            Text = "🔄 重读集合",
            CommandPath = nameof(RefreshCollectionsCommand),
            ToolTip = "重新读取集合表（⛔ 不扫盘）"
        },
    };

    protected override string PageKey => "assets-collections";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2.4*", sample: "2026 科幻"},
        new("Type", "类型", "0.8*", sample: "手动"),
        new("Members", "成员数", "0.9*", sample: "12 个"),
        new("Created", "创建时间", "1.4*", sample: "2026-10-14 05:20")
    };

    protected override string? MandatoryColumnKey => "Name";

    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not CollectionRow c) return base.FieldText(row, columnKey);
        return columnKey switch
        {
            "Name" => c.Name,
            "Type" => c.TypeText,
            "Members" => c.MemberText,
            "Created" => c.CreatedText,
            _ => base.FieldText(row, columnKey)
        };
    }

    public bool IsDbAvailable => _cols?.IsAvailable ?? false;
    public bool HasItems => Items.Count > 0;
    public bool HasSelection => Selected != null;
    public bool ShowDetailBody => Selected != null;
    public bool HasDetailMembers => DetailMembers.Count > 0;

    /// <summary>当前集合是不是智能集合（成员由规则算，⛔ 不许手工增删 / 排序）。</summary>
    public bool IsSmartSelected => Selected?.IsSmart ?? false;

    /// <summary>详情卡里的操作（上移 / 下移 / 移出）在智能集合上禁用。</summary>
    public bool CanEditMembers => !IsSmartSelected;

    /// <summary>详情卡的说明文案（智能 / 手动两种口径）。</summary>
    public string CollectionHint => Selected == null
        ? "在列表里选中一个集合，这里显示它的成员。"
        : Selected.IsSmart
            ? $"⚡ 智能集合：成员由规则决定（{Selected.RuleJson ?? "未填规则"}）—— 本轮只存规则，规则求值未实装。"
            : "📦 手动集合：成员顺序 = 「⬆ 上移 / ⬇ 下移」写下的 sort_order。";

    public CollectionRow? DetailCollection => Selected;
    public AssetMember? DetailMember => SelectedMember;

    public AssetCollectionsViewModel() { }

    public AssetCollectionsViewModel(CollectionService cols, AssetService assets, OperationLogService log,
        AssetNavigationStore store, IColumnLayoutService layout, INavigationService? nav = null)
        : base(layout)
    {
        _cols = cols;
        _assets = assets;
        _log = log;
        _store = store;
        _nav = nav;

        LoadLayout();
        Reload();
        LoadDetail(null);
        StatusText = StatusSummary();
    }

    // ==================== 加载 ====================

    [RelayCommand]
    private void RefreshCollections()
    {
        var keep = Selected?.Id ?? 0;
        Reload();
        if (keep > 0) Selected = Items.FirstOrDefault(c => c.Id == keep);
        StatusText = IsDbAvailable ? $"已重读集合：共 {Items.Count} 个" : "资产库不可用（数据库打不开）。";
    }

    private void Reload()
    {
        _all.Clear();
        _allRows.Clear();
        if (IsDbAvailable)
        {
            _all.AddRange(_cols.List());
            _allRows.AddRange(_assets.QueryRows(limit: AssetAllViewModel.QueryLimit));
        }

        ApplyView();
    }

    private string StatusSummary()
        => IsDbAvailable
            ? $"共 {Items.Count} 个集合" + (Items.Count == 0 ? "（点「➕ 新建集合」开一个）" : "")
            : "资产库不可用（数据库打不开）—— 本页只读，不会崩。";

    private void ApplyView()
    {
        IEnumerable<CollectionRow> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(c => c.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || (c.Description ?? "").Contains(text, StringComparison.OrdinalIgnoreCase));

        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Type" => Order(q, c => c.TypeText),
            "Members" => Order(q, c => c.MemberCount),
            "Created" => Order(q, c => c.CreatedAt),
            _ => Order(q, c => c.Name)
        };

        Items.Clear();
        foreach (var c in q) Items.Add(c);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private IOrderedEnumerable<CollectionRow> Order<TKey>(IEnumerable<CollectionRow> q, Func<CollectionRow, TKey> key)
        => SortDescending ? q.OrderByDescending(key) : q.OrderBy(key);

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        TypeHeader = "类型" + SortArrow("Type");
        MembersHeader = "成员数" + SortArrow("Members");
        CreatedHeader = "创建时间" + SortArrow("Created");
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    // ==================== 集合操作（写 operation_logs） ====================

    [RelayCommand]
    private async Task NewCollectionAsync()
    {
        var name = await DialogHooks.InputAsync("新建集合（手动）", "").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var id = _cols.Create(name.Trim(), CollectionTypes.Manual);
        if (id <= 0) { StatusText = "新建失败（库不可用？）。"; return; }

        _log.Log("collection", "collection", id, afterJson: $"{{\"name\":\"{name.Trim()}\",\"type\":\"manual\"}}");
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == id);
        StatusText = $"已新建手动集合「{name.Trim()}」。";
    }

    [RelayCommand]
    private async Task NewSmartAsync()
    {
        var name = await DialogHooks.InputAsync("新建智能集合（名字）", "").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var rule = await DialogHooks.InputAsync("规则描述（先存文本；规则求值未实装）", "").ConfigureAwait(true);
        var id = _cols.Create(name.Trim(), CollectionTypes.Smart, $"{{\"desc\":\"{(rule ?? "").Replace("\"", "'")}\"}}");
        if (id <= 0) { StatusText = "新建失败（库不可用？）。"; return; }

        _log.Log("collection", "collection", id, afterJson: $"{{\"name\":\"{name.Trim()}\",\"type\":\"smart\"}}");
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == id);
        StatusText = $"已新建智能集合「{name.Trim()}」（⚠️ 规则求值未实装）。";
    }

    [RelayCommand]
    private async Task RenameCollectionAsync()
    {
        if (Selected == null) { StatusText = "先选中一个集合。"; return; }

        var row = Selected;
        var old = row.Name;
        var name = await DialogHooks.InputAsync("重命名集合", old).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == old) { StatusText = "已取消。"; return; }

        var ok = _cols.Rename(row.Id, name.Trim());
        if (ok) _log.Log("collection", "collection", row.Id, beforeJson: $"{{\"name\":\"{old}\"}}", afterJson: $"{{\"name\":\"{name.Trim()}\"}}");

        var keep = row.Id;
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == keep);
        StatusText = ok ? $"已把「{old}」改名为「{name.Trim()}」。" : "改名失败。";
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync()
    {
        if (Selected == null) { StatusText = "先选中一个集合。"; return; }

        var row = Selected;
        if (!await DialogHooks.ConfirmAsync($"删除集合「{row.Name}」？（⛔ 只删集合与成员关系，不删资产、不动磁盘文件）").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        var ok = _cols.Delete(row.Id);
        if (ok) _log.Log("collection", "collection", row.Id, beforeJson: $"{{\"name\":\"{row.Name}\"}}");

        Reload();
        Selected = Items.FirstOrDefault();
        StatusText = ok ? $"已删除集合「{row.Name}」。" : "删除失败。";
    }

    // ==================== 成员操作 ====================

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (Selected == null || SelectedMember == null) { StatusText = "先在详情卡的成员列表里点一个成员。"; return; }
        if (IsSmartSelected) { StatusText = "⚡ 智能集合的成员由规则决定，⛔ 不能手工排序。"; return; }

        var list = DetailMembers.ToList();
        var i = list.IndexOf(SelectedMember);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) { StatusText = "已经到边界了。"; return; }

        (list[i], list[j]) = (list[j], list[i]);
        var ok = _cols.Reorder(Selected.Id, list.Select(m => m.Id).ToList());
        if (ok) _log.Log("collection", "collection", Selected.Id, afterJson: $"{{\"reorder\":\"{SelectedMember.Name}\"}}");

        var keepId = SelectedMember.Id;
        DetailMembers.Clear();
        foreach (var m in list) DetailMembers.Add(m);
        SelectedMember = DetailMembers.FirstOrDefault(m => m.Id == keepId);
        OnPropertyChanged(nameof(HasDetailMembers));
        StatusText = ok ? $"已移动「{SelectedMember?.Name}」。" : "排序没写进库（库不可用？）。";
    }

    /// <summary>详情卡成员列表点一行 = 选中（供上移 / 下移 / 移出）。</summary>
    [RelayCommand]
    private void SelectMember(AssetMember? item)
    {
        SelectedMember = item;
        OnPropertyChanged(nameof(DetailMember));
        StatusText = item == null ? "未选中成员。" : $"已选中成员「{item.Name}」—— 用下面的按钮上移 / 下移 / 移出。";
    }

    /// <summary>打开详情卡里选中的成员（跳「全部资产」）。</summary>
    [RelayCommand]
    private void OpenSelectedMember()
    {
        if (SelectedMember == null) { StatusText = "先在详情卡的成员列表里点一个成员。"; return; }
        _store.Request("assets-all", SelectedMember.Id);
        _nav?.NavigateTo("assets-all");
        StatusText = $"正在打开资产「{SelectedMember.Name}」…";
    }

    [RelayCommand]
    private async Task RemoveMemberAsync()
    {
        if (Selected == null || SelectedMember == null) { StatusText = "先在详情卡的成员列表里点一个成员。"; return; }
        if (IsSmartSelected) { StatusText = "⚡ 智能集合的成员由规则决定，⛔ 不能手工摘出。"; return; }

        var coll = Selected;
        var member = SelectedMember;
        if (!await DialogHooks.ConfirmAsync($"把「{member.Name}」移出集合「{coll.Name}」？（⛔ 不删资产、⛔ 不动磁盘文件）").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        var n = _cols.RemoveAssets(coll.Id, new List<long> { member.Id });
        if (n > 0) _log.Log("collection", "collection", coll.Id, afterJson: $"{{\"removedAssets\":{n}}}");

        var keep = coll.Id;
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == keep);
        StatusText = $"已把「{member.Name}」移出集合（{n} 条）。";
    }

    // ==================== 详情卡 ====================

    partial void OnSelectedChanged(CollectionRow? value)
    {
        OnPropertyChanged(nameof(DetailCollection));
        OnPropertyChanged(nameof(IsSmartSelected));
        OnPropertyChanged(nameof(CanEditMembers));
        OnPropertyChanged(nameof(CollectionHint));
        LoadDetail(value);
        OnPropertyChanged(nameof(HasSelection));
        RaiseDetailChanged();
    }

    private void LoadDetail(CollectionRow? row)
    {
        _loadingDetail = true;
        try
        {
            DetailMembers.Clear();
            SelectedMember = null;
            EditName = row?.Name ?? "";
            EditDescription = row?.Description ?? "";

            if (row == null || row.Id <= 0)
            {
                OnPropertyChanged(nameof(HasDetailMembers));
                return;
            }

            var ids = _cols.ListMemberIds(row.Id);
            foreach (var id in ids.Take(200))
            {
                var asset = _allRows.FirstOrDefault(r => r.Id == id);
                if (asset != null) DetailMembers.Add(new AssetMember(asset));
            }
            OnPropertyChanged(nameof(HasDetailMembers));
        }
        finally { _loadingDetail = false; }
    }

    /// <summary>详情卡「💾 保存」：名称 + 说明一起落库。</summary>
    [RelayCommand]
    private void SaveDetail()
    {
        if (_loadingDetail || Selected == null) { StatusText = "先在列表里选中一个集合。"; return; }

        var name = (EditName ?? "").Trim();
        if (name.Length == 0) { StatusText = "名称不能为空。"; return; }

        var row = Selected;
        var before = $"{{\"name\":\"{row.Name}\"}}";
        var okName = name == row.Name || _cols.Rename(row.Id, name);
        var okDesc = _cols.SetDescription(row.Id, string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim());

        _log.Log("collection", "collection", row.Id, beforeJson: before, afterJson: $"{{\"name\":\"{name}\"}}");

        var keep = row.Id;
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == keep);
        StatusText = okName && okDesc ? $"已保存集合「{name}」。" : "部分字段保存失败。";
    }

    // ==================== 详情面板（Shell 承载） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在列表里选中一个集合，这里显示它的成员与顺序。",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "名称",
                        ValuePath = nameof(EditName),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Placeholder = "集合名（不能为空）"
                    },
                    new DetailField
                    {
                        Label = "说明（可空）",
                        ValuePath = nameof(EditDescription),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Placeholder = "这个集合是干什么的"
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField { Label = "类型 / 成员数", ValuePath = "DetailCollection.MemberText", Kind = FieldKind.Text },
                    new DetailField { Label = "创建时间", ValuePath = "DetailCollection.CreatedText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "成员规则", ValuePath = nameof(CollectionHint), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "成员（点一行选中，再用下面的按钮排序 / 移出）",
                        Kind = FieldKind.List,
                        OptionsPath = nameof(DetailMembers),
                        ItemTextPath = "Name",
                        ItemCommandPath = nameof(SelectMemberCommand)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（这个集合还没有成员）",
                        VisibleWhen = "!" + nameof(HasDetailMembers)
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "⬆ 上移",
                ToolTip = "把选中的成员上移一位（写 sort_order）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = MoveUpCommand
            },
            new DetailAction
            {
                Text = "⬇ 下移",
                ToolTip = "把选中的成员下移一位",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = MoveDownCommand
            },
            new DetailAction
            {
                Text = "📂 打开选中",
                ToolTip = "跳「全部资产」并打开选中的成员",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = OpenSelectedMemberCommand
            },
            new DetailAction
            {
                Text = "⛔ 移出选中",
                Classes = "danger-btn",
                ToolTip = "把选中成员移出本集合（⛔ 不删资产）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = RemoveMemberCommand
            },
            new DetailAction
            {
                Text = "💾 保存",
                Classes = "primary",
                ToolTip = "把名称 / 说明写进集合库（⛔ 不动磁盘文件）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = SaveDetailCommand
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
