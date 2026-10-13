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
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「分类管理」页（NT-018）—— 本模块**唯一的非列表页**：左树（自绘，<c>ItemsControl</c> + 按钮行）＋ 右成员列表。
/// <para>⚠️ 本页**不实现** <c>IDetailPanelProvider</c>（<c>HasDetailPanel=false</c>）→ Shell 不显示右详情列。</para>
/// <para>树按 <see cref="CategoryNode.Depth"/> 扁平化后渲染：只有「父节点已展开」的节点才进可见集合。</para>
/// <para>逻辑操作（新建 / 改名 / 删除子树 / 摘出成员）直接改库并写 <c>operation_logs</c>（reversible=1）。</para>
/// </summary>
public partial class AssetCategoriesViewModel : ListPageViewModelBase
{
    private readonly CategoryService _cats = null!;
    private readonly AssetService _assets = null!;
    private readonly OperationLogService _log = null!;

    /// <summary>全部分类（未扁平化）。</summary>
    private readonly List<CategoryNode> _allCats = new();

    /// <summary>全量资产行缓存（成员按 id 从这里取，⛔ 不 per-member 查库）。</summary>
    private readonly List<AssetRow> _allRows = new();

    public ObservableCollection<CategoryNode> TreeNodes { get; } = new();
    public ObservableCollection<AssetMember> Members { get; } = new();

    /// <summary>右列表当前选中的成员（「⛔ 摘出选中」用；单选，与集合页保持一致）。</summary>
    [ObservableProperty] private AssetMember? _selectedMember;

    [ObservableProperty] private CategoryNode? _selectedCategory;
    [ObservableProperty] private string _searchText = "";

    // ===== 表头（成员列表） =====
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

