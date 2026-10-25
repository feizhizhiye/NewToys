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
        ResetToFirstPage();   // 排序变了回第一页（⛔ 不停在旧顺序的第 N 页）
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

}
