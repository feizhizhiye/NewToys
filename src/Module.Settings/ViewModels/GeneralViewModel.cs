using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 通用设置页。
/// <para>
/// **四个勾选框全部"勾上即写盘"，本页没有保存按钮** —— 这些都是开关类选项，能立刻确认自己改了什么。
/// </para>
/// ⚠️ 诚实声明：本轮只做**持久化**（写 <c>launch.json</c>）。真正的系统级行为
/// （把自身写进开机启动项、托盘图标驻留、关闭按钮拦截）尚未实现，
/// 页面上的提示文字必须如实说明，**不许写着"已生效"**（规范 0.3：文档/界面不得虚报状态）。
/// </summary>
public partial class GeneralViewModel : ObservableObject
{
    private readonly ILaunchConfigService? _launch;

    /// <summary>抑制标志：加载配置时回写属性不该反过来触发保存。</summary>
    private bool _loading;

    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _checkUpdateOnStartup = true;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private string _statusText = "";

    public GeneralViewModel() { }

    public GeneralViewModel(ILaunchConfigService launch)
    {
        _launch = launch;
        var c = launch.Config;

        _loading = true;
        StartWithSystem = c.StartWithSystem;
        StartMinimized = c.StartMinimized;
        CheckUpdateOnStartup = c.CheckUpdateOnStartup;
        CloseToTray = c.CloseToTray;
        _loading = false;

        StatusText = "选项已同步（launch.json）。";
    }

    partial void OnStartWithSystemChanged(bool value) => Persist();
    partial void OnStartMinimizedChanged(bool value) => Persist();
    partial void OnCheckUpdateOnStartupChanged(bool value) => Persist();
    partial void OnCloseToTrayChanged(bool value) => Persist();

    /// <summary>把当前四个开关整体写回 <c>launch.json</c>（改哪个字段都走这里，避免半保存状态）。</summary>
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
}
