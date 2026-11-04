using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.ViewModels;

/// <summary>
/// 批量处理页的选中模型 —— **唯一真相在 VM**（<see cref="SelectedRows"/>），View 只负责回填高亮。
/// <para>
/// 交互口径与「视频列表」「媒体资产」一致：无修饰键 = 单选替换、`Ctrl` 增选 / 取消、`Shift` 连选、`Ctrl+A` 全选；
/// 修饰键接管在 <c>VideoBatchView.axaml.cs</c> 的**隧道**阶段完成（ListBox 在 Multiple 下单击默认是「追加」）。
/// </para>
/// <para>选中只在**当前列表**有效（刷新 / 排序后按 <c>Id</c> 认领）。</para>
/// </summary>
public partial class VideoBatchViewModel
{
    /// <summary>当前选中行（多选口径；单选时只有一条）。批量动作的对象 = 这里。</summary>
    public ObservableCollection<VideoTaskBase> SelectedRows { get; } = new();

    public int SelectedCount => SelectedRows.Count;

    /// <summary>「只跑选中 / 🗑 删除 / 📂 输出目录」的启用条件。</summary>
    public bool HasSelectedRows => SelectedRows.Count > 0;

    /// <summary>
    /// 列表正在重建（清空 → 逐项 Add）。View 的 <c>SelectionChanged</c> 回填要据此跳过，
    /// 否则重建过程中框架抛的「选中被清空」会把用户选好的一批冲掉。
    /// </summary>
    public bool IsSyncingView => _syncing;

    /// <summary>VM 侧改了选中 ⇒ 请 View 把 <c>SelectedItems</c> 回填成 <see cref="SelectedRows"/>。</summary>
    public event EventHandler? SelectionSyncRequested;

    private void RaiseSelectionSync() => SelectionSyncRequested?.Invoke(this, EventArgs.Empty);

    private void NotifySelectionMeta()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelectedRows));
        OnPropertyChanged(nameof(CanActOnSelection));
        OnPropertyChanged(nameof(HasSelection));
        UpdateSummaryText();          // 底栏「已选 N」跟着选中走
    }

    /// <summary>批量命令作用的对象：优先多选集合；只有详情卡的单条选中时退回它。</summary>
    private IReadOnlyList<VideoTaskBase> SelectionList()
        => SelectedRows.Count > 0 ? SelectedRows.ToList()
         : Selected != null ? new[] { Selected }
         : Array.Empty<VideoTaskBase>();

    /// <summary>View 的选中变化回填（点行 / Ctrl 增选 / Shift 连选 / Ctrl+A 全走这里）。</summary>
    public void SetSelectionFromView(IReadOnlyList<VideoTaskBase> rows, VideoTaskBase? clicked)
    {
        SelectedRows.Clear();
        foreach (var r in rows) SelectedRows.Add(r);

        NotifySelectionMeta();

        if (clicked != null) Selected = clicked;
        else if (SelectedRows.Count > 0) Selected = SelectedRows[^1];
    }

    /// <summary>列表重算后按 <c>Id</c> 认领选中项，再把选中回填给 View。</summary>
    private void ReconcileSelection()
    {
        if (SelectedRows.Count > 0)
        {
            var mapped = SelectedRows
                .Select(old => Items.FirstOrDefault(t => t.Id == old.Id)
                            ?? FinishedItems.FirstOrDefault(t => t.Id == old.Id))
                .Where(t => t != null)
                .Select(t => t!)
                .ToList();

            SelectedRows.Clear();
            foreach (var t in mapped) SelectedRows.Add(t);
        }

        if (Selected != null && !Items.Contains(Selected) && !FinishedItems.Contains(Selected))
            Selected = SelectedRows.Count > 0 ? SelectedRows[^1] : null;

        NotifySelectionMeta();
        RaiseSelectionSync();
    }

    /// <summary>右键「全选」：只选当前列表（上区）里的。</summary>
    [RelayCommand]
    private void SelectAll()
    {
        SelectedRows.Clear();
        foreach (var t in Items) SelectedRows.Add(t);
        if (Selected == null && Items.Count > 0) Selected = Items[0];

        NotifySelectionMeta();
        RaiseSelectionSync();
        StatusText = $"已全选 {Items.Count} 个任务。";
    }

    /// <summary>右键「取消选择」：清空选中（详情卡保留当前行，避免右栏突然空白）。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        SelectedRows.Clear();
        NotifySelectionMeta();
        RaiseSelectionSync();
        StatusText = "已取消选择。";
    }

    /// <summary>右键「反选」：当前列表里没选中的变成选中。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        var picked = SelectedRows.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        SelectedRows.Clear();
        foreach (var t in Items)
            if (!picked.Contains(t.Id)) SelectedRows.Add(t);

        NotifySelectionMeta();
        RaiseSelectionSync();
        StatusText = $"已反选，选中 {SelectedRows.Count} 个任务。";
    }

    /// <summary>
    /// ↺ 重新排队：把选中任务的状态清回「等待中」（含已完成的），下次批量运行就会再跑它们。
    /// <para>⛔ 不删任务、不动磁盘输出；只是明确表示"这条我还要再跑一次"。</para>
    /// </summary>
    [RelayCommand]
    private void RequeueSelected()
    {
        var rows = SelectionList();
        if (rows.Count == 0) { StatusText = "先选中要重新排队的任务。"; return; }
        if (IsRunning) { StatusText = "正在跑任务 —— 先「停止」再改状态。"; return; }

        var n = 0;
        foreach (var t in rows)
        {
            if (t.Status == VideoTaskStatus.Pending) continue;
            t.Reset();
            n++;
        }

        _store?.Save();
        RefreshView();
        StatusText = n == 0
            ? "选中的任务本来就在等待中，不用重置。"
            : $"已把 {n} 个任务重新排队（状态回到「等待中」）—— 下次批量运行会再跑它们。";
    }

    // ==================== 详情卡两分区的勾选框（与「视频预览」页一致，**可交互**） ====================

    /// <summary>「✂ 剪辑（截区间）」那段是否参与命令。</summary>
    [ObservableProperty] private bool _editIsCut;

    /// <summary>「🔁 转码（换编码 / 格式）」那段是否参与命令。</summary>
    [ObservableProperty] private bool _editIsTranscode;

    /// <summary>转换任务类型时关掉勾选回调（避免"改勾选 → 换任务 → 重填勾选"来回递归）。</summary>
    private bool _converting;

    partial void OnEditIsCutChanged(bool value)
    {
        IsCutSectionExpanded = value;              // 取消勾选即折叠（与预览页同一套联动）
        ApplyTaskKind();
    }

    partial void OnEditIsTranscodeChanged(bool value)
    {
        IsTranscodeSectionExpanded = value;
        ApplyTaskKind();
    }

}
