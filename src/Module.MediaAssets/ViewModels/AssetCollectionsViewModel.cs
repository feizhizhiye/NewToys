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
/// 「集合管理」页（NT-018）：左集合列表（📦 手动 / ⚡ 智能）+ 右成员列表。
/// <para>成员顺序 = `collection_assets.sort_order`：⚙ 本页用「⬆ 上移 / ⬇ 下移」写它，效果与拖拽一致
/// （⛔ 未做鼠标拖放 —— 见 NT-018 执行记录里对"拖拽排序"的实现说明）。</para>
/// <para>⚠️ 智能集合（`rule_json`）本轮只区分类型 / 图标与存规则文本，**规则求值未实装**。</para>
/// <para>⛔ 本页不实现 <c>IDetailPanelProvider</c>（左右两栏都在页内）。</para>
/// </summary>
public partial class AssetCollectionsViewModel : ListPageViewModelBase
{
    private readonly CollectionService _cols = null!;
    private readonly AssetService _assets = null!;
    private readonly OperationLogService _log = null!;

    private readonly List<AssetRow> _allRows = new();

    public ObservableCollection<CollectionRow> Collections { get; } = new();
    public ObservableCollection<AssetMember> Members { get; } = new();

    [ObservableProperty] private CollectionRow? _selectedCollection;
    [ObservableProperty] private AssetMember? _selectedMember;
    [ObservableProperty] private string _searchText = "";

