using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;

namespace NewToys.Shared.Models;

/// <summary>
/// 「列表页」ViewModel 的公共基类：把<b>五个列表页重复过五遍</b>的那套逻辑收在一处
/// （列显隐 + 详情栏开合 + 排序状态 + 多项筛选），派生类只留自己独有的部分。
/// <para>
/// <b>派生类要做什么</b>：
/// <list type="number">
///   <item>给出 <see cref="PageKey"/>（列显隐偏好落 <c>config\columnlayout.json</c> 的页键）与 <see cref="Columns"/>（列元数据）；</item>
///   <item>构造函数里调一次 <see cref="LoadLayout"/>（<see cref="Columns"/> 之后），把上次的偏好读回来；</item>
///   <item>实现 <see cref="RefreshView"/> —— 基类在「排序列变了 / 筛选条件变了」时调它；</item>
///   <item>为每个可隐藏的列写一个直通属性 <c>public bool IsXxxVisible =&gt; Vis("Xxx");</c>（XAML 只能绑具名属性，绑不了字典）；</item>
///   <item>要用筛选就实现 <see cref="FieldText"/>（可选再实现 <see cref="FieldNumber"/>）。</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ 为什么列可见性用「具名属性 + 字典」两套表示：字典用来落盘与算列宽规格，
/// 具名属性只给 XAML 绑 <c>IsVisible</c>；<see cref="ToggleColumn"/> 翻转后会主动
/// 为 <c>"Is" + key + "Visible"</c> 发一次通知，所以列键必须与该属性名对得上。
/// </para>
/// </summary>
public abstract partial class ListPageViewModelBase : ObservableObject, IStatusProvider
{
    private readonly IColumnLayoutService? _cols;

    /// <summary>列可见性（只有被显式保存过的列才在字典里；不在字典里的列 = 默认可见）。</summary>
    private readonly Dictionary<string, bool> _visibility = new();

    private string _sortColumn = "";
    private bool _sortDescending;

    protected ListPageViewModelBase() { }

    protected ListPageViewModelBase(IColumnLayoutService? cols) => _cols = cols;

    // ==================== 底部状态栏（v3.9：提到基类，Shell 统一承载） ====================

    /// <summary>
    /// 页面状态文本。
    /// <para>
    /// ⚠️ v3.9 起**提到基类**：各列表页**不要再自己声明** <c>[ObservableProperty] private string _statusText</c>
    /// （同名会隐藏基类属性、且事件不会触发）—— 页面里继续正常写 <c>StatusText = "…"</c> 即可。
    /// </para>
    /// <para>Shell 的底部状态栏通过 <see cref="IStatusProvider"/> 消费它。</para>
    /// </summary>
    [ObservableProperty] private string _statusText = "就绪";

    /// <summary>状态文本变化 → 通知 Shell 刷新底栏。</summary>
    public event EventHandler? StatusChanged;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 派生类必须给出的东西 ====================

    /// <summary>本页在 <c>config\columnlayout.json</c> 里的页键（如 <c>"video-list"</c>）。</summary>
    protected abstract string PageKey { get; }

    /// <summary>列元数据（顺序即表头与行的列顺序）。最后一列通常是给「▤」按钮留的窄列 Detail。</summary>
    protected abstract IReadOnlyList<ListColumn> Columns { get; }

    /// <summary>排序列 / 筛选条件变化后重算列表（派生类的 ApplyView / ApplyFilters）。</summary>
    protected abstract void RefreshView();

    /// <summary>
    /// 取某行某列的文本（给筛选用）。
    /// <para>
    /// <b>默认实现 = 反射取同名属性</b>：列键（<c>"Name"</c>/<c>"Url"</c>…）通常就是模型上的属性名，
    /// 于是新列表页只要继承基类就自动能筛选，不必再写一遍取值 switch；
    /// 列键与属性名不一致时返回 <c>null</c>，<see cref="ApplyFilters"/> 会把这条条件当"不生效"跳过（不会误筛掉行）。
    /// 需要更精确的值（派生显示串 / 数值）时由派生类重写。
    /// </para>
    /// </summary>
    protected virtual string? FieldText(object row, string columnKey)
    {
        if (row == null || string.IsNullOrEmpty(columnKey)) return null;
        try
        {
            var p = row.GetType().GetProperty(columnKey);
            return p?.GetValue(row)?.ToString();
        }
        catch { return null; }
    }

