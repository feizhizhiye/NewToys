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
/// 「分类管理」页：与标签页同构的**列表页**（名称 / 路径 / 成员数 / 排序 / 创建时间）+ 详情卡。
/// <para>分类是树，列表用「路径」列 + 首列缩进表达层级；改挂父级走详情卡的「父分类」chips
/// （<c>CategoryService.Move</c> 自带防环）。成员浏览跳「全部资产」按该分类过滤。</para>
/// <para>逻辑操作（新建 / 改名 / 改挂 / 删除子树 / 摘出成员）直接改库并写 <c>operation_logs</c>。</para>
/// </summary>
public partial class AssetCategoriesViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly CategoryService _cats = null!;
    private readonly AssetService _assets = null!;
    private readonly OperationLogService _log = null!;
    private readonly AssetNavigationStore _store = null!;
    private readonly INavigationService? _nav;

    /// <summary>全部分类（<see cref="CategoryNode.Depth"/> / <see cref="CategoryNode.FullPath"/> 由 <see cref="BuildRows"/> 回填）。</summary>
    private readonly List<CategoryNode> _all = new();

    /// <summary>全量资产行缓存（成员按 id 从这里取，⛔ 不 per-member 查库）。</summary>
    private readonly List<AssetRow> _allRows = new();

    /// <summary>默认（层级）顺序 = DFS 序号；列排序非 Path 时不用它。</summary>
    private readonly Dictionary<long, int> _treeOrder = new();

    private bool _loadingDetail;

    public ObservableCollection<CategoryNode> Items { get; } = new();

    /// <summary>详情卡里的成员（前 200 条；点一下跳「全部资产」）。</summary>
    public ObservableCollection<AssetMember> DetailMembers { get; } = new();

    /// <summary>详情卡「父分类」chips（含「（根）」= id 0）。</summary>
    public ObservableCollection<PickItem> ParentCandidates { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private CategoryNode? _selected;
    [ObservableProperty] private string _searchText = "";

    // ===== 详情卡编辑字段 =====
    [ObservableProperty] private string _editName = "";

    // ===== 表头 =====
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _pathHeader = "路径";
    [ObservableProperty] private string _membersHeader = "成员数";
    [ObservableProperty] private string _sortHeader = "排序";
    [ObservableProperty] private string _createdHeader = "创建时间";

    public bool IsNameVisible => Vis("Name");
    public bool IsPathVisible => Vis("Path");
    public bool IsMembersVisible => Vis("Members");
    public bool IsSortVisible => Vis("Sort");
    public bool IsCreatedVisible => Vis("Created");

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "＋ 新建分类",
            Classes = "ghost",
            CommandPath = nameof(NewRootCommand),
            ToolTip = "新建一个**根**分类（子分类在详情卡里挂父级，或选中后点「子分类」）"
        },
        new()
        {
            Text = "＋ 子分类",
            CommandPath = nameof(NewChildCommand),
            IsEnabledPath = nameof(HasSelection),
            ShowToolTipOnDisabled = true,
            ToolTip = "在选中的分类下新建子分类"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            Placeholder = "搜索分类名 / 路径…",
            ValuePath = nameof(SearchText)
        },
        new()
        {
            Text = "重读分类", IconKey = "IconRefresh",
            CommandPath = nameof(RefreshCategoriesCommand),
            ToolTip = "重新读取分类表（⛔ 不扫盘）"
        },
    };

    protected override string PageKey => "assets-categories";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2*", sample: "动作"),
        new("Path", "路径", "2.2*", sample: "影视 / 电影 / 动作"),
        new("Members", "成员数", "0.9*", sample: "12 个"),
        new("Sort", "排序", "0.7*", sample: "0"),
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
        if (row is not CategoryNode c) return base.FieldText(row, columnKey);
        return columnKey switch
        {
            "Name" => c.Name,
            "Path" => c.PathText,
            "Members" => c.MemberText,
            "Sort" => c.SortText,
            "Created" => c.CreatedText,
            _ => base.FieldText(row, columnKey)
        };
    }

    public bool IsDbAvailable => _cats?.IsAvailable ?? false;
    public bool HasItems => Items.Count > 0;
    public bool HasSelection => Selected != null;
    public bool ShowDetailBody => Selected != null;
    public bool HasDetailMembers => DetailMembers.Count > 0;

    /// <summary>详情卡当前显示的分类。</summary>
    public CategoryNode? DetailCategory => Selected;

    public AssetCategoriesViewModel() { }

    public AssetCategoriesViewModel(CategoryService cats, AssetService assets, OperationLogService log,
        AssetNavigationStore store, IColumnLayoutService cols, INavigationService? nav = null)
        : base(cols)
    {
        _cats = cats;
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
    private void RefreshCategories()
    {
        Reload();
        StatusText = IsDbAvailable ? $"已重读分类：共 {Items.Count} 个" : "资产库不可用（数据库打不开）。";
    }

    private void Reload()
    {
        _all.Clear();
        if (IsDbAvailable)
        {
            _all.AddRange(_cats.List());
            _allRows.Clear();
            _allRows.AddRange(_assets.QueryRows(limit: AssetAllViewModel.QueryLimit));
        }

        BuildRows();

        var keep = Selected?.Id ?? 0;
        ApplyView();
        if (keep > 0) Selected = Items.FirstOrDefault(c => c.Id == keep) ?? Selected;
    }

    /// <summary>算 <c>Depth</c> / <c>FullPath</c> / 默认层级序号（脏数据自环时按根处理，⛔ 不死循环）。</summary>
    private void BuildRows()
    {
        _treeOrder.Clear();

        var byId = new Dictionary<long, CategoryNode>();
        foreach (var c in _all) byId[c.Id] = c;

        var children = new Dictionary<long, List<CategoryNode>>();
        foreach (var c in _all)
        {
            var parent = c.ParentId.HasValue && byId.ContainsKey(c.ParentId.Value) && c.ParentId.Value != c.Id
                ? c.ParentId.Value
                : 0;
            if (!children.TryGetValue(parent, out var list)) { list = new List<CategoryNode>(); children[parent] = list; }
            list.Add(c);
        }

        var seq = 0;
        var visited = new HashSet<long>();
        void Walk(long parentId, int depth)
        {
            if (!children.TryGetValue(parentId, out var list)) return;
            foreach (var c in list.OrderBy(x => x.SortOrder).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!visited.Add(c.Id)) continue;
                c.Depth = depth;
                c.FullPath = BuildPath(c, byId);
                _treeOrder[c.Id] = seq++;
                Walk(c.Id, depth + 1);
            }
        }
        Walk(0, 0);

        // 自环 / 断链（父不存在）的兜底：仍要能看到，⛔ 不能因为脏数据整表消失
        foreach (var c in _all.Where(x => !visited.Contains(x.Id)))
        {
            c.Depth = 0;
            c.FullPath = c.Name;
            _treeOrder[c.Id] = seq++;
        }
    }

    private static string BuildPath(CategoryNode node, Dictionary<long, CategoryNode> byId)
    {
        var parts = new List<string>();
        var cur = node;
        var guard = 0;
        while (cur != null && guard++ < 64)
        {
            parts.Insert(0, cur.Name);
            cur = cur.ParentId.HasValue && byId.TryGetValue(cur.ParentId.Value, out var p) && !ReferenceEquals(p, cur)
                ? p
                : null;
        }
        return string.Join(" / ", parts);
    }

    private string StatusSummary()
        => IsDbAvailable
            ? $"共 {Items.Count} 个分类" + (Items.Count == 0 ? "（点「新建分类」开一个）" : "")
            : "资产库不可用（数据库打不开）—— 本页只读，不会崩。";

    private void ApplyView()
    {
        IEnumerable<CategoryNode> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(c => c.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || c.PathText.Contains(text, StringComparison.OrdinalIgnoreCase));

        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Name" => Order(q, c => c.Name),
            "Members" => Order(q, c => c.MemberCount),
            "Sort" => Order(q, c => c.SortOrder),
            "Created" => Order(q, c => c.CreatedAt),
            _ => Order(q, c => _treeOrder.TryGetValue(c.Id, out var i) ? i : int.MaxValue)
        };

        Items.Clear();
        foreach (var c in q) Items.Add(c);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private IOrderedEnumerable<CategoryNode> Order<TKey>(IEnumerable<CategoryNode> q, Func<CategoryNode, TKey> key)
        => SortDescending ? q.OrderByDescending(key) : q.OrderBy(key);

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        PathHeader = "路径" + SortArrow("Path");
        MembersHeader = "成员数" + SortArrow("Members");
        SortHeader = "排序" + SortArrow("Sort");
        CreatedHeader = "创建时间" + SortArrow("Created");
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    // ==================== 逻辑操作（写 operation_logs） ====================

    [RelayCommand]
    private async Task NewRootAsync()
    {
        var name = await DialogHooks.InputAsync("新建分类（根）", "").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消（没输入名字）。"; return; }

        var id = _cats.Create(name.Trim(), null);
        if (id <= 0) { StatusText = "新建失败（库不可用？）。"; return; }

        _log.Log("category", "category", id, afterJson: $"{{\"name\":\"{name.Trim()}\",\"parent\":null}}");
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == id);
        StatusText = $"已新建根分类「{name.Trim()}」。";
    }

    [RelayCommand]
    private async Task NewChildAsync()
    {
        if (Selected == null) { StatusText = "先选中一个分类，再新建子分类。"; return; }

        var parent = Selected;
        var name = await DialogHooks.InputAsync($"在「{parent.Name}」下新建子分类", "").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var id = _cats.Create(name.Trim(), parent.Id);
        if (id <= 0) { StatusText = "新建失败（库不可用？）。"; return; }

        _log.Log("category", "category", id, afterJson: $"{{\"name\":\"{name.Trim()}\",\"parent\":{parent.Id}}}");
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == id);
        StatusText = $"已在「{parent.Name}」下新建「{name.Trim()}」。";
    }

    [RelayCommand]
    private async Task RenameCategoryAsync()
    {
        if (Selected == null) { StatusText = "先选中一个分类。"; return; }

        var old = Selected.Name;
        var name = await DialogHooks.InputAsync("重命名分类", old).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == old) { StatusText = "已取消（名字没变）。"; return; }

        var ok = _cats.Rename(Selected.Id, name.Trim());
        if (ok) _log.Log("category", "category", Selected.Id, beforeJson: $"{{\"name\":\"{old}\"}}", afterJson: $"{{\"name\":\"{name.Trim()}\"}}");

        var keep = Selected.Id;
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == keep);
        StatusText = ok ? $"已把「{old}」改名为「{name.Trim()}」。" : "改名失败。";
    }

    /// <summary>详情卡「父分类」chips：点一下即改挂（<paramref name="parentId"/> = 0 表示移到根）。</summary>
    [RelayCommand]
    private void MoveTo(long parentId)
    {
        if (Selected == null) { StatusText = "先选中一个分类。"; return; }

        var node = Selected;
        var newParent = parentId <= 0 ? (long?)null : parentId;
        var ok = _cats.Move(node.Id, newParent);

        if (ok) _log.Log("category", "category", node.Id, afterJson: $"{{\"parent\":{newParent?.ToString() ?? "null"}}}");
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == node.Id);
        StatusText = ok
            ? $"已把「{node.Name}」挂到「{(newParent.HasValue ? Items.FirstOrDefault(c => c.Id == newParent)?.Name ?? "?" : "根")}」。"
            : "改挂失败（⛔ 不能挂到自己的子孙下）。";
    }

    [RelayCommand]
    private void MoveToRoot()
    {
        if (Selected == null) { StatusText = "先选中一个分类。"; return; }
        MoveTo(0);
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (Selected == null) { StatusText = "先选中一个分类。"; return; }

        var node = Selected;
        var subtree = _cats.SubtreeIds(node.Id);
        var children = subtree.Count - 1;
        var warn = children > 0
            ? $"「{node.Name}」下面还有 {children} 个子分类，删除会**连带删掉整棵子树**（含成员归属关系；⛔ 不动磁盘文件）。继续？"
            : $"删除分类「{node.Name}」？（⛔ 只删归属关系，不动磁盘文件）";
        if (!await DialogHooks.ConfirmAsync(warn).ConfigureAwait(true)) { StatusText = "已取消。"; return; }

        var n = _cats.DeleteSubtree(node.Id);
        if (n > 0) _log.Log("category", "category", node.Id, beforeJson: $"{{\"name\":\"{node.Name}\",\"subtree\":{n}}}");

        Reload();
        Selected = Items.FirstOrDefault();
        StatusText = n > 0 ? $"已删除分类「{node.Name}」（连带 {n - 1} 个子分类）。" : "删除失败。";
    }

    /// <summary>详情卡成员列表点一行 → 跳「全部资产」并选中。</summary>
    [RelayCommand]
    private void OpenMember(AssetMember? item)
    {
        if (item == null) return;
        _store.Request("assets-all", item.Id);
        _nav?.NavigateTo("assets-all");
        StatusText = $"正在打开资产「{item.Name}」…";
    }

    /// <summary>跳「全部资产」（成员完整列表在那边看；本页只给前 200 条）。</summary>
    [RelayCommand]
    private void OpenCategoryInAssets()
    {
        if (Selected == null) { StatusText = "先选中一个分类。"; return; }
        _nav?.NavigateTo("assets-all");
        StatusText = $"已切到「全部资产」—— 用筛选栏的「分类」选「{Selected.Name}」即可看全部成员。";
    }

    // ==================== 详情卡 ====================

    partial void OnSelectedChanged(CategoryNode? value)
    {
        OnPropertyChanged(nameof(DetailCategory));
        LoadDetail(value);
        OnPropertyChanged(nameof(HasSelection));
        RaiseDetailChanged();
    }

    private void LoadDetail(CategoryNode? row)
    {
        _loadingDetail = true;
        try
        {
            DetailMembers.Clear();
            EditName = row?.Name ?? "";

            if (row == null || row.Id <= 0)
            {
                ParentCandidates.Clear();
                OnPropertyChanged(nameof(HasDetailMembers));
                return;
            }

            var ids = _cats.ListMemberIds(row.Id);
            foreach (var r in _allRows.Where(r => ids.Contains(r.Id)).Take(200))
                DetailMembers.Add(new AssetMember(r));

            BuildParentCandidates(row);
            OnPropertyChanged(nameof(HasDetailMembers));
        }
        finally { _loadingDetail = false; }
    }

    private void BuildParentCandidates(CategoryNode row)
    {
        ParentCandidates.Clear();
        ParentCandidates.Add(new PickItem(0, "（根）", Avalonia.Media.Brushes.Gray, row.ParentId == null));

        // ⛔ 自己与子孙不能当父级（服务层也拦，这里是不让它出现在候选里）
        var blocked = _cats.SubtreeIds(row.Id).ToHashSet();
        foreach (var c in _all.Where(c => !blocked.Contains(c.Id)).OrderBy(c => c.FullPath, StringComparer.CurrentCultureIgnoreCase))
            ParentCandidates.Add(new PickItem(c.Id, c.PathText,
                new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(AssetPalette.For(c.Name))),
                row.ParentId == c.Id));
    }

    /// <summary>详情卡「保存」：只保存名称（父级走 chips 立即生效）。</summary>
    [RelayCommand]
    private void SaveDetail()
    {
        if (_loadingDetail || Selected == null) { StatusText = "先在列表里选中一个分类。"; return; }

        var name = (EditName ?? "").Trim();
        if (name.Length == 0) { StatusText = "名称不能为空。"; return; }
        if (name == Selected.Name) { StatusText = "名字没变，不用保存。"; return; }

        var old = Selected.Name;
        var ok = _cats.Rename(Selected.Id, name);
        if (ok) _log.Log("category", "category", Selected.Id, beforeJson: $"{{\"name\":\"{old}\"}}", afterJson: $"{{\"name\":\"{name}\"}}");

        var keep = Selected.Id;
        Reload();
        Selected = Items.FirstOrDefault(c => c.Id == keep);
        StatusText = ok ? $"已保存分类「{name}」。" : "保存失败。";
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
        EmptyText = "在列表里选中一个分类，这里显示它的详情。",
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
                        Placeholder = "分类名（不能为空）"
                    },
                    new DetailField
                    {
                        Label = "父分类",
                        Hint = "点一下即改挂，立即生效",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(ParentCandidates),
                        ChipTextPath = "Name",
                        ChipSelectedPath = "IsSelected",
                        ChipBrushPath = "Brush",
                        ChipCommandPath = nameof(MoveToCommand),
                        ChipCommandArgPath = "Id"
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField { Label = "路径", ValuePath = "DetailCategory.PathText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "成员数", ValuePath = "DetailCategory.MemberText", Kind = FieldKind.Text },
                    new DetailField { Label = "创建时间", ValuePath = "DetailCategory.CreatedText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "成员",
                        Hint = "点一下打开 · 完整列表走「全部资产」",
                        Kind = FieldKind.List,
                        OptionsPath = nameof(DetailMembers),
                        ItemTextPath = "Name",
                        ItemCommandPath = nameof(OpenMemberCommand)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（这个分类还没有成员）",
                        VisibleWhen = "!" + nameof(HasDetailMembers)
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "全部资产", IconKey = "IconOpen",
                ToolTip = "跳到「全部资产」并按本分类过滤（能排序 / 批量操作 / 导出）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = OpenCategoryInAssetsCommand
            },
            new DetailAction
            {
                Text = "保存", IconKey = "IconSave",
                Classes = "primary",
                ToolTip = "把名称写进分类库（⛔ 不动磁盘文件）",
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
