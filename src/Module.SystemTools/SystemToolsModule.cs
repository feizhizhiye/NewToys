using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.SystemTools.Services;
using NewToys.SystemTools.ViewModels;

namespace NewToys.SystemTools;

/// <summary>
/// **系统工具**模块（菜单组键 `system-tools`，Order 25，NT-091 / NT-096 / NT-097）：<b>当前操作系统的常用设置与快速命令</b>
/// —— 收藏夹 / 命令库（对标脚本库布局 + 随包预设）+ 软件包（winget）+ 本模块设置。
/// <para>🔴 组键**不是** `system` —— 那是 Shell 的隐藏宿主组（应用级设置锚点 + 功能清单），同名会把
/// 那 8 个页面渲染进本组（见 <see cref="Initialize"/> 注释）。</para>
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
        services.AddTransient<Views.SettingsSystemView>();         services.AddTransient<SettingsSystemViewModel>();
        services.AddTransient<Views.PackageView>();                services.AddTransient<PackageViewModel>();
    }

    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        // 🔴🔴 组键 ⛔ 不能用 "system"：那是**框架保留的隐藏宿主组**（`AppSettingsRegistration.HiddenGroupKey` /
        // `ModuleLoader.HostGroupKey`，`IsHome=true` ⇒ 不渲染），应用级设置那 8 个节锚点（启动与退出 /
        // 主题外观 / 路径与配置 / 一键按钮 / 加密 DNS / 数据管理 / 功能清单 / 版本与更新）全挂在它名下。
        // NT-091 误用了同名键 ⇒ 这些页面被渲染到「系统工具」组里（`RegisterModule` 同名键会复用并覆盖
        // Title/Order/IsHome + 补登暂存项）。改用本模块自己的键后，宿主组恢复隐藏。
        menu.RegisterModule(GroupKey, "系统工具", "IconSystem", 25);

        // 🔴 函数 Key 一律不改：`script-commands` / `script-favorites` 沿用从脚本中心平移过来的键
        // （页键落 columnlayout.json，改了用户列偏好失效）。**组键与页键是两回事**，组键可改。
        // NT-097：`system-quick` 快捷命令页已删（内容并入命令库预设）；收藏夹升为模块下第一个页面。
        menu.RegisterFunction(GroupKey, "script-favorites", "收藏夹",   "★", "FavoritesView",            1,
            "收藏的命令：按钮墙，点一下就跑");
        menu.RegisterFunction(GroupKey, "script-commands",  "命令库",   "⚙", "SystemCommandLibraryView", 2,
            "只产生副作用的命令行清单（Windows 设置 / 控制面板 / 管理工具 / 快捷设置 / winget），首次运行带预设，点一下即执行");
        // NT-096：软件包页整体从脚本中心迁入（Key / 标题 ⛔ 不改 —— 页键落 columnlayout.json）。
        menu.RegisterFunction(GroupKey, "script-packages",  "软件包",   "📦", "PackageView",             3,
            "搜索、安装、升级、卸载软件包（winget；Linux 上走 dpkg 只读适配器）");

        // 模块设置页（NT-061 通则：挂回本模块的一级菜单，Order 100）。
        menu.RegisterFunction(GroupKey, "settings-system", "系统工具设置", "🖥", "SettingsSystemView", 100,
            "命令超时、本模块的数据文件位置");
    }

    /// <summary>
    /// 首帧之后的轻活（NT-070 两段式）：命令库随包预设播种（NT-097）——
    /// <c>config\system\commands.seeded</c> 标记存在 ⇒ 跳过（删了不复活）；按 Id 只加不改。
    /// </summary>
    public void InitializeDeferred(IServiceProvider services)
    {
        try
        {
            SystemCommandSeeder.Seed(services.GetRequiredService<ISystemCommandStore>());
        }
        catch
        {
            // 播种失败 ⇒ 页面按空数据处理，⛔ 不让启动失败（下次启动会重试）。
        }
    }

    /// <summary>
    /// 本模块的**菜单组键**（⛔ 不是页 Key）。🔴 不能叫 <c>system</c>：那是 Shell 的隐藏宿主组
    /// （应用级设置锚点 + 功能清单挂那），同名会把这 8 个页面渲染进「系统工具」。
    /// </summary>
    private const string GroupKey = "system-tools";

    // 🔴 启动重活只有「命令库预设播种」一件 ⇒ 放 InitializeDeferred（NT-070 两段式，⛔ 不拦首帧）。
}
