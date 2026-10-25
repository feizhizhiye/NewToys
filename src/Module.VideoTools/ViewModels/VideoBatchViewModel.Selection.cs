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

public partial class VideoBatchViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>点表头勾选框：已全勾 → 取消；否则 → 全选。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        if (HasChecked && CheckedCount >= ItemsRaw.Count()) UncheckAll();
        else CheckAll();
    }

    /// <summary>行首勾选框点了一下：只刷新计数与摘要，⛔ 不重排列表（重排会把正在勾的那行挪走）。</summary>
    [RelayCommand]
    private void ItemChecked()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(AllCheckedState));   // v3.16.1：表头三态勾选框跟着走
        UpdateSummaryText();
    }

    /// <summary>☑ 全选（当前列表里显示出来的全部勾上）。</summary>
    [RelayCommand]
    private void CheckAll()
    {
        foreach (var t in Items) t.IsChecked = true;
        ItemChecked();
        StatusText = $"已勾选 {CheckedCount} 个任务。";
    }

    /// <summary>🔄 反选（同样作用于当前视图）。</summary>
    [RelayCommand]
    private void InvertChecked()
    {
        foreach (var t in Items) t.IsChecked = !t.IsChecked;
        ItemChecked();
        StatusText = $"反选后已勾选 {CheckedCount} 个任务。";
    }

    /// <summary>☐ 取消选择（连被筛掉的一起清，不留隐藏的勾选）。</summary>
    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var t in ItemsRaw) t.IsChecked = false;
        ItemChecked();
        StatusText = "已取消全部勾选。";
    }

    // ==================== 详情卡两分区的勾选框（v3.16：与「视频预览」页一致，**可交互**） ====================

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
