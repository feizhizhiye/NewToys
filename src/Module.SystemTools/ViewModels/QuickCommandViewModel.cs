using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.SystemTools.Services;

namespace NewToys.SystemTools.ViewModels;

/// <summary>
/// 「系统工具 → 快捷命令」页（`system-quick`，NT-091）：本机十项常见设置 / 开关的一键入口。
/// <para>
/// 🔴 **应用无提权机制**（需求 §2.3.7③）：需要管理员的项（休眠 / 网卡启禁 / UAC / 远程桌面 / 代理）
/// ⛔ 不代跑 —— 只「📋 复制命令」+ 状态栏提示，由用户在提权的终端里自己执行。
/// 其余项走 Core 的 <c>ScriptRunner</c> 执行（打开设置面板 / 控制面板 / 磁盘清理）。
/// </para>
/// </summary>
public partial class QuickCommandViewModel : ObservableObject, IStatusProvider
{
    private readonly ISystemToolsSettingsService? _settings;
    private readonly ScriptRunner? _runner;

    [ObservableProperty] private string _statusText = "选一项：能直接做的点「▶」，要管理员的点「📋 复制」自己跑。";
    [ObservableProperty] private bool _isBusy;

    public QuickCommandViewModel() : this(null!, null!) { }

    public QuickCommandViewModel(ISystemToolsSettingsService settings, ScriptRunner runner)
    {
        _settings = settings;
        _runner = runner;
        foreach (var it in QuickCommandCatalog.All) Items.Add(it);
    }

    public ObservableCollection<QuickCommandItem> Items { get; } = new();

    public bool IsNotBusy => !IsBusy;

    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    /// <summary>▶ 执行（不需要管理员的项）。</summary>
    [RelayCommand]
    private async Task RunAsync(QuickCommandItem? item)
    {
        if (item is null || IsBusy) return;

        if (item.NeedsAdmin)
        {
            await CopyAsync(item.AdminCommand).ConfigureAwait(true);
            StatusText = "⚠ 「" + item.Title + "」要管理员权限 —— 命令已复制，请在提权的终端里自己跑。";
            return;
        }

        var cmd = item.Command.Length > 0 ? item.Command : "start " + item.Uri;
        if (item.Uri.Length > 0 && item.Command.Length == 0) cmd = "start " + item.Uri;

        IsBusy = true;
        StatusText = "正在执行：" + item.Title + " …";
        try
        {
            var res = await _runner!.RunCommandAsync(new CommandRunRequest
            {
                Command = cmd,
                Shell = "cmd",
                TimeoutSeconds = _settings?.CommandTimeoutSeconds ?? 30
            }).ConfigureAwait(true);

            StatusText = res.Ok ? "已执行：" + item.Title : "⚠ 执行失败：" + (res.Error ?? "退出码 " + res.ExitCode);
        }
        catch (Exception ex) { StatusText = "⚠ 执行失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>📋 复制命令（需要管理员的项用；也可以复制给自己改）。</summary>
    [RelayCommand]
    private async Task CopyAsync(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) { StatusText = "没有可复制的命令。"; return; }

        var win = (Avalonia.Application.Current?.ApplicationLifetime
                   as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }

        try
        {
            await win.Clipboard.SetTextAsync(command);
            StatusText = "已复制命令：" + command;
        }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }
}
