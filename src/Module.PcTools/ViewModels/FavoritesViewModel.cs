using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.ViewModels;

/// <summary>
/// 「电脑工具 → 收藏夹」页：展示被收藏的<b>系统设置项 / 脚本</b>（<see cref="QuickAction.IsFavorite"/> 为真的那些）。
/// <para>
/// 「收藏」不是另一份清单 —— 与「设置与脚本」页读的是同一批对象，所以那里右键收藏 / 取消后，
/// 这里重新进入即是最新状态（页面 VM 是瞬态，每次导航都重建，见规范 6.5）。
/// </para>
/// <para>本轮（v1.24）没有「运行」：卡片只展示命令、可复制，执行在后续版本提供。</para>
/// </summary>
public partial class FavoritesViewModel : ObservableObject
{
    private readonly IQuickActionService? _svc;
    private readonly INavigationService? _nav;

    public ObservableCollection<QuickAction> Items { get; } = new();

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isEmpty = true;

    public string Hint =>
        "★ 收藏来自「设置与脚本」页：选中一条后右键「★ 添加到收藏夹」，或点右侧「☆ 收藏」。";

    public string FilePath => _svc?.FilePath ?? "";

    public string DataFileHint =>
        $"数据文件：{FilePath}（放在 config\\ 下，会被「设置 → 数据管理」的备份 / 导入一起覆盖）";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public FavoritesViewModel() { }

    public FavoritesViewModel(IQuickActionService svc, INavigationService nav)
    {
        _svc = svc;
        _nav = nav;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Items.Clear();
        if (_svc != null)
            foreach (var i in _svc.Favorites) Items.Add(i);

        IsEmpty = Items.Count == 0;
        StatusText = $"共 {Items.Count} 项收藏";
    }

    /// <summary>从收藏夹移除（这里必然是从「已收藏」切成「未收藏」）。</summary>
    [RelayCommand]
    private void Unfavorite(QuickAction? item)
    {
        if (item == null || _svc == null) return;
        _svc.ToggleFavorite(item);
        Refresh();
        StatusText = $"已从收藏夹移除：{item.Title}";
    }

    /// <summary>复制这条的命令内容。</summary>
    [RelayCommand]
    private async Task Copy(QuickAction? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Command))
        {
            StatusText = "这条还没有命令内容。";
            return;
        }
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(item.Command);
            StatusText = "已复制命令到剪贴板。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>跳到「设置与脚本」页（模块内导航走 INavigationService，不依赖 Shell 程序集）。</summary>
    [RelayCommand]
    private void OpenShortcuts() => _nav?.NavigateTo("pc-shortcuts");
}
