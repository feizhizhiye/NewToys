using System;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.ViewModels;

namespace NewToys.ScriptManager.Views;

/// <summary>
/// 爬虫面板（<c>web-crawl</c>，）。
/// </summary>
public partial class CrawlPanelView : UserControl
{
    public CrawlPanelView()
    {
        InitializeComponent();
    }

    /// <summary>单击目录行 = 勾选 / 取消勾选（「⬇ 下载」的作用域）；双击同一次点击会各切一次，净效果为不变。</summary>
    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: CrawlEntry entry }) return;
        if (!e.GetCurrentPoint((Control)sender).Properties.IsLeftButtonPressed) return;
        entry.IsSelected = !entry.IsSelected;
    }

    /// <summary>双击目录行 = 下钻（用这一行的 url 再抓一次；命令在 VM 上）。</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { DataContext: CrawlEntry entry }) return;
        if (DataContext is CrawlPanelViewModel vm)
            vm.DrillDownCommand.Execute(entry);
    }

    /// <summary>双击运行记录行 = 打开那次运行的目录。</summary>
    private void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { DataContext: RunHistoryRow row }) return;
        if (DataContext is CrawlPanelViewModel vm)
            vm.OpenRunDirCommand.Execute(row);
    }
}
