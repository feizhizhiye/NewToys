using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.SystemTools.Services;
using NewToys.SystemTools.ViewModels;

namespace NewToys.SystemTools;

/// <summary>
/// **系统工具**模块（`system`，Order 25，NT-091 / NT-096）：<b>当前操作系统的常用设置与快速动作</b>
/// —— 命令库 / 收藏夹 / 快捷命令 + 软件包（winget）+ 本模块设置。
/// <para>
/// 🔴 依赖只有 Shared / Core / UI：⛔ 不引 Shell、⛔ 不引其它模块；执行一律走 Core 的
/// <c>ScriptRunner</c>（NT-088 上提），⛔ 不自己起进程跑脚本。
/// </para>
/// </summary>
public class SystemToolsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // 命令条目清单：config\system\commands.json（本模块手工数据的唯一落点，⛔ 不开 DB）。
        services.AddSingleton<ISystemCommandStore, SystemCommandStore>();
        // 设置：config\system.json（经 Core 的 IOptionsStore）。
        services.AddSingleton<ISystemToolsSettingsService, SystemToolsSettingsService>();

        // 软件包（NT-096 自脚本中心迁入）：条目数据仍落 config\package.json（老用户的源偏好不丢）。
        services.AddSingleton(sp => new PackageSettingsService(sp.GetRequiredService<ConfigStoreService>()));
        services.AddSingleton<IPackageSettingsService>(sp => sp.GetRequiredService<PackageSettingsService>());
        // 后端按平台二选一（页面 / VM 零改动，只换 IPackageManagerService 实现）：
        // Windows = winget（PackageManagerService）；Linux = dpkg/apt 只读适配器（AptPackageService）。
        if (System.OperatingSystem.IsLinux())
            services.AddSingleton<IPackageManagerService, AptPackageService>();
        else
            services.AddSingleton<IPackageManagerService, PackageManagerService>();

        // 执行器（Core，NT-088 上提）：与脚本中心注册的是同一套类型，各容器一份实例（模块内自用）。
        services.AddSingleton(sp => new ScriptRunner(
            sp.GetRequiredService<IAppConfigService>(),
            sp.GetRequiredService<IProcessRunner>()));

        services.AddTransient<Views.SystemCommandLibraryView>();   services.AddTransient<SystemCommandListViewModel>();
        services.AddTransient<Views.FavoritesView>();              services.AddTransient<FavoritesViewModel>();
        services.AddTransient<Views.QuickCommandView>();           services.AddTransient<QuickCommandViewModel>();
        services.AddTransient<Views.SettingsSystemView>();         services.AddTransient<SettingsSystemViewModel>();
        services.AddTransient<Views.PackageView>();                services.AddTransient<PackageViewModel>();
    }

    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("system", "系统工具", "IconSystem", 25);

        // 🔴 Key 一律不改：`script-commands` / `script-favorites` 沿用从脚本中心平移过来的键
        // （页键落 columnlayout.json，改了用户列偏好失效）。
        menu.RegisterFunction("system", "script-commands",  "命令库",   "⚙", "SystemCommandLibraryView", 1,
            "只产生副作用的命令行清单：系统设置面板 / 控制面板 / 管理工具，点一下即执行");
        menu.RegisterFunction("system", "system-quick",     "快捷命令", "⚡", "QuickCommandView",         2,
            "十项常见系统开关（电源 / 主题 / 休眠 / 网卡 / UAC / 代理 …）；需要管理员的项只给命令，⛔ 不代跑");
        menu.RegisterFunction("system", "script-favorites", "收藏夹",   "★", "FavoritesView",            3,
            "收藏的命令：按钮墙，点一下就跑");
        // NT-096：软件包页整体从脚本中心迁入（Key / 标题 ⛔ 不改 —— 页键落 columnlayout.json）。
        menu.RegisterFunction("system", "script-packages",  "软件包",   "📦", "PackageView",             4,
            "搜索、安装、升级、卸载软件包（winget；Linux 上走 dpkg 只读适配器）");

        // 模块设置页（NT-061 通则：挂回本模块的一级菜单，Order 100）。
        menu.RegisterFunction("system", "settings-system", "系统工具设置", "🖥", "SettingsSystemView", 100,
            "命令超时、本模块的数据文件位置");
    }

    // 🔴 本模块没有建库 / 播种 / 扫描之类的重活 ⇒ 不覆盖 InitializeDeferred（接口有默认空实现）。
}
