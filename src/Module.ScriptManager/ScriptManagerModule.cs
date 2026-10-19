using System;
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
        // v3.80：「软件源」设置页已删（源 = 系统 winget 配置，全要 UAC）⇒ 改成命令库里的命令条目，
        // 设置页「软件源」节只剩说明 + 一键加入命令库（见 SettingsScriptViewModel）。
        // 「脚本库 / 命令库」的清单服务（v1.34 起仅本模块使用，故沉到模块内）。
        // 它依赖 Core 的 ConfigStoreService 与 Shared 的 IAppConfigService，由 Shell 在 LoadAll 之前已注册。
        services.AddSingleton<IQuickActionService, QuickActionService>();

        // NT-074：软件包管理整套**下放到本模块**（只有本模块用，⛔ 不让框架层背业务）。
        // 原在 Shared（接口 / 模型）+ Core（winget 后端 / 输出解析 / package.json 设置），
        // 现全部落 NewToys.ScriptManager.{Models,Services}；Shell 侧的 DI 与确认钩子接线已摘掉
        // （二次确认改走 Core.DialogHooks，与模块里其它破坏性操作同一套）。
        services.AddSingleton(sp => new PackageSettingsService(sp.GetRequiredService<ConfigStoreService>()));
        services.AddSingleton<IPackageSettingsService>(sp => sp.GetRequiredService<PackageSettingsService>());
        // NT-081 步骤5：包后端按平台二选一（页面/VM 零改动，只换 IPackageManagerService 实现）。
        // Windows = winget（PackageManagerService）；Linux = dpkg/apt 只读适配器（AptPackageService）。
        if (OperatingSystem.IsLinux())
            services.AddSingleton<IPackageManagerService, AptPackageService>();
        else
            services.AddSingleton<IPackageManagerService, PackageManagerService>();

        // v3.64 / NT-061：本模块设置（脚本目录 + 三个解释器路径）——**本模块配置的唯一写入点**，
        // 落 config\script-manager.json（经 Core 的 IOptionsStore）。旧值原来在 app.json，首次运行自动搬迁。
        services.AddSingleton<IScriptManagerSettingsService, ScriptManagerSettingsService>();

        // NT-067 的底座：脚本执行（静默跑、捕获输出）。
        // 落本模块（⛔ 不进 Core：框架层冻结；脚本执行是"脚本管理器"的能力，不是全应用能力）。
        // ⚠️ NT-068 S8：manifest 服务（ISysInfoManifestService）已删除 —— sysinfo.manifest.json 模型废弃。
        // NT-081 步骤3：进程执行本体走 Shell 注册的 IProcessRunner（Core）；本类只留脚本特有逻辑。
        services.AddSingleton(sp => new ScriptRunner(
            sp.GetRequiredService<IAppConfigService>(),
            sp.GetRequiredService<IProcessRunner>()));

        // NT-068 S2：数据层 —— 手工数据（集合/条目/视图）落 config\script\script.json，
        // 观测缓存落 data\script\script.db。两者都**不进** Core（框架层冻结）。
        services.AddSingleton<IScriptStoreService, ScriptStoreService>();
        // NT-076：观测表**并进总库** `data\newtoys.db`（连接向 Core 的 SqliteDatabase 要，⛔ 不再单独 script.db）
        services.AddSingleton<ScriptDatabase>();
        // NT-076：本模块在总库里的建表（+ 老 script.db 一次性合并），由总库 schema 统一入口调用
        services.AddSingleton<ILibrarySchemaContributor, ScriptManagerSchemaContributor>();
        // NT-078 批次 C：脚本实体文件的备份节（`data.scripts`）—— 脚本目录 = 默认目录 + 各库 RootPath，
        // 这段解析只有本模块有（框架层⛔ 不引模块），所以按 IBackupSection 契约由模块自己注册。
        services.AddSingleton<IBackupSection, ScriptsBackupSection>();
        // NT-068 S5：采集器执行（跑条目 → 解析输出 → 写观测库 → 生成列 schema）
        services.AddSingleton(sp => new CollectorService(
            sp.GetRequiredService<ScriptRunner>(),
            sp.GetRequiredService<ScriptDatabase>(),
            sp.GetRequiredService<IScriptManagerSettingsService>()));
        // NT-068 S4：集合页（主从两栏）
        services.AddTransient<Views.CollectionView>();  services.AddTransient<CollectionViewModel>();
        services.AddTransient<Views.SettingsScriptView>();  services.AddTransient<SettingsScriptViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        // 🔴 NT-070（启动提速）：原「开机即落位三份数据」的数据库/播种重活已搬到
        // InitializeDeferred（首帧之后）。这里只剩菜单注册 + 钩子接线 —— 两者都是纯内存操作，
        // 但必须在首帧前完成，否则左树会先空后跳。
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
        menu.RegisterFunction("script", "script-collections", "脚本集合", "🗂", "CollectionView",     4, "按需组合采集器的容器：一个集合 = 一组条目，可在信息面板里整屏切换");
        menu.RegisterFunction("script", "script-info",      "信息面板", "💻", "ScriptInfoView",     99, "按集合分节展示采集器输出；顶部「集合 ▾」一次显示一个集合");

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

    /// <summary>
    /// NT-070（启动提速）：原来写在 Initialize 里的启动重活 —— 打开两个数据层 + 播种。
    /// 挪到首帧之后执行，启动时不再被 SQLite 打开 / 写库 / 复制示例脚本挡住。
    /// <para>
    /// NT-068 S2：落位三份数据 —— config\script-manager.json（设置，解析设置服务时建）、
    /// config\script\script.json（手工数据）、data\script\script.db（观测缓存）。
    /// 库坏 / 目录只读都不该拦住整个应用（⛔ 不抛），页面按空数据处理。
    /// </para>
    /// </summary>
    public void InitializeDeferred(IServiceProvider services)
    {
        try
        {
            var store = services.GetRequiredService<IScriptStoreService>();
            var db = services.GetRequiredService<ScriptDatabase>();
            // 保留策略兜底：days 模式的条目在启动时清一遍超龄观测（§三.10）。
            db.PruneAllDays(store.Data.Items);

            // NT-068 S5：内置采集脚本 + 内置采集器条目 + 内置「系统信息」集合（只加不改）。
            var settings = services.GetRequiredService<IScriptManagerSettingsService>();
            BuiltinCollectorSeeder.Seed(store, settings);

            // v3.66 第 ④ 条：随包「示例」—— 示例脚本文件夹 + 示例命令，**只播种一次**（删了不复活）。
            ExampleSeeder.Seed(store, settings);

            // NT-083 批 3：随包「环境维护」脚本 + 内置集合「环境维护」—— 同样只播种一次。
            // ⛔ 不覆盖用户改过的脚本、⛔ 不抢回被移出的集合成员。
            EnvScriptsSeeder.Seed(store, settings);
        }
        catch
        {
            // 数据层不可用 ⇒ 页面按空数据处理（状态栏由页面自己报 ⚠），⛔ 不让启动失败。
        }
    }
}
