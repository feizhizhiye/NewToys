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

public abstract partial class ListPageViewModelBase : ObservableObject, IStatusProvider, IToolbarSource
{

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
        ResetToFirstPage();
    }

    // ==================== 表头右键「🔎 筛选」子菜单的快捷项（v3.19） ====================

    /// <summary>表头右键「🔎 筛选」子菜单的快捷项（统一项：添加列 / 清空 + 本页分类值或占位）。</summary>
    public ObservableCollection<FilterQuickItem> QuickFilters { get; } = new();

    /// <summary>是否有快捷筛选项（XAML 据此在「占位提示」与「列表」间切换）。</summary>
    public bool HasQuickFilters => QuickFilters.Count > 0;

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
        if (col == null) { NotifyFilterUi(); ResetToFirstPage(); return; }

        var clause = new FilterClause { Column = col, Op = op ?? FilterOperators.Equal, Value = value, Enabled = true };
        clause.PropertyChanged += OnClauseChanged;
        Filters.Add(clause);
        NotifyFilterUi();
        ResetToFirstPage();
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
        ResetToFirstPage();
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
