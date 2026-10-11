using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 通用设置页（v1.35：**四项系统级行为全部真正生效**）。
/// <list type="bullet">
///   <item><b>开机时启动</b>：写 / 删 <c>HKCU\…\Run</c> 里的启动项（<see cref="StartupService"/>），勾选即改注册表；</item>
///   <item><b>启动时最小化到托盘</b>：由 Shell 在启动时读本开关决定是否显示主窗口（改了下次启动生效）；</item>
///   <item><b>关闭按钮最小化到托盘</b>：主窗口 <c>OnClosing</c> 每次关闭时实时读本开关 → <b>立即生效</b>；</item>
///   <item><b>启动时检查更新</b>：启动时 GET 一个纯文本版本地址（<c>app.json</c> 的 <c>UpdateUrl</c>），只提示不自动更新。</item>
/// </list>
/// <para>
/// ⚠️ 诚实边界（规范 0.3）：没配更新地址就是「未配置」，页面照实写，不假装检查过；
/// 托盘 / 最小化这类**要下一次启动才看得到**的，也在界面上标注清楚。
/// </para>
/// </summary>
public partial class GeneralViewModel : ObservableObject, IStatusProvider
{
    private readonly ILaunchConfigService? _launch;
    private readonly IAppConfigService? _app;
    private readonly StartupService? _startup;
    private readonly UpdateService? _update;

    /// <summary>抑制标志：加载配置时回写属性不该反过来触发保存。</summary>
    private bool _loading;

    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _checkUpdateOnStartup = true;
    [ObservableProperty] private bool _closeToTray;

    /// <summary>更新检查用的版本文件地址（自由文本 → 需点「💾 保存」，规范 3.5）。</summary>
    [ObservableProperty] private string _updateUrl = "";

    [ObservableProperty] private string _statusText = "";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>开机启动项的**真实状态**（读注册表，不是读配置）—— 让"配置勾了但没写进去"这种偏差一眼可见。</summary>
    [ObservableProperty] private string _startupStateText = "";

    [ObservableProperty] private bool _isCheckingUpdate;

    public bool IsNotBusy => !IsCheckingUpdate;

    /// <summary>当前程序版本（显示在页面上，检查更新时与线上比对）。</summary>
    public string CurrentVersion => UpdateService.CurrentVersion;

    /// <summary>托盘相关开关的说明（哪些要重启才生效，一次讲清）。</summary>
    public string TrayHint =>
        "「启动时最小化到托盘」与「开机启动」要**下一次启动**才看得到效果；" +
        "「关闭按钮最小化到托盘」**立即生效**（主窗口每次关闭时实时读这个开关）。" +
        "想彻底退出：托盘图标右键 → 「退出 NewToys」。";

    public GeneralViewModel() { }

    public GeneralViewModel(ILaunchConfigService launch, IAppConfigService app,
                            StartupService startup, UpdateService update)
    {
        _launch = launch;
        _app = app;
        _startup = startup;
        _update = update;

        var c = launch.Config;
        _loading = true;
        StartWithSystem = c.StartWithSystem;
        StartMinimized = c.StartMinimized;
        CheckUpdateOnStartup = c.CheckUpdateOnStartup;
        CloseToTray = c.CloseToTray;
        UpdateUrl = (app.UpdateUrl ?? "").Trim();
        _loading = false;

        RefreshStartupState();
        StatusText = $"当前版本 {CurrentVersion} · 选项已同步（launch.json / app.json）。";
    }

    partial void OnStartWithSystemChanged(bool value) { Persist(); ApplyStartup(); }
    partial void OnStartMinimizedChanged(bool value) => Persist();
    partial void OnCheckUpdateOnStartupChanged(bool value) => Persist();
    partial void OnCloseToTrayChanged(bool value) => Persist();

    /// <summary>把四个开关整体写回 <c>launch.json</c>（改哪个字段都走这里，避免半保存状态）。</summary>
    private void Persist()
    {
        if (_loading || _launch == null) return;

        var c = _launch.Config;
        c.StartWithSystem = StartWithSystem;
        c.StartMinimized = StartMinimized;
        c.CheckUpdateOnStartup = CheckUpdateOnStartup;
        c.CloseToTray = CloseToTray;

        var (ok, msg) = _launch.Save(c);
        StatusText = ok
            ? $"已保存 · {DateTime.Now:HH:mm:ss}（写入 launch.json）"
            : $"保存失败：{msg}";
    }

    /// <summary>「开机时启动」：把注册表改成与勾选一致（真实生效，不只是存起来）。</summary>
    private void ApplyStartup()
    {
        if (_loading || _startup == null) return;

        if (!_startup.IsSupported)
        {
            StartupStateText = "当前平台不支持开机启动（仅 Windows）。";
            StatusText = "已保存到 launch.json，但开机启动只在 Windows 上能真正生效。";
            return;
        }

        var (ok, msg) = _startup.SetEnabled(StartWithSystem);
        RefreshStartupState();
        StatusText = ok ? "已保存 · " + msg : "⚠ " + msg;
    }

    /// <summary>重读一次注册表，把开机启动项的真实状态显示出来。</summary>
    private void RefreshStartupState()
    {
        if (_startup == null) { StartupStateText = "开机启动服务未就绪。"; return; }
        if (!_startup.IsSupported) { StartupStateText = "当前平台不支持开机启动（仅 Windows）。"; return; }

        var real = _startup.IsEnabled;
        StartupStateText = real == StartWithSystem
            ? $"注册表状态：{(real ? "已启用" : "未启用")}（与上方勾选一致）"
            : $"⚠ 注册表状态：{(real ? "已启用" : "未启用")}，与上方勾选不一致 —— 点一下勾选框可重新写一次";
    }

    /// <summary>💾 保存更新地址（自由文本 → 显式保存，不边打边写）。</summary>
    [RelayCommand]
    private void SaveUpdateUrl()
    {
        if (_app == null) return;
        _app.UpdateUrl = (UpdateUrl ?? "").Trim();
        StatusText = (UpdateUrl ?? "").Trim().Length == 0
            ? "已清空更新地址 —— 启动时不再检查更新。"
            : $"已保存更新地址（app.json），下次启动会检查。";
    }

    /// <summary>🔎 立即检查一次更新（只提示，不自动下载 / 替换自己）。</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (_update == null) { StatusText = "更新服务未就绪。"; return; }
        if (IsCheckingUpdate) return;

        IsCheckingUpdate = true;
        StatusText = "正在检查更新……";
        try
        {
            var (hasUpdate, remote, message) = await _update.CheckAsync((UpdateUrl ?? "").Trim());
            StatusText = hasUpdate ? "🎉 " + message : message;
        }
        catch (Exception ex) { StatusText = "检查更新失败：" + ex.Message; }
        finally { IsCheckingUpdate = false; }
    }

    /// <summary>打开任务管理器的「启动」页，让用户能自己去核对 / 禁用。</summary>
    [RelayCommand]
    private void OpenStartupSettings()
    {
        if (_startup == null) return;
        var (_, msg) = _startup.OpenStartupSettings();
        StatusText = msg;
    }

    /// <summary>🔄 重新读一次注册表状态（怀疑外部改过启动时用）。</summary>
    [RelayCommand]
    private void RefreshStartup() => RefreshStartupState();
}
