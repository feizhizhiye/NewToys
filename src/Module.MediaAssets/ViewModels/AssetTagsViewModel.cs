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
using NewToys.MediaAssets.Views.Detail;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「标签管理」页（NT-018）：标签列表 + 详情卡（用了该标签的资产）。
/// <para>
/// 逻辑操作（新建 / 改名 / 改色 / 合并 / 删除 / 清理未使用）**直接改库**并写 <c>operation_logs</c>
/// （<c>reversible=1</c>）—— ⛔ 不走任务队列（那是物理操作，NT-019）。
/// </para>
/// <para>⚠️ 标签是用户手动数据（R1 例外）：重建库会丢，靠 NT-022 的「导出用户数据」兜底。</para>
/// </summary>
public partial class AssetTagsViewModel : ListPageViewModelBase, IDetailPanelProvider
{
    private readonly TagService _tags = null!;
    private readonly OperationLogService _log = null!;
    private readonly AssetNavigationStore _store = null!;
    private readonly INavigationService? _nav;
    private readonly List<TagRow> _all = new();
    private TagDetailView? _detailView;

    /// <summary>详情卡回填中（保存要跳过这一次，⛔ 别把"显示"当成"用户改了"）。</summary>
    private bool _loadingDetail;

    public ObservableCollection<TagRow> Items { get; } = new();

    /// <summary>用了当前标签的资产（详情卡下半部分；点一下跳过去）。</summary>
    public ObservableCollection<TagAssetRef> DetailAssets { get; } = new();

    /// <summary>详情卡的色板（8 色；点一下即选，立即落库；⛔ XAML 不写死色值 → 用 <see cref="ColorSwatch"/> 带画刷）。</summary>
    public ObservableCollection<ColorSwatch> Palette { get; }
        = new(AssetPalette.Colors.Select(c => new ColorSwatch(c)));

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private TagRow? _selected;
    [ObservableProperty] private string _searchText = "";

    // ===== 详情卡编辑字段（「💾 保存」才落库） =====
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editNamespace = "";
    [ObservableProperty] private string _editColor = "";

    // ===== 表头 =====
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _namespaceHeader = "命名空间";
    [ObservableProperty] private string _colorHeader = "颜色";
    [ObservableProperty] private string _usageHeader = "使用数";
    [ObservableProperty] private string _createdHeader = "创建时间";

    // ===== 列显隐 =====
    public bool IsNameVisible => Vis("Name");
    public bool IsNamespaceVisible => Vis("Namespace");
    public bool IsColorVisible => Vis("Color");
    public bool IsUsageVisible => Vis("Usage");
    public bool IsCreatedVisible => Vis("Created");

