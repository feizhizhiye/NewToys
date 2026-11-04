using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.ViewModels;

/// <summary>
/// 视频列表的选中模型 —— **唯一真相在 VM**（<see cref="SelectedRows"/>），View 只负责回填高亮。
/// <para>
/// 交互口径与「媒体资产 → 全部资产」一致：无修饰键 = 单选替换、`Ctrl` 增选 / 取消、`Shift` 连选、`Ctrl+A` 全选；
/// 修饰键接管在 <c>VideoListView.axaml.cs</c> 的**隧道**阶段完成（ListBox 默认在 Multiple 下单击是「追加」）。
/// </para>
/// <para>选中只在**当前列表**有效（翻页 / 重扫后按路径认领）。</para>
/// </summary>
public partial class VideoListViewModel
{
    /// <summary>当前选中行（多选口径；单选时只有一条）。批量动作的对象 = 这里。</summary>
    public ObservableCollection<VideoItem> SelectedRows { get; } = new();

    public int SelectedCount => SelectedRows.Count;

    /// <summary>「➡ 发送到批量处理」的启用条件。</summary>
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
    }

    /// <summary>
    /// View 的选中变化回填（点行 / Ctrl 增选 / Shift 连选 / Ctrl+A 全走这里）。
    /// <paramref name="clicked"/> = 本次点击的行（详情卡跟它走）；为 null 时取最后一条。
    /// </summary>
    public void SetSelectionFromView(IReadOnlyList<VideoItem> rows, VideoItem? clicked)
    {
        SelectedRows.Clear();
        foreach (var r in rows) SelectedRows.Add(r);

        NotifySelectionMeta();

        if (clicked != null) Selected = clicked;
        else if (SelectedRows.Count > 0) Selected = SelectedRows[^1];
    }

    /// <summary>
    /// 列表重算后按 <c>FullPath</c> 认领选中项（列表里是同一批对象引用，但过滤 / 排序会换集合实例），
    /// 再把选中回填给 View。⛔ 不做这一步 ⇒ 刷新后高亮全丢、详情卡与列表对不上。
    /// </summary>
    private void ReconcileSelection()
    {
        var mapped = SelectedRows
            .Select(old => Videos.FirstOrDefault(v => string.Equals(v.FullPath, old.FullPath, StringComparison.OrdinalIgnoreCase)))
            .Where(v => v != null)
            .Select(v => v!)
            .ToList();

        SelectedRows.Clear();
        foreach (var v in mapped) SelectedRows.Add(v);

        if (Selected != null && !Videos.Contains(Selected))
            Selected = SelectedRows.Count > 0 ? SelectedRows[^1] : null;

        NotifySelectionMeta();
        RaiseSelectionSync();
    }

    /// <summary>右键「全选」：只选当前列表里的（与勾选时代的分页口径一致）。</summary>
    [RelayCommand]
    private void SelectAll()
    {
        SelectedRows.Clear();
        foreach (var v in Videos) SelectedRows.Add(v);
        Selected = Videos.Count > 0 ? Videos[0] : null;

        NotifySelectionMeta();
        RaiseSelectionSync();
        StatusText = $"已全选 {Videos.Count} 个视频。";
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

    /// <summary>右键「反选」：当前列表里没选中的变成选中，已选中的去掉。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        var picked = SelectedRows.Select(v => v.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SelectedRows.Clear();
        foreach (var v in Videos)
            if (!picked.Contains(v.FullPath)) SelectedRows.Add(v);

        NotifySelectionMeta();
        RaiseSelectionSync();
        StatusText = $"已反选，选中 {SelectedRows.Count} 个视频。";
    }

    /// <summary>➡ 发送到批量处理：为选中视频批量建**转码**任务（只建任务，不立刻跑）。</summary>
    [RelayCommand]
    private void SendToBatch()
    {
        var paths = SelectedRows.Select(v => v.FullPath).ToList();
        if (paths.Count == 0) { StatusText = "先选中几个视频（鼠标点选，Ctrl 增选 / Shift 连选 / Ctrl+A 全选）。"; return; }

        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        _store.EnqueueForTranscode(paths);
        StatusText = $"已把 {paths.Count} 个文件送去「批量处理」建转码任务……";
        _nav?.NavigateTo("video-batch");
    }
}
