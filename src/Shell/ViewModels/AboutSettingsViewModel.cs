using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// 应用级设置页 · **关于**节：版本 / 组件版本 / 🔄 重载模块。
/// <para>
/// 🔴 本节**没有节锚点菜单 Key**（需求表格里是"—（页内可点）"）⇒ 只能从设置页的节导航进入，
/// ⛔ 不进 <c>settings</c> 分组、⛔ 不可被 <c>OpenTab</c> 外部寻址。
/// </para>
/// <para>
/// 「重载模块」走 <see cref="IModuleReloadService"/>，左树重建与底栏计数仍由 Shell 订阅 <c>Reloaded</c> 完成；
/// 重载后组件版本列表自动重取（热加载进来的模块要立刻出现在列表里）。
/// </para>
/// </summary>
public partial class AboutSettingsViewModel : ObservableObject, IStatusProvider
{
    private readonly IModuleReloadService _reload;

    public AboutSettingsViewModel(IModuleReloadService reload)
    {
        _reload = reload;
        StatusText = $"NewToys {Version} · 个人高强度使用工具集";
        RefreshVersions();
    }

    /// <summary>版本号（与左树底部同一来源：编译时自动生成的 <c>UpdateService.CurrentVersion</c>，⛔ 禁手写）。</summary>
    public string Version => UpdateService.CurrentVersion;

    // ==================== 技术栈 ====================

    /// <summary>当前展示的库清单（各项目直接引用，Avalonia 家族折叠）。</summary>
    public ObservableCollection<TechLibraryItem> Libraries { get; } = new();

    /// <summary>重取技术栈清单（打开本节 / 重载模块后调用 —— 热加载的模块可能带来新依赖）。</summary>
    private void RefreshLibraries()
    {
        var items = TechStackProbe.Collect();
        Libraries.Clear();
        foreach (var item in items) Libraries.Add(item);
    }

    // ==================== 组件版本 ====================

    /// <summary>外壳程序集（入口 + NewToys.Shared / Core / UI）。</summary>
    public ObservableCollection<LoadedComponentItem> ShellComponents { get; } = new();

    /// <summary>已加载的功能模块（<c>Module.*</c>）。</summary>
    public ObservableCollection<LoadedComponentItem> ModuleComponents { get; } = new();

    [ObservableProperty] private string _moduleSummary = "";

    /// <summary>空态提示显隐（没有模块时提示 Modules\ 目录为空）。</summary>
    [ObservableProperty] private bool _hasNoModules;

    /// <summary>重取组件版本（打开本节 / 点刷新 / 重载模块后调用）。</summary>
    [RelayCommand]
    private void RefreshVersions()
    {
        var (shell, modules) = LoadedComponentProbe.Collect();

        ShellComponents.Clear();
        foreach (var item in shell) ShellComponents.Add(item);

        ModuleComponents.Clear();
        foreach (var item in modules) ModuleComponents.Add(item);

        HasNoModules = modules.Count == 0;
        ModuleSummary = HasNoModules ? "" : $"共 {modules.Count} 个模块";

        RefreshLibraries();
    }

    // ==================== 增加功能 ====================

    /// <summary>🔄 重载模块：重扫 <c>Modules\</c> 把新放的 DLL 装进来（只新增；移除模块要重启）。</summary>
    [RelayCommand]
    private void ReloadModules()
    {
        var (added, skipped, failed) = _reload.HotReload();
        StatusText = failed > 0
            ? $"⚠ 模块重载：新增 {added} / 跳过 {skipped} / 失败 {failed}（失败不影响使用，详情见 Debug 输出）"
            : $"模块重载：新增 {added} / 跳过 {skipped} / 失败 0。";

        RefreshVersions();
    }

    /// <summary>📁 打开 <c>&lt;程序目录&gt;\Modules\</c>（不存在就建，方便直接往里丢 DLL）。</summary>
    [RelayCommand]
    private void OpenModulesFolder()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, ModuleLoader.ModulesSubDirectory);
            Directory.CreateDirectory(dir);
            OpenInFileManager(dir);
            StatusText = "已打开模块目录：" + dir;
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 打开模块目录失败：" + ex.Message;
        }
    }

    /// <summary>在系统文件管理器里打开目录（平台中立：Windows 资源管理器 / macOS Finder / Linux 默认管理器）。</summary>
    private static void OpenInFileManager(string dir)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", "\"" + dir + "\"")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", "\"" + dir + "\"")
                : new ProcessStartInfo("xdg-open", "\"" + dir + "\"");
        psi.UseShellExecute = true;
        Process.Start(psi);
    }

    // ==================== 底部状态栏（IStatusProvider） ====================

    [ObservableProperty] private string _statusText = "";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;
}
