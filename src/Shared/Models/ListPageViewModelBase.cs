using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
public abstract partial class ListPageViewModelBase : ObservableObject, IStatusProvider, IToolbarSource
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

    // ==================== 声明式工具条（v3.53 / NT-055：提到基类，Shell 统一承载） ====================

    /// <summary>
    /// 工具条内容（需求 §4.8.5）。**默认 <c>null</c> = 本页没有声明式工具条**（Shell ⛔ 不留空行，
    /// 页面继续用页内自绘工具条）；迁到 Shell 承载的页只需 <c>override</c> 本属性给出描述符列表。
    /// <para>🔴 与 <see cref="IStatusProvider"/> 同一套路：一次在基类实现，覆盖全部列表页。</para>
    /// <para>⚠️ 页面 <c>override</c> 后**必须删掉页内的 <c>Border.toolbar</c>**，否则会出现两条工具条。</para>
    /// </summary>
    public virtual IReadOnlyList<ToolbarItemDescriptor>? Toolbar => null;

    /// <summary>工具条构成变化时通知 Shell 重建（工具条静态的页用不着；给"项随状态增删"的页备用）。</summary>
    public event EventHandler? ToolbarChanged;

    /// <summary>工具条构成变了 → 通知 Shell（<see cref="Toolbar"/> 是动态构造的页才需要调）。</summary>
    protected void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 派生类必须给出的东西 ====================

    /// <summary>本页在 <c>config\columnlayout.json</c> 里的页键（如 <c>"video-list"</c>）。</summary>
    protected abstract string PageKey { get; }

    /// <summary>列元数据（顺序即表头与行的列顺序）。</summary>
    /// <para>⛔ **不要追加 <c>Detail</c> 占位列**（v3.12）：详情开关已从表头挪到工具条末位
    /// （<c>Button.detail-toggle</c>，见规范 §3.6），表头因此不再有那一格。</para>
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
        SeedStandardQuickFilters();   // v3.19 / NT-006：表头右键「筛选」子菜单的标准项（页有分类再 SetQuickFilters 覆盖）
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
        foreach (var c in Columns) map[c.Key] = Vis(c.Key);
        _cols.SavePage(PageKey, map);
    }

    /// <summary>把「IsXxxVisible」这批具名属性整体发一次通知（加载 / 复位后让 XAML 跟上）。</summary>
    protected void NotifyColumnsChanged()
    {
        foreach (var c in Columns) OnPropertyChanged("Is" + c.Key + "Visible");
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>
    /// v3.19 / NT-006：**必须始终可见的列**（右键菜单里不可隐藏、<see cref="VisibleColumnSpecs"/> 恒给它宽度）。
    /// 默认自动识别「名称 / 标题 / 主机」这类标识列；派生类可 override 明确指定。
    /// </summary>
    protected virtual string? MandatoryColumnKey
    {
        get
        {
            foreach (var c in Columns)
                if (c.Key is "Name" or "Title" or "Host"
                    || c.Header.Contains("名称") || c.Header.Contains("标题"))
                    return c.Key;
            return null;
        }
    }

    /// <summary>读某列当前是否可见（未在字典里 = 默认可见；标识列恒可见）。供派生类的具名属性直通。</summary>
    protected bool Vis(string key)
        => key == MandatoryColumnKey || !_visibility.TryGetValue(key, out var v) || v;

    // ==================== 列宽规格（表头与行共用同一份） ====================

    /// <summary>
    /// 「手动列宽」（v3.20，需求 §6.4.15）：列键 → 像素宽。拖表头边界产生，只存会话（⛔ 不落盘）。
    /// 不在这里的列 = 用 <see cref="Columns"/> 元数据里的星号宽度。
    /// </summary>
    private readonly Dictionary<string, double> _manualWidths = new();

    /// <summary>
    /// 「锁定列宽」（v3.22，需求 §4.8.1）：`ListColumn.Sample` 的示例文本宽 ≤ 表头宽 → 该列锁成表头宽（且不窄于 34px）。
    /// 锁定的列**不可拖拽**、不参与任何自动调整。
    /// </summary>
    private readonly Dictionary<string, double> _lockedWidths = new();

    /// <summary>
    /// v3.22：按「示例值宽 ≤ 表头宽」把列锁成表头宽（不可拖、不自动调）。
    /// 由 View 的 <c>ColumnGrid.HeaderMeasured</c> 事件转调（首次布局后一次性）。
    /// <para>锁定的宽度**不小于 <see cref="MinLockedWidth"/>** —— 表头文字很短的列也要留出空间，
    /// 让**边界把手始终显示、能拖**（用户要求：最小列宽也要能拖）。</para>
    /// </summary>
    public void LockColumnsBySample(IReadOnlyList<double>? headerWidths, IReadOnlyList<double>? sampleWidths)
    {
        if (headerWidths == null) return;

        var changed = false;
        for (var i = 0; i < Columns.Count && i < headerWidths.Count; i++)
        {
            var col = Columns[i];
            if (string.IsNullOrEmpty(col.Sample)) continue;          // 没给示例值 → 不参与判定
            var hw = headerWidths[i];
            if (hw <= 0) continue;                                   // 隐藏列 / 还没测到
            var sw = sampleWidths != null && i < sampleWidths.Count ? sampleWidths[i] : double.MaxValue;
            if (sw > hw) continue;                                   // 示例比表头还宽 → 不锁

            var w = Math.Max(hw, MinLockedWidth);
            if (_lockedWidths.TryGetValue(col.Key, out var old) && Math.Abs(old - w) <= 0.5) continue;
            _lockedWidths[col.Key] = w;
            changed = true;
        }
        if (!changed) return;

        OnPropertyChanged(nameof(VisibleColumnSpecs));
        OnPropertyChanged(nameof(LockedColumnMask));
    }

    /// <summary>锁定的最小列宽（px）—— 保证「已收藏」这类表头很短的列也留得下拖拽把手。</summary>
    public const double MinLockedWidth = 34;

    /// <summary>
    /// 示例行的文本（按列顺序，与 <see cref="Columns"/> 一一对齐）：给 View 的「水印行」绑
    /// <c>{Binding SampleRowTexts[i]}</c> 用；⛔ 不进 <c>Items</c>、不参与统计 / 导入导出。
    /// </summary>
    public string[] SampleRowTexts
    {
        get
        {
            var arr = new string[Columns.Count];
            for (var i = 0; i < Columns.Count; i++) arr[i] = Columns[i].Sample ?? "";
            return arr;
        }
    }

    /// <summary>有没有给任何列填过示例值（View 据此决定要不要画那条水印行）。</summary>
    public bool HasSampleRow => Columns.Any(c => !string.IsNullOrEmpty(c.Sample));

    /// <summary>
    /// 示例值文本串（';' 分隔，给表头 <c>ColumnGrid.SampleSpecs</c> 测量用；⛔ 别用逗号 —— 示例值里可能有逗号）。
    /// </summary>
    public string SampleSpecs => string.Join(';', Columns.Select(c => (c.Sample ?? "").Replace(';', ' ')));

    /// <summary>
    /// 锁定列的掩码串（给表头 <c>ColumnGrid.LockedColumns</c> 用）：每位 '1' = 该列已锁定（边界不放拖拽把手）。
    /// </summary>
    public string LockedColumnMask
    {
        get
        {
            var chars = new char[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
                chars[i] = _lockedWidths.ContainsKey(Columns[i].Key) ? '1' : '0';
            return new string(chars);
        }
    }

    private bool IsLocked(int index)
        => index >= 0 && index < Columns.Count && _lockedWidths.ContainsKey(Columns[index].Key);

    /// <summary>
    /// 拖动表头列边界 → 改这一列的像素宽（由 View 的 <c>ColumnGrid.ColumnResize</c> 事件转调）。
    /// <para>索引越界 / 同一列自己拖自己 → 忽略；改完只发 <see cref="VisibleColumnSpecs"/> 通知，
    /// 表头与所有行同规格重建（列索引永不错位）。</para>
    /// </summary>
    public void ResizeColumns(int leftIndex, double leftWidthPx, int rightIndex, double rightWidthPx)
    {
        if (leftIndex < 0 || rightIndex != leftIndex + 1) return;
        if (leftIndex >= Columns.Count || rightIndex >= Columns.Count) return;
        if (leftWidthPx < 0 || rightWidthPx < 0) return;
        // v3.21：锁定列（内容没超过表头宽）**不可拖拽** —— 两侧任一被锁就拒绝
        if (IsLocked(leftIndex) || IsLocked(rightIndex)) return;

        _manualWidths[Columns[leftIndex].Key] = leftWidthPx;
        _manualWidths[Columns[rightIndex].Key] = rightWidthPx;
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>
    /// 列宽规格串：可见列写它的宽度（手动像素优先，否则星号比例），隐藏列写 <c>"0"</c>（列索引保持不动 → 表头与行永不错位）。
    /// ⚠️ 必须做成字符串：<c>Grid.ColumnDefinitions</c> 是 CLR 属性，XAML 里不能写 {Binding}（AVLN3000），
    /// 故由各模块的 <c>ColumnGrid</c> 控件解析。
    /// </summary>
    public string VisibleColumnSpecs
    {
        get
        {
            var parts = new string[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
            {
                var key = Columns[i].Key;
                // 优先级：隐藏 0 → 锁定（表头宽）→ 手动（拖出来的）→ 星号元数据
                parts[i] = !Vis(key) ? "0"
                    : _lockedWidths.TryGetValue(key, out var lw)
                        ? ((int)Math.Round(lw)).ToString(CultureInfo.InvariantCulture)
                    : _manualWidths.TryGetValue(key, out var w)
                        ? ((int)Math.Round(w)).ToString(CultureInfo.InvariantCulture)
                        : Columns[i].Width;
            }
            return string.Join(",", parts);
        }
    }

    /// <summary>右键表头勾选 / 取消勾选某列（增删表头项）；结果落 <c>config\columnlayout.json</c>。标识列（名称）不可隐藏。</summary>
    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (key == MandatoryColumnKey) return;   // v3.19 / NT-006：标识列（名称）必须显示，忽略隐藏请求
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

    /// <summary>工具条末位的详情开关调它：展开 / 收起详情。</summary>
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

    /// <summary>可筛选的列（排除无标题的列）；给条件里的下拉框用。</summary>
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

    // ==================== 表头右键「🔎 筛选」子菜单的快捷项（v3.19 / NT-006） ====================

    /// <summary>表头右键「🔎 筛选」子菜单的快捷项（统一项：添加列 / 清空 + 本页分类值或占位）。</summary>
    public ObservableCollection<FilterQuickItem> QuickFilters { get; } = new();

    /// <summary>是否有快捷筛选项（XAML 据此在「占位提示」与「列表」间切换）。</summary>
    public bool HasQuickFilters => QuickFilters.Count > 0;

    // ==================== 详情卡示例态（v3.32 / 需求 §4.8.3） ====================

    /// <summary>
    /// 详情卡是否处于**示例态**：没有选中项、但本页给了示例数据（<see cref="HasSampleRow"/>）→
    /// 详情卡以**示例值**填充并**禁用全部交互控件**（避免用户去改 / 保存一份假数据）。
    /// <para>由派生类在"选中项变化"时调 <see cref="UpdateDetailSample"/> 维护，⛔ 不要手写赋值。</para>
    /// </summary>
    [ObservableProperty] private bool _isDetailSample;

    /// <summary>派生类在选中项变化时调：没有选中项且有示例数据 → 进示例态；否则退出。</summary>
    protected void UpdateDetailSample(bool hasSelection) => IsDetailSample = HasSampleRow && !hasSelection;

    /// <summary>示例态变了（派生类可 override：把自己的"详情正文可见性"等派生属性发一次通知）。</summary>
    protected virtual void OnDetailSampleChanged() { }

    /// <summary>示例态变化 → 通知派生类（<c>partial void</c> 只能写在定义类，故转一道虚方法）。</summary>
    partial void OnIsDetailSampleChanged(bool value) => OnDetailSampleChanged();

    /// <summary>没有分类的页，种子一次性建「＋ 添加筛选列 / 🧹 清空 / （占位）」三项。</summary>
    private void SeedStandardQuickFilters()
    {
        if (QuickFilters.Count > 0) return;
        QuickFilters.Add(new FilterQuickItem { Label = "＋ 添加筛选列…", Command = AddFilterCommand });
        QuickFilters.Add(new FilterQuickItem { Label = "🧹 清空筛选（全部列）", Command = ClearFiltersCommand });
        QuickFilters.Add(new FilterQuickItem { Label = "（本页暂无分类筛选）" });
        OnPropertyChanged(nameof(HasQuickFilters));
    }

    /// <summary>标准两项（各页通用）。</summary>
    private void AddStandardQuickFilters()
    {
        QuickFilters.Add(new FilterQuickItem { Label = "＋ 添加筛选列…", Command = AddFilterCommand });
        QuickFilters.Add(new FilterQuickItem { Label = "🧹 清空筛选（全部列）", Command = ClearFiltersCommand });
    }

    /// <summary>
    /// 派生类用：设置「筛选」子菜单的**分类**快捷项（如本页各分类 / 类别值）。
    /// 会连同标准两项（添加列 / 清空）一起重建。每项自带命令 —— 菜单项在弹出树里取不到页面 VM 的命令。
    /// </summary>
    protected void SetQuickFilters(IEnumerable<(string Label, string ColumnKey, string Value)> items)
        => SetQuickFilters(items.Select(i => (i.Label, i.ColumnKey, i.Value, (string?)null)));

    /// <summary>
    /// 带**比较方式**的版本（v3.21）：`op` 传 <c>null</c> = 等于；标签这类"逗号分隔串里含某个值"的
    /// 必须传 <see cref="FilterOperators.Contains"/>（用"等于"会把打了多个标签的行全筛掉）。
    /// </summary>
    protected void SetQuickFilters(IEnumerable<(string Label, string ColumnKey, string Value, string? Op)> items)
    {
        QuickFilters.Clear();
        AddStandardQuickFilters();
        foreach (var (label, key, value, op) in items)
        {
            var k = key; var v = value; var o = op;
            QuickFilters.Add(new FilterQuickItem
            {
                Label = label,
                ColumnKey = k,
                Value = v,
                Command = new RelayCommand(() => ApplyQuickFilterValue(k, v, o))
            });
        }
        OnPropertyChanged(nameof(HasQuickFilters));
    }

    /// <summary>应用一条快捷筛选：清掉同列旧条件后加一条「比较方式 = 等于 / 包含」（分类值用等于最准）。</summary>
    private void ApplyQuickFilterValue(string columnKey, string value, string? op = null)
    {
        EnsureFilterColumns();
        for (var i = Filters.Count - 1; i >= 0; i--)
            if (string.Equals(Filters[i].ColumnKey, columnKey, StringComparison.OrdinalIgnoreCase))
            {
                Filters[i].PropertyChanged -= OnClauseChanged;
                Filters.RemoveAt(i);
            }

        var col = FilterableColumns.FirstOrDefault(c => string.Equals(c.Key, columnKey, StringComparison.OrdinalIgnoreCase));
        if (col == null) { NotifyFilterUi(); RefreshView(); return; }

        var clause = new FilterClause { Column = col, Op = op ?? FilterOperators.Equal, Value = value, Enabled = true };
        clause.PropertyChanged += OnClauseChanged;
        Filters.Add(clause);
        NotifyFilterUi();
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
            if (c.Header.Length > 0) FilterableColumns.Add(c);
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
