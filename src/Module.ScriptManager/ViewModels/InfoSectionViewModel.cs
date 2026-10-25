using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.UI.Controls;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 信息面板里的**一个节** = 一个采集器（§三.8）。
/// </summary>
/// <remarks>
/// <para>
/// 节头：采集器标题 + 状态点（ok / warn / error <b>着色</b>）+ 上次采集时间 + 行数 + 折叠 + 局部刷新；
/// 节体：这一采集器**在本视图下的列**（<see cref="ScriptItem.Columns"/> 经视图投影后，§三.13），预览 20 行 + 「展开全部」。
/// </para>
/// <para>
/// 🔴 状态着色只能绑 <b>bool 具名属性</b>（XAML 绑不了枚举比较）：<see cref="IsOk"/> / <see cref="IsWarn"/> /
/// <see cref="IsError"/> / <see cref="IsIdle"/>，样式在本页 XAML 里用主题语义键给。
/// </para>
/// <para><see cref="Rows"/> 是**显示槽**（预览切片）；全部行在 <see cref="AllRows"/>，增量合并时复用行对象。</para>
/// </remarks>
public sealed partial class InfoSectionViewModel : ObservableObject
{
    /// <summary>每节预览行数（§三.8：预览 20 行 + 展开全部）。</summary>
    public const int PreviewRowLimit = 20;

    private readonly List<SysInfoRow> _all = new();

    /// <summary>
    /// 本节**在当前视图下的有效列**（<see cref="Services.ViewColumnProjection.Resolve"/> 的投影副本）。
    /// 🔴 不是 <see cref="ScriptItem.Columns"/> —— 后者是脚本能力，这里连显隐 / 标题 / 宽度都按视图算好了。
    /// </summary>
    private IReadOnlyList<ScriptColumn> _columns = Array.Empty<ScriptColumn>();

    public InfoSectionViewModel(ScriptItem item) : this(item, item.Columns ?? new List<ScriptColumn>()) { }

    public InfoSectionViewModel(ScriptItem item, IReadOnlyList<ScriptColumn> columns)
    {
        Item = item;
        _columns = columns;
        RebuildHeaders();

        RefreshCommand = new RelayCommand(() => RefreshRequested?.Invoke(this));
        ToggleCollapseCommand = new RelayCommand(ToggleCollapse);
        ToggleShowAllCommand = new RelayCommand(() => ShowAll = !ShowAll);
        ToggleColumnCommand = new RelayCommand<string?>(ToggleColumn);
        SortCommand = new RelayCommand<string?>(SortBy);
        OpenDetailCommand = new RelayCommand(() => DetailRequested?.Invoke(this));
    }

    // ==================== 所属条目 ====================
    public ScriptItem Item { get; }

    public string ItemId => Item.Id;

    /// <summary>节标题 = 条目标题（空则用目标文件名）。</summary>
    public string Title => string.IsNullOrWhiteSpace(Item.Title) ? Item.Target : Item.Title;

    /// <summary>节副标题（目标 / 类别，悬浮提示用）。</summary>
    public string Subtitle => string.IsNullOrWhiteSpace(Item.Category)
        ? Item.Target
        : Item.Category + " · " + Item.Target;

    // ==================== 回调（页面挂） ====================
    /// <summary>局部刷新（⟳）。</summary>
    public Action<InfoSectionViewModel>? RefreshRequested { get; set; }

    /// <summary>折叠状态变化（落视图定义的 <c>collapsed</c>）。</summary>
    public Action<InfoSectionViewModel>? CollapseChanged { get; set; }

    /// <summary>列显隐变化（写回列 schema + 落盘 + 重排）。</summary>
    public Action<InfoSectionViewModel, ScriptColumn>? ColumnToggled { get; set; }

    /// <summary>打开「节详情卡」（右键菜单；卡片按这一节渲染）。</summary>
    public Action<InfoSectionViewModel>? DetailRequested { get; set; }

    // ==================== 命令 ====================
    public ICommand RefreshCommand { get; }
    public ICommand ToggleCollapseCommand { get; }
    public ICommand ToggleShowAllCommand { get; }
    public ICommand ToggleColumnCommand { get; }
    public ICommand SortCommand { get; }
    public ICommand OpenDetailCommand { get; }

    // ==================== 状态 ====================
    [ObservableProperty] private string _status = SysInfoStatus.Idle;

    /// <summary>状态中文标签（正常 / 有警告 / 出错 / 未采集）。</summary>
    [ObservableProperty] private string _statusLabel = SysInfoStatus.Label(SysInfoStatus.Idle);

    /// <summary>上次采集时间（<c>HH:mm:ss</c>；空 = 还没采过）。</summary>
    [ObservableProperty] private string _capturedText = "";

    /// <summary>数据行数（全部，不是预览）。</summary>
    [ObservableProperty] private int _rowCount;

    [ObservableProperty] private bool _isCollapsed;
    [ObservableProperty] private bool _showAll;
    [ObservableProperty] private bool _isRefreshing;

    /// <summary>失败原因（节头提示 + 节体提示）。</summary>
    [ObservableProperty] private string _error = "";

