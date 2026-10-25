using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>勾选状态变了 → 刷新「已勾选几个」与统计行。</summary>
    public void OnItemCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(AllCheckedState));   // v3.16.1：表头三态勾选框跟着走
        SummaryText = _all.Count == 0
            ? "尚未扫描"
            : $"共 {Videos.Count} / {_all.Count} 个视频 · 合计 {FormatSize(_all.Sum(v => v.SizeBytes))}"
              + (CheckedCount > 0 ? $" · 已勾选 {CheckedCount}" : "");
    }

    /// <summary>行首勾选框被点了一下（只刷新"已勾选几个"，不重排列表 —— 重排会把正在勾的那行挪走）。</summary>
    [RelayCommand]
    private void ItemChecked() => OnItemCheckedChanged();

    /// <summary>
    /// 表头勾选框的三态（v3.16.1）：null = 部分勾选（显示为实心方块）。
    /// ⚠️ 行勾选变化 / 列表刷新后要 `OnPropertyChanged(AllCheckedState)`，否则表头不跟着走。
    /// </summary>
    public bool? AllCheckedState
    {
        get
        {
            if (_all.Count == 0) return false;
            var c = CheckedCount;
            return c == 0 ? false : (c >= _all.Count ? true : null);
        }
    }

    /// <summary>点表头勾选框：已全勾 → 取消；否则 → 全选。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        if (CheckedCount >= _all.Count && _all.Count > 0) UncheckAll();
        else CheckAll();
    }

    /// <summary>☑ 全选（当前列表里显示的全部勾上）。</summary>
    [RelayCommand]
    private void CheckAll()
    {
        foreach (var v in Videos) v.IsChecked = true;
        OnItemCheckedChanged();
        StatusText = $"已勾选 {CheckedCount} 个文件。";
    }

    /// <summary>☑ 反选。</summary>
    [RelayCommand]
    private void InvertChecked()
    {
        foreach (var v in Videos) v.IsChecked = !v.IsChecked;
        OnItemCheckedChanged();
        StatusText = $"反选后已勾选 {CheckedCount} 个文件。";
    }

    /// <summary>☐ 取消全部勾选。</summary>
    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var v in _all) v.IsChecked = false;
        OnItemCheckedChanged();
        StatusText = "已取消全部勾选。";
    }

    /// <summary>➡ 批量处理·转码：把勾选的文件交给「批量处理」页批量生成转码任务（v3.8：原"发送到转码页"）。</summary>
    [RelayCommand]
    private void SendToTranscode()
    {
        var paths = _all.Where(v => v.IsChecked).Select(v => v.FullPath).ToList();
        if (paths.Count == 0) { StatusText = "先勾几个文件（行首的勾选框）。"; return; }

        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        _store.EnqueueForTranscode(paths);
        StatusText = $"已把 {paths.Count} 个文件送去「批量处理」建转码任务……";
        _nav?.NavigateTo("video-batch");
    }

    /// <summary>✂ 视频预览·剪辑：把勾选的文件送进「视频预览」页的剪辑任务编辑器。</summary>
    [RelayCommand]
    private void SendToCut()
    {
        var paths = _all.Where(v => v.IsChecked).Select(v => v.FullPath).ToList();
        if (paths.Count == 0) { StatusText = "先勾几个文件（行首的勾选框）。"; return; }

        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        _store.EnqueueForCut(paths);
        StatusText = $"已把 {paths.Count} 个文件送去「视频预览」页……";
        _nav?.NavigateTo("video-cut");
    }

    // ==================== 媒体信息（ffprobe + 多线程） ====================

}
