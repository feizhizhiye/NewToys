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
/// 「系统工具 → 收藏夹」页（`script-favorites`，自脚本中心平移）。
/// <para>
/// 🔴 <b>数据源换了</b>（拍板 2）：这里展示的是<b>本模块命令条目</b>里 `IsFavorite` 的那些，
/// 落 `config\system\commands.json`。⛔ 不是 Core 的 `IFavoritesService`（那份是首页卡片的「菜单功能 key」收藏）。
/// ⇒ 已知回退：脚本库条目的收藏不再出现在本页（用户 2026-10-22 接受）。
/// </para>
/// <para>界面是**一排按钮**，操作逻辑与首页「常用功能」一致：左键点击执行，右键菜单「▶ 运行 / ✕ 移出收藏夹」。</para>
/// </summary>
public partial class FavoritesViewModel : ObservableObject, IStatusProvider
{
    private readonly ISystemCommandStore? _store;
    private readonly ISystemToolsSettingsService? _settings;
    private readonly ScriptRunner? _runner;
    private readonly ISystemCommandLauncher? _launcher;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEmpty = true;

    public FavoritesViewModel() { }

    public FavoritesViewModel(ISystemCommandStore store, ISystemToolsSettingsService settings, ScriptRunner runner, ISystemCommandLauncher? launcher = null)
    {
        _store = store;
        _settings = settings;
        _runner = runner;
        _launcher = launcher;
        Refresh();
    }

    public ObservableCollection<SystemCommand> Items { get; } = new();

    public bool IsNotBusy => !IsBusy;

    public string Hint =>
        "★ 收藏来自「命令库」页：选中一条后点「★ 收藏」。\n" +
        "点按钮直接执行；右键「✕ 移出收藏夹」移出。🔴 需要管理员的条目点按钮会弹 UAC，提权后在 Windows 终端里执行（取消则不执行）。";

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

    /// <summary>执行一条收藏：普通项走 Core 的 ScriptRunner；需要管理员的项弹 UAC 提权执行（可见窗口）。</summary>
    [RelayCommand]
    private async Task RunAsync(SystemCommand? item)
    {
        if (item is null || IsBusy) return;

        if (item.NeedAdmin)
        {
            // 补充（用户 2026-10-23：勾了需要管理员的也能直接运行）。
            if (_launcher is null) { StatusText = "提权运行不可用（DI 未注册）。"; return; }
            var (_, msg) = _launcher.RunElevated(item);
            StatusText = msg;
            return;
        }

        // 「打开一个窗口」类（start … / *.msc / *.cpl / ms-settings: / explorer …）：不等结束，否则整个列表变灰。
        if (SystemCommandLauncher.TryStartWindowCommand(item.Command, out var openMsg))
        {
            StatusText = openMsg.Length > 0 ? openMsg : $"已打开「{item.Title}」（不等待窗口关闭）。";
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
}