    protected override string PageKey => "assets-tags";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2*", sample: "科幻"),
        new("Namespace", "命名空间", "1.2*", sample: "（默认）"),
        new("Color", "颜色", "1*", sample: "#4C8DFF"),
        new("Usage", "使用数", "0.9*", sample: "12 个"),
        new("Created", "创建时间", "1.4*", sample: "2026-10-14 05:20")
    };

    protected override string? MandatoryColumnKey => "Name";

    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    /// <summary>列键 → 值（⛔ 列键与属性名不一致的必须显式写，否则筛选项静默失效）。</summary>
    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not TagRow t) return base.FieldText(row, columnKey);
        return columnKey switch
        {
            "Name" => t.Name,
            "Namespace" => t.NamespaceText,
            "Color" => t.ColorHexText,
            "Usage" => t.UsageText,
            "Created" => t.CreatedText,
            _ => base.FieldText(row, columnKey)
        };
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;
    public bool ShowSampleRow => HasSampleRow && Items.Count == 0;
    public bool ShowDetailBody => HasSelection || IsDetailSample;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    /// <summary>详情卡里"用了这个标签的资产"有没有内容（空 → 显示提示）。</summary>
    public bool HasDetailAssets => DetailAssets.Count > 0;

    /// <summary>详情卡当前显示的标签（没选中 → 示例行）。</summary>
    public TagRow DetailTag => Selected ?? SampleTag;

    /// <summary>库是否可用（不可用照常显示 + 状态栏提示，⛔ 不崩）。</summary>
    public bool IsDbAvailable => _tags?.IsAvailable ?? false;

    /// <summary>详情卡示例标签（v3.32 §4.8.3：没数据时也能看清卡片长什么样）。</summary>
    private static readonly TagRow SampleTag = new()
    {
        Id = 0,
        Name = "科幻",
        Namespace = null,
        Color = null,
        UsageCount = 12,
        CreatedAt = "2026-10-14 05:20:00"
    };

    public AssetTagsViewModel() { }

    public AssetTagsViewModel(TagService tags, OperationLogService log, AssetNavigationStore store,
        IColumnLayoutService cols, INavigationService? nav = null)
        : base(cols)
    {
        _tags = tags;
        _log = log;
        _store = store;
        _nav = nav;

        LoadLayout();
        Reload();
        UpdateDetailSample(false);
        LoadDetail(null);
        StatusText = StatusSummary();
    }

    // ==================== 加载 ====================

    [RelayCommand]
    private void RefreshTags()
    {
        Reload();
        StatusText = IsDbAvailable ? $"已重读标签：共 {Items.Count} 个" : "资产库不可用（数据库打不开）。";
    }

    private void Reload()
    {
        _all.Clear();
        if (IsDbAvailable) foreach (var t in _tags.List()) _all.Add(t);

        var keep = Selected?.Id ?? 0;
        ApplyView();
        if (keep > 0) Selected = Items.FirstOrDefault(t => t.Id == keep) ?? Selected;
    }

    private string StatusSummary()
        => IsDbAvailable
            ? $"共 {Items.Count} 个标签" + (Items.Count == 0 ? "（点「➕ 新建标签」开一个）" : "")
            : "资产库不可用（数据库打不开）—— 本页只读，不会崩。";

    private void ApplyView()
    {
        IEnumerable<TagRow> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || t.NamespaceText.Contains(text, StringComparison.OrdinalIgnoreCase));

        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Namespace" => Order(q, t => t.NamespaceText),
            "Usage" => Order(q, t => t.UsageCount),
            "Created" => Order(q, t => t.CreatedAt),
            _ => Order(q, t => t.Name)
        };

        Items.Clear();
        foreach (var t in q) Items.Add(t);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowSampleRow));
    }

    private IOrderedEnumerable<TagRow> Order<TKey>(IEnumerable<TagRow> q, Func<TagRow, TKey> key)
        => SortDescending ? q.OrderByDescending(key) : q.OrderBy(key);

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        NamespaceHeader = "命名空间" + SortArrow("Namespace");
        ColorHeader = "颜色" + SortArrow("Color");
        UsageHeader = "使用数" + SortArrow("Usage");
        CreatedHeader = "创建时间" + SortArrow("Created");
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    // ==================== 逻辑操作（一律写 operation_logs） ====================

    /// <summary>新建标签（工具条「➕ 新建标签」）。</summary>
    [RelayCommand]
    private async Task NewTagAsync()
    {
        var name = await DialogHooks.InputAsync("新建标签", "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消（没输入名字）。"; return; }

        var id = _tags.Create(name.Trim());
        if (id <= 0) { StatusText = "新建失败（重名或库不可用）。"; return; }

        _log.Log("tag", "tag", id, afterJson: $"{{\"name\":\"{name.Trim()}\"}}");
        Reload();
        Selected = Items.FirstOrDefault(t => t.Id == id);
        StatusText = $"已新建标签「{name.Trim()}」。";
    }

    /// <summary>重命名（行右键）。</summary>
    [RelayCommand]
    private async Task RenameTagAsync()
    {
        if (Selected == null) { StatusText = "先选中一个标签。"; return; }

        var old = Selected.Name;
        var name = await DialogHooks.InputAsync("重命名标签", old).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == old) { StatusText = "已取消（名字没变）。"; return; }

        var ok = _tags.Rename(Selected.Id, name.Trim());
        if (ok) _log.Log("tag", "tag", Selected.Id, beforeJson: $"{{\"name\":\"{old}\"}}", afterJson: $"{{\"name\":\"{name.Trim()}\"}}");
        Reload();
        StatusText = ok ? $"已把「{old}」改名为「{name.Trim()}」。" : "改名失败（同命名空间下重名？）。";
    }

    /// <summary>改颜色（详情卡色板点选 → 立即落库）。</summary>
    [RelayCommand]
    private void SetColor(string? hex)
    {
        if (Selected == null || string.IsNullOrWhiteSpace(hex)) { StatusText = "先选中一个标签。"; return; }

        var name = Selected.Name;
        var ok = _tags.SetColor(Selected.Id, hex);
        if (ok) _log.Log("tag", "tag", Selected.Id, afterJson: $"{{\"color\":\"{hex}\"}}");
        EditColor = hex!;
        Reload();
        StatusText = ok ? $"已把「{name}」的颜色改成 {hex}。" : "改色失败。";
    }

    /// <summary>改颜色（行右键「🎨 改颜色…」）：手输 #RRGGBB。详情卡上的色板走 <see cref="SetColor"/>。</summary>
    [RelayCommand]
    private async Task ChangeColorAsync()
    {
        if (Selected == null) { StatusText = "先选中一个标签。"; return; }

        var name = Selected.Name;
        var input = await DialogHooks.InputAsync("改成什么颜色？（#RRGGBB）", Selected.ColorHex).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(input)) { StatusText = "已取消。"; return; }

        var hex = input.Trim();
        if (!hex.StartsWith("#", StringComparison.Ordinal)) hex = "#" + hex;
        if (hex.Length != 7) { StatusText = "颜色格式不对（要 #RRGGBB）。"; return; }

        var ok = _tags.SetColor(Selected.Id, hex);
        if (ok) _log.Log("tag", "tag", Selected.Id, afterJson: $"{{\"color\":\"{hex}\"}}");
        EditColor = hex;
        Reload();
        StatusText = ok ? $"已把「{name}」的颜色改成 {hex}。" : "改色失败。";
    }

    /// <summary>合并（行右键「合并到…」）：输入目标标签名，把当前标签的挂接转过去后删掉自己。</summary>
    [RelayCommand]
    private async Task MergeTagAsync()
    {
        if (Selected == null) { StatusText = "先选中要合并掉的标签。"; return; }

        var src = Selected;
        var target = await DialogHooks.InputAsync("合并到哪个标签？（输入目标标签名）", "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(target)) { StatusText = "已取消。"; return; }

        var dst = _all.FirstOrDefault(t => string.Equals(t.Name, target.Trim(), StringComparison.OrdinalIgnoreCase));
        if (dst == null) { StatusText = $"没找到标签「{target.Trim()}」—— 先建一个再合并。"; return; }
        if (dst.Id == src.Id) { StatusText = "不能合并到自己。"; return; }

        var ok = _tags.Merge(src.Id, dst.Id);
        if (ok) _log.Log("tag", "tag", dst.Id, beforeJson: $"{{\"mergedFrom\":\"{src.Name}\",\"fromId\":{src.Id}}}");
        Reload();
        StatusText = ok ? $"已把「{src.Name}」合并进「{dst.Name}」。" : "合并失败。";
    }

    /// <summary>删除标签（行右键；二次确认）。</summary>
    [RelayCommand]
    private async Task DeleteTagAsync()
    {
        if (Selected == null) { StatusText = "先选中一个标签。"; return; }

        var row = Selected;
        var warn = row.UsageCount > 0
            ? $"「{row.Name}」正在被 {row.UsageCount} 个资产使用，删除后这些资产就失去该标签（⛔ 不动磁盘文件）。继续？"
            : $"删除标签「{row.Name}」？";
        if (!await DialogHooks.ConfirmAsync(warn).ConfigureAwait(false)) { StatusText = "已取消。"; return; }

        var ok = _tags.Delete(row.Id);
        if (ok) _log.Log("tag", "tag", row.Id, beforeJson: $"{{\"name\":\"{row.Name}\"}}");
        Reload();
        StatusText = ok ? $"已删除标签「{row.Name}」。" : "删除失败。";
    }

    /// <summary>清理未使用标签（工具条；二次确认）。</summary>
    [RelayCommand]
    private async Task DeleteUnusedAsync()
    {
        var unused = _all.Count(t => t.IsUnused);
        if (unused == 0) { StatusText = "没有未使用的标签。"; return; }
        if (!await DialogHooks.ConfirmAsync($"删除 {unused} 个未使用的标签？").ConfigureAwait(false)) { StatusText = "已取消。"; return; }

        var n = _tags.DeleteUnused();
        _log.Log("tag", "tag", null, afterJson: $"{{\"deletedUnused\":{n}}}");
        Reload();
        StatusText = $"已清理 {n} 个未使用的标签。";
    }

    // ==================== 详情卡 ====================

    partial void OnSelectedChanged(TagRow? value)
    {
        UpdateDetailSample(value != null);
        OnPropertyChanged(nameof(DetailTag));
        LoadDetail(value);
        OnPropertyChanged(nameof(HasSelection));
        RaiseDetailChanged();
    }

    private void LoadDetail(TagRow? row)
    {
        _loadingDetail = true;
        try
        {
            DetailAssets.Clear();
            var target = row ?? (HasSampleRow ? SampleTag : null);
            EditName = target?.Name ?? "";
            EditNamespace = target?.Namespace ?? "";
            EditColor = target?.ColorHex ?? "";

            if (target == null || target.Id <= 0) { OnPropertyChanged(nameof(HasDetailAssets)); return; }
            foreach (var (id, name) in _tags.ListAssetsOfTag(target.Id)) DetailAssets.Add(new TagAssetRef(id, name));
            OnPropertyChanged(nameof(HasDetailAssets));
        }
        finally { _loadingDetail = false; }
    }

    /// <summary>详情卡「💾 保存」：把名称 / 命名空间 / 颜色一次落库。</summary>
    [RelayCommand]
    private void SaveDetail()
    {
        if (_loadingDetail || Selected == null) { StatusText = "先在左侧选中一个标签。"; return; }

        var name = (EditName ?? "").Trim();
        if (name.Length == 0) { StatusText = "名称不能为空。"; return; }

        var before = $"{{\"name\":\"{Selected.Name}\",\"color\":\"{Selected.ColorHex}\"}}";
        var okName = name == Selected.Name || _tags.Rename(Selected.Id, name);
        var okColor = _tags.SetColor(Selected.Id, EditColor);

        _log.Log("tag", "tag", Selected.Id, beforeJson: before,
            afterJson: $"{{\"name\":\"{name}\",\"color\":\"{EditColor}\"}}");

        Reload();
        StatusText = okName && okColor ? $"已保存标签「{name}」。" : "部分字段保存失败（重名？）。";
    }

    /// <summary>详情卡里点某个资产 → 跳到「全部资产」并选中它（跨页投递；取件点在 OnAttachedToVisualTree）。</summary>
    [RelayCommand]
    private void OpenAsset(TagAssetRef? item)
    {
        if (item == null) return;
        _store.Request("assets-all", item.Id);
        _nav?.NavigateTo("assets-all");
        StatusText = $"正在打开资产「{item.Name}」…";
    }

    // ==================== 详情面板（Shell 承载） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    public object? DetailContent
    {
        get
        {
            if (_detailView == null)
            {
                _detailView = new TagDetailView();
                _detailView.DataContext = this;
            }
            return _detailView;
        }
    }

    public event EventHandler? DetailChanged;

    /// <summary>工具条末位开关：⛔ 别绑基类 <c>ToggleDetailCommand</c>（它不通知 Shell，右列不会出现）。</summary>
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
