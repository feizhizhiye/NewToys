using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// 应用级设置页 · **关于**节（v3.65 / NT-062，需求 §4.9.2 / §6.5）：
/// 版本 / 关于 / **功能清单入口** / 🔄 重载模块。
/// <para>
/// 🔴 本节**没有节锚点菜单 Key**（需求表格里是"—（页内可点）"）⇒ 只能从设置页的节导航进入，
/// ⛔ 不进 <c>settings</c> 分组、⛔ 不可被 <c>OpenTab</c> 外部寻址。
/// </para>
/// <para>
/// 「🔄 重载模块」v3.62 由功能清单页**移入本节**（语义 = 规范 §3.7 热加载：只新增、移除要重启）；
/// 走 <see cref="IModuleReloadService"/>，左树重建与底栏计数仍由 Shell 订阅 <c>Reloaded</c> 完成。
/// </para>
/// </summary>
public partial class AboutSettingsViewModel : ObservableObject, IStatusProvider
{
    private readonly INavigationService _nav;
    private readonly IModuleReloadService _reload;

    public AboutSettingsViewModel(INavigationService nav, IModuleReloadService reload)
    {
        _nav = nav;
        _reload = reload;
        StatusText = $"NewToys {Version} · 个人高强度使用工具集";
    }

    /// <summary>版本号（与左树底部同一来源：编译时自动生成的 <c>UpdateService.CurrentVersion</c>，⛔ 禁手写）。</summary>
    public string Version => UpdateService.CurrentVersion;

    /// <summary>打开**功能清单**页（Key <c>settings-all</c> ⛔ 不改；v3.62 起它是独立页，不在设置内）。</summary>
    [RelayCommand]
    private void OpenFunctionList() => _nav.NavigateTo("settings-all");

    /// <summary>🔄 重载模块：重扫 <c>Modules\</c> 把新放的 DLL 装进来（只新增；移除模块要重启）。</summary>
    [RelayCommand]
    private void ReloadModules()
    {
        var (added, skipped, failed) = _reload.HotReload();
        StatusText = failed > 0
            ? $"⚠ 模块重载：新增 {added} / 跳过 {skipped} / 失败 {failed}（失败不影响使用，详情见 Debug 输出）"
            : $"模块重载：新增 {added} / 跳过 {skipped} / 失败 0。";
    }

    // ==================== 底部状态栏（IStatusProvider） ====================

    [ObservableProperty] private string _statusText = "";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;
}
