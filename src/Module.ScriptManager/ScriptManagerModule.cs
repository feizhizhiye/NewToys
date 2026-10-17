using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.ScriptManager.Services;
using NewToys.ScriptManager.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
namespace NewToys.ScriptManager;
public class ScriptManagerModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<Views.FavoritesView>();      services.AddTransient<FavoritesViewModel>();
        services.AddTransient<Views.ScriptLibraryView>();    services.AddTransient<ScriptLibraryViewModel>();
        services.AddTransient<Views.PackageView>();  services.AddTransient<PackageViewModel>();
        services.AddTransient<Views.ScriptInfoView>();         services.AddTransient<ScriptInfoViewModel>();
        services.AddTransient<Views.CommandLibraryView>();  services.AddTransient<CommandLibraryViewModel>();
        // 「软件源」设置页（NT-050 由 Module.Settings 迁入）：模块专属设置归模块（需求 §4.9），
        // 自注册进**本模块分组 `pc`**，Key / Order / 图标 / 描述⛔不改（收藏与跳转按 Key 寻址）。
        services.AddTransient<Views.PackageSourceView>();  services.AddTransient<PackageSourceViewModel>();
        // 「脚本库 / 命令库」的清单服务（v1.34 起仅本模块使用，故沉到模块内）。
        // 它依赖 Core 的 ConfigStoreService 与 Shared 的 IAppConfigService，由 Shell 在 LoadAll 之前已注册。
        services.AddSingleton<IQuickActionService, QuickActionService>();

        // v3.64 / NT-061：本模块设置（脚本目录 + 三个解释器路径）——**本模块配置的唯一写入点**，
        // 落 config\script-manager.json（经 Core 的 IOptionsStore）。旧值原来在 app.json，首次运行自动搬迁。
        services.AddSingleton<IScriptManagerSettingsService, ScriptManagerSettingsService>();

        // NT-067：信息面板页的两块底座 —— manifest 加载/校验 + 脚本执行（静默跑、捕获输出）。
        // 都落本模块（⛔ 不进 Core：框架层冻结；脚本执行是"脚本管理器"的能力，不是全应用能力）。
        services.AddSingleton<ISysInfoManifestService, SysInfoManifestService>();
        services.AddSingleton(sp => new ScriptRunner(sp.GetRequiredService<IAppConfigService>()));
        services.AddTransient<Views.SettingsScriptView>();  services.AddTransient<SettingsScriptViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("script", "脚本管理器", "IconPc", 20);
        // 第 7 个参数是「功能简介」：功能清单页展示，首页收藏卡也复用它（单一真相源）。
        // ⚠️ v1.24 起**删除了「工具箱首页」** —— 模块默认落在「脚本库」（order 1），
        //    功能入口本身由左侧二级菜单承担。
        // 🔴 NT-068：菜单项顺序（order）= 收藏夹 / 脚本库 / 命令库 / 软件包 / 信息面板 / 设置。
        menu.RegisterFunction("script", "script-favorites", "收藏夹",   "★",  "FavoritesView",      0, "已收藏的脚本与命令，点一下即运行");
        menu.RegisterFunction("script", "script-library",   "脚本库",   "▤",  "ScriptLibraryView",  1, "较复杂的系统设置项与常用脚本，可运行 / 增删改查 / 收藏");
        menu.RegisterFunction("script", "script-commands",  "命令库",   "⚙",  "CommandLibraryView", 2, "系统自带设置入口（Windows 设置面板 / 管理工具），点一下即打开");
        menu.RegisterFunction("script", "script-packages",  "软件包",   "📦", "PackageView",        3, "搜索、安装、升级、卸载软件包");
        menu.RegisterFunction("script", "script-info",      "信息面板", "💻", "ScriptInfoView",     99, "用 sysinfo\\ 下的脚本呈现任意信息表（列与脚本由 manifest 定义）");

        // 软件包模块的源设置：v3.65 / NT-062 起**并入「脚本管理器设置」页**（需求 §6.5）——
        // 原独立菜单项 `settings-package` 已移除（⛔ 不再单独占一个菜单项），数据仍落 config\package.json。
        // ⚠️ View/VM 的 DI 注册保留（SettingsScriptView 以子控件形式内嵌它）。

        // v3.64 / NT-061：**模块设置页通则** —— 每个功能模块一个设置页、View/VM 归本模块。
        // v3.66 / NT-062：菜单项**挂回本模块的一级菜单**（⛔ 不再进「设置」组 —— 该一级菜单项已删除）。
        menu.RegisterFunction("script", "settings-script", "脚本管理器设置", "🛠", "SettingsScriptView", 100,
            "脚本目录 + 软件源（winget 源与查询偏好）");

        // 删除等破坏性操作的二次确认：走公共对话框钩子（由 Shell 注入到 DialogHooks），
        // 不再在 Shell 里硬接（那样会让 Release 下 Shell 被迫引用本模块）。
        QuickActionService.ConfirmHandler = DialogHooks.ConfirmAsync;
    }
}
