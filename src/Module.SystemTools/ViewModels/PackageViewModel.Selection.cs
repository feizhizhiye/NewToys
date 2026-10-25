using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.SystemTools.ViewModels;

public partial class PackageViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>按需拉一次 <c>winget show</c>，把长描述 / 发布者 / 主页 / 协议补进详情列。</summary>
    [RelayCommand]
    private async Task LoadDetailAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null) return;

        StatusText = "正在读取详情（winget show）……";
        // v3.78：商店包只在 msstore 源里 ⇒ show 必须带 --source，否则详情读不出来。
        var (ok, msg, detail) = await _pkg.ShowAsync(p.Id,
            p.Source);
        if (!ok || detail == null) { StatusText = msg; return; }

        if (!string.IsNullOrWhiteSpace(detail.Name)) p.Name = detail.Name;
        // ⛔ 不覆盖 Version：winget show 的 Version 是「最新可用版」——覆盖已安装项会把「⬆ 升级到 X」错显成「已安装」
        //    （v3.19：修的 bug：点一下详情，状态就从"升级"变"已安装"）。只在原本为空时才补。
        if (string.IsNullOrWhiteSpace(p.Version) && !string.IsNullOrWhiteSpace(detail.Version)) p.Version = detail.Version;
        p.Publisher = detail.Publisher;
        p.Description = detail.Description;
        p.Homepage = detail.Homepage;
        p.License = detail.License;
        p.InstallLocation = detail.InstallLocation;

        // v3.19：PackageInfo 已是 ObservableObject → 上面的赋值直接通知详情卡 / 列表行刷新，
        // ⛔ 不用再整体重建列表（以前是普通类才需要 ApplyView，且重建会重置滚动位）。
        DetailExtra = detail.Description;
        StatusText = msg;
    }

    // ==================== v3.37：勾选列 + 批量安装 / 升级队列 ====================

    /// <summary>行被勾了几条（工具条「⬇ 安装选中」「⬆ 升级选中」按它操作）。</summary>
    public int CheckedCount => _all.Count(p => p.IsChecked);

    public bool HasChecked => CheckedCount > 0;

    public string CheckedText => CheckedCount == 0 ? "未勾选" : $"已勾选 {CheckedCount} 个";

    /// <summary>批量按钮的可用态：有勾选且不在忙（Avalonia 绑定不支逻辑运算符，⛔ 别在 XAML 里写 And）。</summary>
    public bool CanRunBatch => HasChecked && !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunBatch));
        OnPropertyChanged(nameof(CanUpgradeSelected));
    }

    /// <summary>
    /// 表头 ☑ 的三态（🔴 用**原生三态 CheckBox**，⛔ 别用字符"☑"画 —— 字体会渲染成深色块）：
    /// <c>true</c> = 全选、<c>false</c> = 全不选、<c>null</c> = 部分选中。
    /// </summary>
    [ObservableProperty] private bool? _allCheckedState = false;

    /// <summary>表头 ☑ 点一下：当前不是"全选"就全选，否则全不选。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var target = AllCheckedState != true;
        foreach (var p in _all) p.IsChecked = target;
        RefreshChecked();
        StatusText = target ? $"已全选 {_all.Count} 条。" : "已取消全选。";
    }

    /// <summary>表头右键：取消选择。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        foreach (var p in _all) p.IsChecked = false;
        RefreshChecked();
        StatusText = "已取消选择。";
    }

    /// <summary>表头右键：反选。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var p in _all) p.IsChecked = !p.IsChecked;
        RefreshChecked();
        StatusText = $"已反选，当前勾选 {CheckedCount} 条。";
    }

    /// <summary>重算勾选计数与表头三态（行的 IsChecked 一变就会被调）。</summary>
    public void RefreshChecked()
    {
        var total = _all.Count;
        var n = _all.Count(p => p.IsChecked);
        AllCheckedState = total == 0 ? false : n == 0 ? false : n == total ? true : null;

        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(CheckedText));
        OnPropertyChanged(nameof(CanRunBatch));
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageInfo.IsChecked)) RefreshChecked();
    }

}
