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
        // 商店包只在 msstore 源里 ⇒ show 必须带 --source，否则详情读不出来。
        var (ok, msg, detail) = await _pkg.ShowAsync(p.Id,
            p.Source);
        if (!ok || detail == null) { StatusText = msg; return; }

        if (!string.IsNullOrWhiteSpace(detail.Name)) p.Name = detail.Name;
        // ⛔ 不覆盖 Version：winget show 的 Version 是「最新可用版」——覆盖已安装项会把「升级到 X」错显成「已安装」
        //    （v3.19：修的 bug：点一下详情，状态就从"升级"变"已安装"）。只在原本为空时才补。
        if (string.IsNullOrWhiteSpace(p.Version) && !string.IsNullOrWhiteSpace(detail.Version)) p.Version = detail.Version;
        p.Publisher = detail.Publisher;
        p.Description = detail.Description;
        p.Homepage = detail.Homepage;
        p.License = detail.License;
        p.InstallLocation = detail.InstallLocation;

        // PackageInfo 已是 ObservableObject → 上面的赋值直接通知详情卡 / 列表行刷新，
        // ⛔ 不用整体重建列表：重建会重置滚动位。
        DetailExtra = detail.Description;
        StatusText = msg;
    }

    // ==================== 批量作用域 = 列表选中行 ====================

    /// <summary>
    /// 批量命令（安装 / 升级 / 卸载）的作用域 = 列表当前选中的行。
    /// <para>与 <c>TableView.SelectedItems</c> 双向绑定：控件侧 Ctrl / Shift 多选直接改它，
    /// 反向「全选 / 取消 / 反选」也只改它，控件选择跟着走。</para>
    /// </summary>
    public ObservableCollection<PackageInfo> BatchSelection { get; } = new();

    /// <summary>批量操作数（工具条「安装选中 / 升级选中 / 卸载选中」的可用态与计数都看它）。</summary>
    public int BatchCount => BatchSelection.Count;

    /// <summary>批量按钮的可用态：有选中行且不在忙（Avalonia 绑定不支逻辑运算符，⛔ 别在 XAML 里写 And）。</summary>
    public bool CanRunBatch => BatchCount > 0 && !IsBusy;

    /// <summary>列表重建 / 翻页后，把已经不在当前页的选中项剔掉，避免对看不见的行执行批量。</summary>
    private void RefreshBatchSelection()
    {
        for (var i = BatchSelection.Count - 1; i >= 0; i--)
            if (!Packages.Contains(BatchSelection[i])) BatchSelection.RemoveAt(i);
        NotifyBatchChanged();
    }

    private void NotifyBatchChanged()
    {
        OnPropertyChanged(nameof(BatchCount));
        OnPropertyChanged(nameof(CanRunBatch));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunBatch));
        OnPropertyChanged(nameof(CanUpgradeSelected));
    }

    /// <summary>右键「全选」：选中当前页全部行（不在当前页的保持原样）。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var all = BatchCount > 0 && BatchCount == Packages.Count;
        BatchSelection.Clear();
        if (!all)
            foreach (var p in Packages) BatchSelection.Add(p);
        NotifyBatchChanged();
        StatusText = all ? "已取消选择。" : $"已选中当前页 {Packages.Count} 条。";
    }

    /// <summary>右键「取消选择」。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        BatchSelection.Clear();
        NotifyBatchChanged();
        StatusText = "已取消选择。";
    }

    /// <summary>右键「⇄ 反选」：当前页内反向选中。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        var inverted = Packages.Where(p => !BatchSelection.Contains(p)).ToList();
        BatchSelection.Clear();
        foreach (var p in inverted) BatchSelection.Add(p);
        NotifyBatchChanged();
        StatusText = $"已反选，当前选中 {BatchCount} 条。";
    }

}