    /// <summary>取某行某列的数值（给「大于 / 小于」筛选用）。默认不支持数值比较。</summary>
    protected virtual double? FieldNumber(object row, string columnKey) => null;

    // ==================== 初始化 ====================

    /// <summary>
    /// 读回上次保存的列显隐与详情栏开合状态。
    /// ⚠️ 必须等派生类的 <see cref="Columns"/> 就绪后再调（所以放在派生构造函数里，而不是基类构造里）。
    /// </summary>
    protected void LoadLayout()
    {
        LoadVisibility();
        IsDetailVisible = _cols?.LoadFlag(PageKey, IColumnLayoutService.FlagDetailPanel) ?? false;
    }

    private void LoadVisibility()
    {
        if (_cols == null) return;
        var saved = _cols.LoadPage(PageKey);
        if (saved.Count == 0) return;
        foreach (var c in Columns)
            if (saved.TryGetValue(c.Key, out var v)) _visibility[c.Key] = v;
        NotifyColumnsChanged();
    }

    private void SaveVisibility()
    {
        if (_cols == null) return;
        var map = new Dictionary<string, bool>();
        foreach (var c in Columns)
            if (c.Key != "Detail") map[c.Key] = Vis(c.Key);
        _cols.SavePage(PageKey, map);
    }

    /// <summary>把「IsXxxVisible」这批具名属性整体发一次通知（加载 / 复位后让 XAML 跟上）。</summary>
    protected void NotifyColumnsChanged()
    {
        foreach (var c in Columns)
            if (c.Key != "Detail") OnPropertyChanged("Is" + c.Key + "Visible");
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>读某列当前是否可见（未在字典里 = 默认可见）。供派生类的具名属性直通。</summary>
    protected bool Vis(string key) => !_visibility.TryGetValue(key, out var v) || v;

    // ==================== 列宽规格（表头与行共用同一份） ====================

    /// <summary>
    /// 列宽规格串：可见列写它的星号宽度，隐藏列写 <c>"0"</c>（列索引保持不动 → 表头与行永不错位）。
    /// ⚠️ 必须做成字符串：<c>Grid.ColumnDefinitions</c> 是 CLR 属性，XAML 里不能写 {Binding}（AVLN3000），
    /// 故由各模块的 <c>ColumnGrid</c> 控件解析。
    /// </summary>
    public string VisibleColumnSpecs
    {
        get
        {
            var parts = new string[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
                parts[i] = Vis(Columns[i].Key) ? Columns[i].Width : "0";
            return string.Join(",", parts);
        }
    }

    /// <summary>右键表头勾选 / 取消勾选某列（增删表头项）；结果落 <c>config\columnlayout.json</c>。</summary>
    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _visibility[key] = !Vis(key);
        SaveVisibility();
        OnPropertyChanged("Is" + key + "Visible");
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>把某一列强制设为可见 / 不可见（派生类初始化时用）。</summary>
    protected void SetColumnVisible(string key, bool visible)
    {
        _visibility[key] = visible;
        OnPropertyChanged("Is" + key + "Visible");
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    // ==================== 右栏详情开合 ====================

    /// <summary>是否显示右侧详情栏。<b>默认收起</b>；用户的点选落盘，下次进本页保持。</summary>
    [ObservableProperty] private bool _isDetailVisible;

    /// <summary>展开详情前给派生类一次「没选中就选第一项」的机会。</summary>
    protected virtual void EnsureDetailSelection() { }

    /// <summary>表头右侧「▤」按钮：展开 / 收起详情。</summary>
    [RelayCommand]
    private void ToggleDetail()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible) EnsureDetailSelection();
        SaveDetailFlag();
    }

    /// <summary>右键「▤ 显示详情栏」：确保展开并定位到当前选中项。</summary>
    [RelayCommand]
    private void ShowDetail()
    {
        EnsureDetailSelection();
        IsDetailVisible = true;
        SaveDetailFlag();
    }

    /// <summary>把「详情栏是否展开」落盘（派生类在构造函数 / 建任务后想直接记住状态时用）。</summary>
    protected void SaveDetailFlag()
        => _cols?.SaveFlag(PageKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);

    // ==================== 排序 ====================

    protected string SortColumn => _sortColumn;
    protected bool SortDescending => _sortDescending;

    /// <summary>直接指定当前排序（派生类从配置里恢复上次排序列时用；不落盘、不重排）。</summary>
    protected void SetSort(string column, bool descending)
    {
        _sortColumn = column ?? "";
        _sortDescending = descending;
    }

    /// <summary>表头排序箭头的后缀（派生类拼表头文案时用）。</summary>
    protected string SortArrow(string column)
        => string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase)
            ? (_sortDescending ? " ▼" : " ▲")
            : "";

