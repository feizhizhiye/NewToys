using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「全部资产」的选中模型 —— **唯一真相在 VM**（<see cref="SelectedRows"/>），View 只负责回填高亮。
/// <para>
/// 交互口径与资源管理器一致：无修饰键 = 单选替换、`Ctrl` 增选 / 取消、`Shift` 连选、`Ctrl+A` 全选；
/// 修饰键接管在 <c>AssetAllView.axaml.cs</c> 的**隧道**阶段完成（ListBox 的默认逻辑不认修饰键）。
/// </para>
/// <para>分页口径沿用勾选时代：选中**只在当前页**有效（翻页 / 刷新即清）。</para>
/// </summary>
public partial class AssetAllViewModel
{
    /// <summary>当前选中行（多选口径；单选时只有一条）。批量动作的对象 = 这里。</summary>
    public ObservableCollection<AssetRow> SelectedRows { get; } = new();

    public int SelectedCount => SelectedRows.Count;

    /// <summary>批量按钮的启用条件（加标签 / 加集合 / 移入回收站 / 从库移除 / 读媒体信息）。</summary>
    public bool HasSelectedRows => SelectedRows.Count > 0;

    /// <summary>VM 侧改了选中 ⇒ 请 View 把 <c>SelectedItems</c> 回填成 <see cref="SelectedRows"/>。</summary>
    public event EventHandler? SelectionSyncRequested;

    private void RaiseSelectionSync() => SelectionSyncRequested?.Invoke(this, EventArgs.Empty);

    private void NotifySelectionMeta()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelectedRows));
    }

    /// <summary>
    /// View 的选中变化回填（点行 / Ctrl 增选 / Shift 连选 / Ctrl+A 全走这里）。
    /// <paramref name="clicked"/> = 本次点击的行（详情卡跟它走）；为 null 时取最后一条。
    /// </summary>
    public void SetSelectionFromView(IReadOnlyList<AssetRow> rows, AssetRow? clicked)
    {
        SelectedRows.Clear();
        foreach (var r in rows) SelectedRows.Add(r);

        NotifySelectionMeta();

        if (clicked != null) Selected = clicked;
        else if (SelectedRows.Count > 0) Selected = SelectedRows[^1];
    }

    /// <summary>
    /// 列表重建后按 Id 认领选中项（<see cref="Items"/> 里都是新实例），再把选中回填给 View。
    /// 🔴 不做这一步 ⇒ 翻页 / 刷新后高亮全丢、详情卡与列表对不上。
    /// </summary>
    private void ReconcileSelection()
    {
        var mapped = SelectedRows
            .Select(old => Items.FirstOrDefault(i => i.Id == old.Id))
            .Where(r => r != null)
            .Select(r => r!)
            .ToList();

        SelectedRows.Clear();
        foreach (var r in mapped) SelectedRows.Add(r);

        // 详情卡：选中项换了实例 ⇒ 按 Id 认领；真不在本页才落到第一条
        var detail = Selected == null ? null : Items.FirstOrDefault(i => i.Id == Selected.Id);
        Selected = detail
                   ?? (SelectedRows.Count > 0 ? SelectedRows[^1] : (Items.Count > 0 ? Items[0] : null));

        // 「高亮 == 详情 == 批量对象」三者口径一致：详情卡有内容就必须有一行被选中
        if (SelectedRows.Count == 0 && Selected != null) SelectedRows.Add(Selected);

        NotifySelectionMeta();
        RaiseSelectionSync();
    }

    /// <summary>右键「☑ 全选当前页」：只选当前页（与勾选时代的分页口径一致）。</summary>
    [RelayCommand]
    private void SelectAll()
    {
        SelectedRows.Clear();
        foreach (var r in Items) SelectedRows.Add(r);
        Selected = Items.Count > 0 ? Items[0] : null;

        NotifySelectionMeta();
        RaiseSelectionSync();
        StatusText = $"已全选当前页 {Items.Count} 条。";
    }

    /// <summary>右键「☐ 取消选择」：清空选中（详情卡保留当前行，避免右栏突然空白）。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        SelectedRows.Clear();
        NotifySelectionMeta();
        RaiseSelectionSync();
        StatusText = "已取消选择。";
    }

    /// <summary>右键「⇄ 反选当前页」：当前页没选中的变成选中，已选中的去掉。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        var picked = SelectedRows.Select(r => r.Id).ToHashSet();
        SelectedRows.Clear();
        foreach (var r in Items)
            if (!picked.Contains(r.Id)) SelectedRows.Add(r);

        NotifySelectionMeta();
        RaiseSelectionSync();
        StatusText = $"已反选当前页，选中 {SelectedRows.Count} 条。";
    }

    /// <summary>批量动作的操作对象（未选 ⇒ 空表，调用方据此给提示）。</summary>
    private List<AssetRow> BatchRows() => SelectedRows.ToList();

    /// <summary>把一批行装进 <c>_all</c>（⛔ 别直接 <c>_all.Add</c>：草稿态 / 库态都要走同一条路）。</summary>
    private void AddRows(IEnumerable<AssetRow> rows)
    {
        foreach (var r in rows) _all.Add(r);
    }
}
