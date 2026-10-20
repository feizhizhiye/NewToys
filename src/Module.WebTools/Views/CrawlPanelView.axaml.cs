using System;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.WebTools.Models;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

/// <summary>
/// 爬虫面板（<c>web-crawl</c>，NT-086 批 D）。
/// </summary>
public partial class CrawlPanelView : UserControl
{
    public CrawlPanelView() => InitializeComponent();

    /// <summary>双击目录行 = 下钻（用这一行的 url 再抓一次；命令在 VM 上）。</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { DataContext: CrawlEntry entry }) return;
        if (DataContext is CrawlPanelViewModel vm)
            vm.DrillDownCommand.Execute(entry);
    }
}
