using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

// v3.66：Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>
    /// 表头 ☑ 点一下：当前不是"全选"就全选（**只勾当前页**），否则全不选（**跨页全清**）。
    /// <para>2026-10-16 用户定「跟随分页」：<see cref="Items"/> = 当前页可见行；
    /// 底栏「每页」选「全部」（<see cref="ListPageViewModelBase.AllPageSize"/>）时 当前页 = 全部 ⇒ 间接实现全量选择。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var target = AllCheckedState != true;
        if (target)
        {
            foreach (var r in Items) r.IsChecked = true;      // 只勾当前页
        }
        else
        {
            // ⛔ 取消必须清**全部行**（只清当前页会在别的页留看不见的勾，下一步「加入库」误提交）
            foreach (var r in _all) r.IsChecked = false;
        }
        RefreshChecked();
        StatusText = target
            ? $"已全选当前页 {Items.Count} 条，累计勾选 {CheckedCount} 条。"
            : "已取消全选（其它页的勾选也一并清掉）。";
    }

    /// <summary>表头右键：取消选择（跨页全清）。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        foreach (var r in _all) r.IsChecked = false;
        RefreshChecked();
        StatusText = "已取消选择。";
    }

    /// <summary>表头右键：反选（**只反当前页**）。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var r in Items) r.IsChecked = !r.IsChecked;
        RefreshChecked();
        StatusText = $"已反选当前页，累计勾选 {CheckedCount} 条。";
    }

    /// <summary>
    /// 重算勾选计数与表头三态（行的 IsChecked 一变就会被调）。
    /// <para>🔴 三态按**当前页**（<see cref="Items"/>）算：用户盯着屏幕上的格点表头 ☑，
    /// 「部分选中」必须对应看得见的行；计数（<see cref="CheckedCount"/>）仍按跨页累计。</para>
    /// </summary>
    public void RefreshChecked()
    {
        var total = Items.Count;
        var n = Items.Count(r => r.IsChecked);
        AllCheckedState = total == 0 ? false : n == 0 ? false : n == total ? true : null;

        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetRow.IsChecked)) RefreshChecked();
    }

    /// <summary>把一批行装进 <c>_all</c> 并挂上勾选监听（⛔ 别直接 `_all.Add`，否则计数不更新）。</summary>
    private void AddRows(IEnumerable<AssetRow> rows)
    {
        foreach (var r in rows)
        {
            r.PropertyChanged += OnRowPropertyChanged;
            _all.Add(r);
        }
    }

}