    /// <summary>本节选中的行（详情卡）。</summary>
    [ObservableProperty] private SysInfoRow? _selected;

    // ---- 状态着色（XAML 绑 bool） ----
    public bool IsOk => Status == SysInfoStatus.Ok;
    public bool IsWarn => Status == SysInfoStatus.Warn;
    public bool IsError => Status == SysInfoStatus.Error;
    public bool IsIdle => Status == SysInfoStatus.Idle;

    partial void OnStatusChanged(string value)
    {
        StatusLabel = SysInfoStatus.Label(value);
        OnPropertyChanged(nameof(IsOk));
        OnPropertyChanged(nameof(IsWarn));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsIdle));
    }

    partial void OnShowAllChanged(bool value) => Project();

    partial void OnIsCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExpanded));
        CollapseChanged?.Invoke(this);
    }

    partial void OnIsRefreshingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

    /// <summary>折叠反向（XAML 里给节体的 <c>IsVisible</c>）。</summary>
    public bool IsExpanded => !IsCollapsed;

    /// <summary>空闲才允许再次刷新（§三.9 硬约束 3：前一次未完成 ⇒ 跳过本次）。</summary>
    public bool CanRefresh => !IsRefreshing;

    // ==================== 列 / 行 ====================
    public ObservableCollection<SysInfoRow> Rows { get; } = new();

    public ObservableCollection<SysInfoHeaderCell> HeaderCells { get; } = new();

    /// <summary>列宽规格串（隐藏列 = <c>0</c>，列索引保持不动）。</summary>
    public string ColumnSpecs => InfoRowFactory.Specs(_columns);

    /// <summary>有没有列（首次采集前为空 ⇒ 节体给「还没有列，先刷新一次」）。</summary>
    public bool HasColumns => _columns.Count > 0;

    public bool HasRows => _all.Count > 0;

    /// <summary>还有没显示的预览外行（按钮文案切换用）。</summary>
    public bool HasMore => _all.Count > PreviewRowLimit && !ShowAll;

    /// <summary>行数超过预览上限 ⇒ 「展开全部 / 只看前 20 行」按钮要一直在（收起也要能点）。</summary>
    public bool CanToggleShowAll => _all.Count > PreviewRowLimit;

    /// <summary>有列但这次没采到行 ⇒ 给一行提示（别让节体看起来像坏了）。</summary>
    public bool ShowNoRowsHint => HasColumns && _all.Count == 0;

    /// <summary>行数文案（「18 行」/「共 18 行」）。</summary>
    public string RowCountText => _all.Count == 0 ? "0 行" : _all.Count + " 行";

    /// <summary>节体空提示。</summary>
    public string EmptyBodyText => HasColumns ? "这次没有采集到行。" : "还没有列，先刷新一次。";

    /// <summary>「展开全部 / 收起」按钮文案。</summary>
    public string ShowAllText => ShowAll ? "⌃ 只看前 20 行" : $"⌄ 展开全部（{_all.Count} 行）";

    /// <summary>全部行（只读，页面统计 / 复制用）。</summary>
    public IReadOnlyList<SysInfoRow> AllRows => _all;

    /// <summary>整节就绪：有列才渲染表头。</summary>
    public IReadOnlyList<ScriptColumn> Columns => _columns;

    // ==================== 数据装载 ====================

    /// <summary>
    /// 换一套列投影（切视图时调）：重建表头 + 按新列重建各行单元格，<b>保留</b> RowKey / 状态 / 选中。
    /// </summary>
    public void ApplyColumns(IReadOnlyList<ScriptColumn> columns)
    {
        _columns = columns;

        var selKey = Selected?.RowKey;
        if (_all.Count > 0)
        {
            var rebuilt = new List<SysInfoRow>(_all.Count);
            foreach (var r in _all)
                rebuilt.Add(InfoRowFactory.Create(ItemId, r.RowKey, r.Status, _columns, InfoRowFactory.ToAttrs(r)));

            _all.Clear();
            _all.AddRange(rebuilt);
        }

        RebuildHeaders();

        if (_all.Count > 0)
            Selected = _all.FirstOrDefault(r => string.Equals(r.RowKey, selKey, StringComparison.OrdinalIgnoreCase)) ?? _all[0];

        Project();
    }

    /// <summary>重建表头（列 schema 变化后调）。</summary>
    public void RebuildHeaders()
    {
        HeaderCells.Clear();
        foreach (var cell in InfoRowFactory.BuildHeaders(_columns))
        {
            cell.ToggleCommand = ToggleColumnCommand;
            cell.SortCommand = SortCommand;
            HeaderCells.Add(cell);
        }
        OnPropertyChanged(nameof(HasColumns));
        OnPropertyChanged(nameof(ColumnSpecs));
        OnPropertyChanged(nameof(EmptyBodyText));
        OnPropertyChanged(nameof(ShowNoRowsHint));
    }

    /// <summary>
    /// 用一批「属性值」重建行集合（就地合并：已有 <see cref="SysInfoRow"/> 按 RowKey 复用 ⇒ 选中项不丢）。
    /// </summary>
    public void SetRows(IEnumerable<(string RowKey, string Status, IReadOnlyDictionary<string, string> Attrs)> rows)
    {
        var next = new List<SysInfoRow>();
        var existing = new Dictionary<string, SysInfoRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _all)
            if (!existing.ContainsKey(r.RowKey)) existing[r.RowKey] = r;

        foreach (var (rowKey, status, attrs) in rows)
        {
            if (existing.TryGetValue(rowKey, out var row))
            {
                InfoRowFactory.Apply(row, attrs);
                row.Status = status;
            }
            else
            {
                row = InfoRowFactory.Create(ItemId, rowKey, status, _columns, attrs);
            }
            next.Add(row);
        }

        _all.Clear();
        _all.AddRange(next);

        EnsureSelectedStillPresent();
        RowCount = _all.Count;
        OnPropertyChanged(nameof(RowCountText));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanToggleShowAll));
        OnPropertyChanged(nameof(ShowAllText));
        Project();
    }

    /// <summary>标记一条失败：保留上次数据，只把状态翻成 error（§三.13 最后一条）。</summary>
    public void MarkError(string message)
    {
        Status = SysInfoStatus.Error;
        Error = message;
    }

    /// <summary>标记成功（时间戳 / 行数由 <see cref="SetRows"/> 与调用方给）。</summary>
    public void MarkOk(string status, DateTime at)
    {
        Status = status;
        Error = "";
        CapturedText = at.ToString("HH:mm:ss");
    }

    private void EnsureSelectedStillPresent()
    {
        if (Selected != null && !_all.Contains(Selected)) Selected = null;
        if (Selected == null && _all.Count > 0) Selected = _all[0];
    }

    // ==================== 排序 / 投影 ====================

    private string _sortAttr = "";
    private bool _sortDescending;

    /// <summary>点本节表头：同列再点反向，换列升序。</summary>
    private void SortBy(string? attr)
    {
        if (string.IsNullOrWhiteSpace(attr)) return;
        if (string.Equals(_sortAttr, attr, StringComparison.OrdinalIgnoreCase))
            _sortDescending = !_sortDescending;
        else
        {
            _sortAttr = attr;
            _sortDescending = false;
        }
        UpdateHeaderTexts();
        Project();
    }

    private void UpdateHeaderTexts()
    {
        foreach (var h in HeaderCells)
        {
            var arrow = string.Equals(h.Attr, _sortAttr, StringComparison.OrdinalIgnoreCase)
                ? (_sortDescending ? " ▼" : " ▲")
                : "";
            h.Text = h.Title + arrow;
        }
    }

    /// <summary>把 <see cref="_all"/> 按排序 / 预览窗口投到 <see cref="Rows"/>（增量合并，不重建可视树）。</summary>
    private void Project()
    {
        IEnumerable<SysInfoRow> q = _all;

        if (_sortAttr.Length > 0)
        {
            var cmp = StringComparer.CurrentCultureIgnoreCase;
            q = _sortDescending ? q.OrderByDescending(r => r.Of(_sortAttr), cmp)
                                : q.OrderBy(r => r.Of(_sortAttr), cmp);
        }

        if (!ShowAll) q = q.Take(PreviewRowLimit);

        MergeInto(Rows, q.ToList());

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanToggleShowAll));
        OnPropertyChanged(nameof(ShowAllText));
        OnPropertyChanged(nameof(EmptyBodyText));
        OnPropertyChanged(nameof(ShowNoRowsHint));
    }

    /// <summary>行集合增量合并：已有行对象一律复用，只移动位置 / 增删差集（界面不闪）。</summary>
    private static void MergeInto(ObservableCollection<SysInfoRow> target, List<SysInfoRow> desired)
    {
        for (var i = target.Count - 1; i >= 0; i--)
            if (!desired.Contains(target[i])) target.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            var want = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], want)) continue;

            var at = target.IndexOf(want);
            if (at >= 0) target.Move(at, i);
            else target.Insert(i, want);
        }
    }

    // ==================== 折叠 / 列 ====================

    private void ToggleCollapse() => IsCollapsed = !IsCollapsed;

    /// <summary>切换某列显隐（本视图内生效，落盘由页面负责）。</summary>
    private void ToggleColumn(string? attr)
    {
        if (string.IsNullOrWhiteSpace(attr)) return;
        var col = _columns.FirstOrDefault(c => string.Equals(c.Key, attr, StringComparison.OrdinalIgnoreCase));
        if (col == null) return;

        col.Visible = !col.Visible;
        foreach (var h in HeaderCells)
            if (string.Equals(h.Attr, attr, StringComparison.OrdinalIgnoreCase)) h.IsVisible = col.Visible;
        foreach (var row in _all)
            foreach (var cell in row.Cells)
                if (string.Equals(cell.Attr, attr, StringComparison.OrdinalIgnoreCase)) cell.IsVisible = col.Visible;

        OnPropertyChanged(nameof(ColumnSpecs));
        ColumnToggled?.Invoke(this, col);
    }
}
