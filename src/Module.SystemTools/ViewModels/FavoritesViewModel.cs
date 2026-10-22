using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.Services;

namespace NewToys.SystemTools.ViewModels;

/// <summary>
/// 「系统工具 → 收藏夹」页（`script-favorites`，NT-091 自脚本中心平移）。
/// <para>
/// 🔴 <b>数据源换了</b>（NT-091 拍板 2）：这里展示的是<b>本模块命令条目</b>里 `IsFavorite` 的那些，
/// 落 `config\system\commands.json`。⛔ 不是 Core 的 `IFavoritesService`（那份是首页卡片的「菜单功能 key」收藏）。
/// ⇒ 已知回退：脚本库条目的收藏不再出现在本页（用户 2026-10-22 接受）。
/// </para>
/// <para>界面仍是**一排按钮**：收藏 = 常用动作，点一下就跑；「✕」移出收藏。</para>
/// </summary>
public partial class FavoritesViewModel : ObservableObject, IStatusProvider
{
    private readonly ISystemCommandStore? _store;
    private readonly ISystemToolsSettingsService? _settings;
    private readonly ScriptRunner? _runner;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEmpty = true;

    public FavoritesViewModel() { }

    public FavoritesViewModel(ISystemCommandStore store, ISystemToolsSettingsService settings, ScriptRunner runner)
    {
        _store = store;
        _settings = settings;
        _runner = runner;
        Refresh();
    }

    public ObservableCollection<SystemCommand> Items { get; } = new();

    public bool IsNotBusy => !IsBusy;

    public string Hint =>
        "★ 收藏来自「命令库」页：选中一条后点「★ 收藏」。\n" +
        "点按钮直接执行；点 ✕ 移出收藏夹。🔴 需要管理员的条目只复制命令（应用不代跑）。";

    public string DataFileHint => "数据文件：" + (_store?.FilePath ?? "");

    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [RelayCommand]
    private void Refresh()
    {
        Items.Clear();
        if (_store is not null)
            foreach (var i in _store.Items.Where(x => x.IsFavorite)) Items.Add(i);

        IsEmpty = Items.Count == 0;
        StatusText = $"共 {Items.Count} 项收藏";
    }

    /// <summary>执行一条收藏（走 Core 的 ScriptRunner；需要管理员的只复制命令）。</summary>
    [RelayCommand]
    private async Task RunAsync(SystemCommand? item)
    {
        if (item is null || IsBusy) return;

        if (item.NeedAdmin)
        {
            // NT-097：补上真正的剪贴板复制（原来只改状态栏文案，用户复制不到）。
            await SetClipboardAsync(item.Command).ConfigureAwait(true);
            StatusText = "⚠ 这条要管理员权限 —— 命令已复制，请在提权的终端里自己跑：" + item.Command;
            return;
        }

        IsBusy = true;
        StatusText = "正在运行：" + item.Title + " …";
        try
        {
            var res = await _runner!.RunCommandAsync(new CommandRunRequest
            {
                Command = item.Command,
                Shell = string.IsNullOrWhiteSpace(item.Shell) ? "auto" : item.Shell,
                TimeoutSeconds = _settings?.CommandTimeoutSeconds ?? 30
            }).ConfigureAwait(true);

            StatusText = res.Ok ? "已执行：" + item.Title : "⚠ 执行失败：" + (res.Error ?? "退出码 " + res.ExitCode);
        }
        catch (Exception ex) { StatusText = "⚠ 执行失败：" + ex.Message; }
        finally { IsBusy = false; }
        }

    /// <summary>移出收藏夹（这里必然是从「已收藏」切成「未收藏」）。</summary>
    [RelayCommand]
    private void Unfavorite(SystemCommand? item)
    {
        if (item is null || _store is null) return;
        item.IsFavorite = false;
        _store.Save();
        Refresh();
        StatusText = "已移出收藏夹：" + item.Title;
    }

    private async Task SetClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) { StatusText = "没有可复制的命令。"; return; }
        var win = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }
        try { await win.Clipboard.SetTextAsync(text); }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }
}
