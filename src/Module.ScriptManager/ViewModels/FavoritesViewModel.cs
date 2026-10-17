using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 「脚本管理器 → 收藏夹」页：展示被收藏的<b>系统设置项 / 脚本</b>（<see cref="QuickAction.IsFavorite"/> 为真的那些，
/// 「脚本库」与「命令库」两页的收藏项都在这里汇总）。
/// <para>
/// 「收藏」不是另一份清单 —— 与两个列表页读的是同一批对象，所以那里右键收藏 / 取消后，
/// 这里重新进入即是最新状态（页面 VM 是瞬态，每次导航都重建，见规范 6.5）。
/// </para>
/// <para>
/// 界面是**一排按钮**（不是卡片）：收藏 = 常用功能，点一下就跑，不放复杂信息。
/// 「✕ 取消固定」放右键菜单（菜单在 View 的 code-behind 里现场构造并直接挂命令，
/// 避免弹出树取不到 DataContext，见规范 3.6 的 ContextMenu 注）。
/// </para>
/// </summary>
public partial class FavoritesViewModel : ObservableObject, IStatusProvider
{
    private readonly IQuickActionService? _svc;
    private readonly INavigationService? _nav;

    public ObservableCollection<QuickAction> Items { get; } = new();

    [ObservableProperty] private string _statusText = "";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isBusy;

    /// <summary>供卡片按钮 / 右键菜单绑定（避免跨弹出树取父级 VM）。</summary>
    public bool IsNotBusy => !IsBusy;

    public string Hint =>
        "★ 收藏来自「脚本库」「命令库」两页：选中一条后右键「★ 添加到收藏夹」，或点右侧「☆ 收藏」。\n" +
        "点按钮直接执行；右键按钮可「✕ 取消固定」移出收藏夹。";

    public string DataFileHint =>
        $"数据文件：{_svc?.ScriptsFilePath}\n　　　　　{_svc?.QuickFilePath}\n（两个页面各一份，都在 config\\ 下，会被「设置 → 数据管理」的备份 / 导入一起覆盖）";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public FavoritesViewModel() { }

    public FavoritesViewModel(IQuickActionService svc, INavigationService nav)
    {
        _svc = svc;
        _nav = nav;
        Refresh();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [RelayCommand]
    private void Refresh()
    {
        Items.Clear();
        if (_svc != null)
            foreach (var i in _svc.Favorites) Items.Add(i);

        IsEmpty = Items.Count == 0;
        StatusText = $"共 {Items.Count} 项收藏";
    }

    /// <summary>运行一条收藏项（命令经 <c>cmd</c> 执行，需要管理员权限的走 UAC 提权）。</summary>
    [RelayCommand]
    private async Task RunAsync(QuickAction? item)
    {
        if (item == null || _svc == null) return;
        if (IsBusy) return;

        IsBusy = true;
        StatusText = $"正在运行：{item.Title} …";
        try
        {
            var (ok, message) = await _svc.RunAsync(item).ConfigureAwait(true);
            StatusText = ok ? message : "⚠ " + message;
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 运行失败：" + ex.Message;
        }
        finally { IsBusy = false; }
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

    /// <summary>跳到「脚本库」页（模块内导航走 INavigationService，不依赖 Shell 程序集）。</summary>
    [RelayCommand]
    private void OpenShortcuts() => _nav?.NavigateTo("script-library");
}