    // ===== 表头（成员列表；与分类页同一套列，观感一致） =====
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
    /// 工具条：页内 ⛔ 不再自写 <c>Border.toolbar</c>，改由 Shell 渲染本描述符。
    /// ⚠️ 与迁移前**逐项一致**（搜索框 + 上移 / 下移 / 移出选中，后三者**智能集合置灰**）；
    /// 本页 <c>HasDetailPanel=false</c> ⇒ Shell ⛔ 不显示详情开关。
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            Placeholder = "搜索成员名称…",
            ValuePath = nameof(SearchText)
        },
        new()
        {
            Text = "⬆ 上移",
            CommandPath = nameof(MoveUpCommand),
            IsEnabledPath = "!" + nameof(IsSmartSelected),
            ShowToolTipOnDisabled = true,
            ToolTip = "上移一位（写 sort_order，与拖拽同效）"
        },
        new()
        {
            Text = "⬇ 下移",
            CommandPath = nameof(MoveDownCommand),
            IsEnabledPath = "!" + nameof(IsSmartSelected),
            ShowToolTipOnDisabled = true,
            ToolTip = "下移一位（写 sort_order）"
        },
        new()
        {
            Text = "⛔ 移出选中",
            Classes = "danger-btn",
            CommandPath = nameof(RemoveMemberCommand),
            IsEnabledPath = "!" + nameof(IsSmartSelected),
            ShowToolTipOnDisabled = true,
            ToolTip = "把选中的资产移出本集合（⛔ 不删资产、⛔ 不动磁盘文件）"
        },
    };

    protected override string PageKey => "assets-collections";

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

    public bool IsDbAvailable => _cols?.IsAvailable ?? false;
    public bool HasCollections => Collections.Count > 0;
    public bool HasMembers => Members.Count > 0;

    /// <summary>当前集合是不是智能集合（成员由规则算，⛔ 不许手工增删 / 排序）。</summary>
    public bool IsSmartSelected => SelectedCollection?.IsSmart ?? false;

    /// <summary>当前集合的说明文案（详情提示用）。</summary>
    public string CollectionHint => SelectedCollection == null
        ? "在左边选一个集合，右边显示它的成员。"
        : SelectedCollection.IsSmart
            ? $"⚡ 智能集合：成员由规则决定（{SelectedCollection.RuleJson ?? "未填规则"}）—— 本轮只存规则，规则求值未实装。"
            : "📦 手动集合：成员顺序 = 拖拽 / 上下移动写下的 sort_order。";

    public AssetCollectionsViewModel() { }

    public AssetCollectionsViewModel(CollectionService cols, AssetService assets, OperationLogService log, IColumnLayoutService layout)
        : base(layout)
    {
        _cols = cols;
        _assets = assets;
        _log = log;

        LoadLayout();
        ReloadAll();
        StatusText = StatusSummary();
    }

    // ==================== 加载 ====================

    [RelayCommand]
    private void RefreshCollections()
    {
        var keep = SelectedCollection?.Id ?? 0;
        ReloadAll();
        if (keep > 0) SelectCollection(Collections.FirstOrDefault(c => c.Id == keep) ?? Collections.FirstOrDefault());
        StatusText = IsDbAvailable ? $"已重读集合：共 {Collections.Count} 个" : "资产库不可用（数据库打不开）。";
    }

    private void ReloadAll()
    {
        _allRows.Clear();
        Collections.Clear();
        if (IsDbAvailable)
        {
            _allRows.AddRange(_assets.QueryRows(limit: AssetAllViewModel.QueryLimit));
            foreach (var c in _cols.List()) Collections.Add(c);
        }

        OnPropertyChanged(nameof(HasCollections));
        if (SelectedCollection == null && Collections.Count > 0) SelectCollection(Collections[0]);
        else LoadMembers(SelectedCollection?.Id ?? 0);
    }

    private string StatusSummary()
        => IsDbAvailable
            ? $"共 {Collections.Count} 个集合" + (Collections.Count == 0 ? "（点「➕ 新建集合」开一个）" : "")
            : "资产库不可用（数据库打不开）—— 本页只读，不会崩。";

    /// <summary>点左边一个集合（命令走这里；<c>SelectCollection</c> 保持私有，供内部复用）。</summary>
    [RelayCommand]
    private void SelectCollectionItem(CollectionRow? row) => SelectCollection(row);

    private void SelectCollection(CollectionRow? row)
    {
        foreach (var c in Collections) c.IsSelected = ReferenceEquals(c, row);
        SelectedCollection = row;
        OnPropertyChanged(nameof(IsSmartSelected));
        OnPropertyChanged(nameof(CollectionHint));
        LoadMembers(row?.Id ?? 0);
        StatusText = row == null ? "未选中集合。" : $"{row.TypeIcon} {row.Name}：{Members.Count} 个成员";
    }

    private void LoadMembers(long collectionId)
    {
        Members.Clear();
        SelectedMember = null;
        if (collectionId > 0 && IsDbAvailable)
        {
            var ids = _cols.ListMemberIds(collectionId);
            foreach (var id in ids)
            {
                var row = _allRows.FirstOrDefault(r => r.Id == id);
                if (row != null) Members.Add(new AssetMember(row));
            }
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

    // ==================== 集合操作（逻辑操作，写 operation_logs） ====================

    [RelayCommand]
    private async Task NewCollectionAsync()
    {
        var name = await DialogHooks.InputAsync("新建集合（手动）", "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var id = _cols.Create(name.Trim(), CollectionTypes.Manual);
        if (id <= 0) { StatusText = "新建失败（库不可用？）。"; return; }

        _log.Log("collection", "collection", id, afterJson: $"{{\"name\":\"{name.Trim()}\",\"type\":\"manual\"}}");
        ReloadAll();
        SelectCollection(Collections.FirstOrDefault(c => c.Id == id));
        StatusText = $"已新建手动集合「{name.Trim()}」。";
    }

    /// <summary>新建**智能**集合（只存规则文本；规则求值未实装）。</summary>
    [RelayCommand]
    private async Task NewSmartAsync()
    {
        var name = await DialogHooks.InputAsync("新建智能集合（名字）", "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var rule = await DialogHooks.InputAsync("规则描述（先存文本；规则求值未实装）", "").ConfigureAwait(false);
        var id = _cols.Create(name.Trim(), CollectionTypes.Smart, $"{{\"desc\":\"{(rule ?? "").Replace("\"", "'")}\"}}");
        if (id <= 0) { StatusText = "新建失败（库不可用？）。"; return; }

        _log.Log("collection", "collection", id, afterJson: $"{{\"name\":\"{name.Trim()}\",\"type\":\"smart\"}}");
        ReloadAll();
        SelectCollection(Collections.FirstOrDefault(c => c.Id == id));
        StatusText = $"已新建智能集合「{name.Trim()}」（⚠️ 规则求值未实装）。";
    }

    [RelayCommand]
    private async Task RenameCollectionAsync()
    {
        if (SelectedCollection == null) { StatusText = "先选一个集合。"; return; }

        var row = SelectedCollection;
        var old = row.Name;
        var name = await DialogHooks.InputAsync("重命名集合", old).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == old) { StatusText = "已取消。"; return; }

        var ok = _cols.Rename(row.Id, name.Trim());
        if (ok) _log.Log("collection", "collection", row.Id, beforeJson: $"{{\"name\":\"{old}\"}}", afterJson: $"{{\"name\":\"{name.Trim()}\"}}");

        var keep = row.Id;
        ReloadAll();
        SelectCollection(Collections.FirstOrDefault(c => c.Id == keep));
        StatusText = ok ? $"已把「{old}」改名为「{name.Trim()}」。" : "改名失败。";
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync()
    {
        if (SelectedCollection == null) { StatusText = "先选一个集合。"; return; }

        var row = SelectedCollection;
        if (!await DialogHooks.ConfirmAsync($"删除集合「{row.Name}」？（⛔ 只删集合与成员关系，不删资产、不动磁盘文件）").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        var ok = _cols.Delete(row.Id);
        if (ok) _log.Log("collection", "collection", row.Id, beforeJson: $"{{\"name\":\"{row.Name}\"}}");
        ReloadAll();
        SelectCollection(Collections.FirstOrDefault());
        StatusText = ok ? $"已删除集合「{row.Name}」。" : "删除失败。";
    }

    // ==================== 成员操作 ====================

    /// <summary>成员上移一位（写 sort_order；与拖拽同效）。智能集合不允许改顺序。</summary>
    [RelayCommand]
    private void MoveUp() => Move(-1);

    /// <summary>成员下移一位。</summary>
    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedCollection == null || SelectedMember == null) { StatusText = "先在右边选中一个成员。"; return; }
        if (IsSmartSelected) { StatusText = "⚡ 智能集合的成员由规则决定，⛔ 不能手工排序。"; return; }

        var list = Members.ToList();
        var i = list.IndexOf(SelectedMember);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) { StatusText = "已经到边界了。"; return; }

        (list[i], list[j]) = (list[j], list[i]);
        var ok = _cols.Reorder(SelectedCollection.Id, list.Select(m => m.Id).ToList());
        _log.Log("collection", "collection", SelectedCollection.Id, afterJson: $"{{\"reorder\":\"{SelectedMember.Name}\"}}");

        var keep = SelectedMember;
        Members.Clear();
        foreach (var m in list) Members.Add(m);
        SelectedMember = keep;
        StatusText = ok ? $"已移动「{keep.Name}」。" : "排序没写进库（库不可用？）。";
    }

    /// <summary>把选中成员移出集合（⛔ 不删资产）。</summary>
    [RelayCommand]
    private async Task RemoveMemberAsync()
    {
        if (SelectedCollection == null || SelectedMember == null) { StatusText = "先在右边选中一个成员。"; return; }
        if (IsSmartSelected) { StatusText = "⚡ 智能集合的成员由规则决定，⛔ 不能手工摘出。"; return; }

        var coll = SelectedCollection;
        var member = SelectedMember;
        if (!await DialogHooks.ConfirmAsync($"把「{member.Name}」移出集合「{coll.Name}」？（⛔ 不删资产、⛔ 不动磁盘文件）").ConfigureAwait(false))
        { StatusText = "已取消。"; return; }

        var n = _cols.RemoveAssets(coll.Id, new List<long> { member.Id });
        if (n > 0) _log.Log("collection", "collection", coll.Id, afterJson: $"{{\"removedAssets\":{n}}}");

        var keep = coll.Id;
        ReloadAll();
        SelectCollection(Collections.FirstOrDefault(c => c.Id == keep));
        StatusText = $"已把「{member.Name}」移出集合（{n} 条）。";
    }
}