    /// <summary>左键点表头：同一列再点一次 = 反向；换列 = 升序（<paramref name="descFirst"/> 指定首点即降序的列）。</summary>
    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;

        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase))
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column == "Favorite" || column == "Pinned";   // ★ / 📌 这类：首点即把「有」的排前面
        }
        RefreshView();
    }

    // ==================== 多项筛选 ====================

    /// <summary>筛选条件（可任意添加多条，全部满足才显示）。</summary>
    public ObservableCollection<FilterClause> Filters { get; } = new();

    /// <summary>可筛选的列（排除「▤」窄列与无标题的列）；给条件里的下拉框用。</summary>
    public ObservableCollection<ListColumn> FilterableColumns { get; } = new();

    /// <summary>供 XAML 里「＋ 条件」按钮与筛选摘要显示用。</summary>
    public bool HasFilters => Filters.Count > 0;

    public int ActiveFilterCount => Filters.Count(f => f.IsActive);

    public string FilterSummary => ActiveFilterCount == 0
        ? "筛选：未启用"
        : $"筛选：{ActiveFilterCount} 条生效（{Filters.Count} 条已添加）";

    /// <summary>建一条新的筛选条件（默认取第一个可筛选列、「包含」、空值 = 暂不生效）。</summary>
    [RelayCommand]
    private void AddFilter()
    {
        EnsureFilterColumns();
        if (FilterableColumns.Count == 0) return;

        var clause = new FilterClause { Column = FilterableColumns[0] };
        clause.PropertyChanged += OnClauseChanged;
        Filters.Add(clause);
        NotifyFilterUi();
    }

    [RelayCommand]
    private void RemoveFilter(FilterClause? clause)
    {
        if (clause == null) return;
        clause.PropertyChanged -= OnClauseChanged;
        Filters.Remove(clause);
        NotifyFilterUi();
        RefreshView();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        ResetFilters();
        RefreshView();
    }

    /// <summary>清空全部筛选条件但不重算列表（派生类要在同一个流程里连续做几件事时用）。</summary>
    protected void ResetFilters()
    {
        foreach (var f in Filters) f.PropertyChanged -= OnClauseChanged;
        Filters.Clear();
        NotifyFilterUi();
    }

    private void OnClauseChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyFilterUi();
        RefreshView();
    }

    private void NotifyFilterUi()
    {
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterSummary));
    }

    /// <summary>把可筛选的列填进下拉框的数据源（第一次加条件时做一次）。</summary>
    private void EnsureFilterColumns()
    {
        if (FilterableColumns.Count > 0) return;
        foreach (var c in Columns)
            if (c.Key != "Detail" && c.Header.Length > 0) FilterableColumns.Add(c);
    }

    /// <summary>
    /// 按当前筛选条件过滤一批行。<b>条件之间是「且」</b>；没生效的条件（空值 / 未勾选）自动跳过。
    /// </summary>
    protected IEnumerable<T> ApplyFilters<T>(IEnumerable<T> rows)
    {
        var active = Filters.Where(f => f.IsActive).ToList();
        if (active.Count == 0) return rows;

        return rows.Where(row => active.All(f =>
        {
            var text = FieldText(row!, f.ColumnKey);
            // 这一列取不到值（列键与模型属性对不上）→ 这条条件不生效，而不是把所有行都筛掉
            if (text == null) return true;
            return f.Matches(text, FieldNumber(row!, f.ColumnKey));
        }));
    }

    /// <summary>筛选 + 关键字搜索之后剩下的行数（派生类拼统计文案时用）。</summary>
    protected int CountFiltered<T>(IEnumerable<T> rows) => ApplyFilters(rows).Count();
}