    protected override string PageKey => "assets-categories";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2.6*", sample: "The.Matrix.1999.1080p.mkv"),
        new("Type", "类型", "0.8*", sample: "视频"),
        new("Size", "大小", "0.9*", sample: "4.2 GB"),
        new("Modified", "修改时间", "1.3*", sample: "2026-10-13 21:04"),
        new("Storage", "存储源", "1.2*", sample: "移动硬盘 D")
    };

    protected override void RefreshView() => ApplyView();

    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not AssetMember m) return base.FieldText(row, columnKey);
        return columnKey switch
        {
            "Name" => m.Name,
            "Type" => m.TypeText,
            "Size" => m.SizeText,
            "Modified" => m.ModifiedText,
            "Storage" => m.StorageName,
            _ => base.FieldText(row, columnKey)
        };
    }

    protected override double? FieldNumber(object row, string columnKey)
        => row is AssetMember m && columnKey == "Size" ? m.Row.Size : base.FieldNumber(row, columnKey);

    public bool IsDbAvailable => _cats?.IsAvailable ?? false;
    public bool HasNodes => TreeNodes.Count > 0;
    public bool HasMembers => Members.Count > 0;

    public AssetCategoriesViewModel() { }

    public AssetCategoriesViewModel(CategoryService cats, AssetService assets, OperationLogService log, IColumnLayoutService cols)
        : base(cols)
    {
        _cats = cats;
        _assets = assets;
        _log = log;

        LoadLayout();
        ReloadAll();
        StatusText = StatusSummary();
    }

    // ==================== 加载 ====================

    [RelayCommand]
    private void RefreshCategories()
    {
        var keep = SelectedCategory?.Id ?? 0;
        ReloadAll();
        if (keep > 0) SelectCategory(_allCats.FirstOrDefault(n => n.Id == keep));
        StatusText = IsDbAvailable ? $"已重读分类：共 {_allCats.Count} 个分类档 / {Members.Count} 个成员" : "资产库不可用（数据库打不开）。";
    }

    private void ReloadAll()
    {
        _allCats.Clear();
        _allRows.Clear();
        if (IsDbAvailable)
        {
            _allCats.AddRange(_cats.List());
            _allRows.AddRange(_assets.QueryRows(limit: AssetAllViewModel.QueryLimit));
        }

        // 默认全部展开（首次进页面就能看清结构；用户折叠过的不重置）
        foreach (var n in _allCats) if (n.ChildCount == 0) n.IsExpanded = true;
        RebuildTree();
        // 首次打开没有选中 → 直接落到第一个根分类，右栏不至于空着
        if (SelectedCategory == null && _allCats.Count > 0)
            SelectCategory(TreeNodes.FirstOrDefault(n => n.Depth == 0));
        else
            LoadMembers(SelectedCategory?.Id ?? 0);
    }

    private string StatusSummary()
        => IsDbAvailable
            ? $"共 {_allCats.Count} 个分类" + (_allCats.Count == 0 ? "（点「➕ 新建分类」开一个根分类）" : "")
            : "资产库不可用（数据库打不开）—— 本页只读，不会崩。";

    /// <summary>扁平化：填 <c>Depth</c> / <c>ChildCount</c>，只保留「祖先链全展开」的节点。</summary>
    private void RebuildTree()
    {
        foreach (var n in _allCats)
        {
            n.ChildCount = _allCats.Count(c => c.ParentId == n.Id);
            n.Depth = 0;
        }

        TreeNodes.Clear();
        foreach (var root in _allCats.Where(n => n.ParentId == null))
            AddVisible(root, 0);

        OnPropertyChanged(nameof(HasNodes));
    }

    private void AddVisible(CategoryNode node, int depth)
    {
        node.Depth = depth;
        TreeNodes.Add(node);
        if (!node.IsExpanded) return;
        foreach (var child in _allCats.Where(c => c.ParentId == node.Id).OrderBy(c => c.SortOrder))
            AddVisible(child, depth + 1);
    }

    private void LoadMembers(long categoryId)
    {
        Members.Clear();
        SelectedMember = null;
        if (categoryId > 0 && IsDbAvailable)
        {
            var ids = _cats.ListMemberIds(categoryId);
            foreach (var r in _allRows.Where(r => ids.Contains(r.Id)))
                Members.Add(new AssetMember(r));
        }
        ApplyView();
    }

    private void ApplyView()
    {
        IEnumerable<AssetMember> q = Members;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0) q = q.Where(m => m.Name.Contains(text, StringComparison.OrdinalIgnoreCase));

        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Type" => Order(q, m => m.TypeText),
            "Size" => Order(q, m => m.Row.Size),
            "Modified" => Order(q, m => m.Row.Mtime ?? ""),
            "Storage" => Order(q, m => m.StorageName),
            _ => Order(q, m => m.Name)
        };

        var ordered = q.ToList();
        Members.Clear();
        foreach (var m in ordered) Members.Add(m);

        UpdateHeaders();
        OnPropertyChanged(nameof(HasMembers));
    }

    private IOrderedEnumerable<AssetMember> Order<TKey>(IEnumerable<AssetMember> q, Func<AssetMember, TKey> key)
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

    // ==================== 树操作 ====================

    /// <summary>点一个节点：选中 + 切换展开 + 载入成员。</summary>
    [RelayCommand]
    private void SelectNode(CategoryNode? node)
    {
        if (node == null) return;
        SelectCategory(node);
    }

    private void SelectCategory(CategoryNode? node)
    {
        foreach (var n in _allCats) n.IsSelected = ReferenceEquals(n, node);
        SelectedCategory = node;

        if (node != null && node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
            RebuildTree();
        }
        LoadMembers(node?.Id ?? 0);
        StatusText = node == null ? "未选中分类。" : $"分类「{node.Name}」：{Members.Count} 个成员";
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var n in _allCats) n.IsExpanded = true;
        RebuildTree();
        StatusText = "已展开全部分类。";
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var n in _allCats) n.IsExpanded = false;
        RebuildTree();
        StatusText = "已折叠全部分类。";
    }

    // ==================== 逻辑操作（写 operation_logs） ====================

    /// <summary>新建**根**分类。</summary>
    [RelayCommand]
    private async Task NewRootAsync()
    {
        var name = await DialogHooks.InputAsync("新建分类（根）", "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }
        await CreateAsync(name.Trim(), null).ConfigureAwait(false);
    }

    /// <summary>在当前选中分类下新建**子**分类。</summary>
    [RelayCommand]
    private async Task NewChildAsync()
    {
        if (SelectedCategory == null) { StatusText = "先选中一个分类，再新建子分类（没选中就给根分类用「➕ 新建分类」）。"; return; }

        var parent = SelectedCategory;
        var name = await DialogHooks.InputAsync($"在「{parent.Name}」下新建子分类", "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }
        await CreateAsync(name.Trim(), parent.Id).ConfigureAwait(false);
    }

    private Task CreateAsync(string name, long? parentId)
    {
        var id = _cats.Create(name, parentId);
        if (id <= 0) { StatusText = $"新建分类「{name}」失败。"; return Task.CompletedTask; }

        _log.Log("category", "category", id, afterJson: $"{{\"name\":\"{name}\",\"parent\":{parentId?.ToString() ?? "null"}}}");
        ReloadAll();
        SelectCategory(_allCats.FirstOrDefault(n => n.Id == id));
        StatusText = $"已新建分类「{name}」。";
        return Task.CompletedTask;
    }

    /// <summary>重命名（行右键）。</summary>
    [RelayCommand]
    private async Task RenameCategoryAsync()
    {
        if (SelectedCategory == null) { StatusText = "先选中一个分类。"; return; }

        var node = SelectedCategory;
        var old = node.Name;
        var name = await DialogHooks.InputAsync("重命名分类", old).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == old) { StatusText = "已取消。"; return; }

        var ok = _cats.Rename(node.Id, name.Trim());
        if (ok) _log.Log("category", "category", node.Id, beforeJson: $"{{\"name\":\"{old}\"}}", afterJson: $"{{\"name\":\"{name.Trim()}\"}}");

        var keep = node.Id;
        ReloadAll();
        SelectCategory(_allCats.FirstOrDefault(n => n.Id == keep));
        StatusText = ok ? $"已把「{old}」改名为「{name.Trim()}」。" : "改名失败。";
    }

    /// <summary>删除分类（连带整棵子树；级联提示 + 二次确认）。</summary>
    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory == null) { StatusText = "先选中一个分类。"; return; }

        var node = SelectedCategory;
        var subtree = _cats.SubtreeIds(node.Id);
        var children = subtree.Count - 1;
        var warn = children > 0
            ? $"「{node.Name}」下面还有 {children} 个子分类，删除会**连带删掉整棵子树**（含成员归属关系；⛔ 不动磁盘文件）。继续？"
            : $"删除分类「{node.Name}」？（⛔ 只删归属关系，不动磁盘文件）";
        if (!await DialogHooks.ConfirmAsync(warn).ConfigureAwait(false)) { StatusText = "已取消。"; return; }

        var n = _cats.DeleteSubtree(node.Id);
        if (n > 0) _log.Log("category", "category", node.Id, beforeJson: $"{{\"name\":\"{node.Name}\",\"subtree\":{n}}}");
        ReloadAll();
        SelectCategory(null);
        StatusText = n > 0 ? $"已删除分类「{node.Name}」（连带 {n - 1} 个子分类）。" : "删除失败。";
    }

    /// <summary>把选中的成员**摘出**本分类（⛔ 只删归属，不删资产）。</summary>
    [RelayCommand]
    private async Task RemoveMembersAsync()
    {
        if (SelectedCategory == null || SelectedMember == null) { StatusText = "先在右边选中要摘出的成员。"; return; }

        var cid = SelectedCategory.Id;
        var catName = SelectedCategory.Name;
        var ids = new List<long> { SelectedMember.Id };
        if (!await DialogHooks.ConfirmAsync($"把资产「{SelectedMember.Name}」从「{catName}」摘出？（⛔ 不删资产、⛔ 不动磁盘文件）").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        var n = _cats.RemoveAssets(cid, ids);
        if (n > 0) _log.Log("category", "category", cid, afterJson: $"{{\"removedAssets\":{n}}}");
        var keep = cid;
        ReloadAll();
        SelectCategory(_allCats.FirstOrDefault(c => c.Id == keep));
        StatusText = $"已把 {n} 个资产摘出本分类。";
    }
}
