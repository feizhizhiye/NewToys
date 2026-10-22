using System;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.ScriptManager.Services;
using NewToys.ScriptManager.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.UI.Services;   // NT-089：跨页传物（书签 / Ping → 抓取）已上提 NewToys.UI
namespace NewToys.ScriptManager;
public class ScriptManagerModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<Views.ScriptLibraryView>();    services.AddTransient<ScriptLibraryViewModel>();
        services.AddTransient<Views.ScriptInfoView>();         services.AddTransient<ScriptInfoViewModel>();
        // 「脚本库 / 命令库」的清单服务（v1.34 起仅本模块使用，故沉到模块内）。
        // 它依赖 Core 的 ConfigStoreService 与 Shared 的 IAppConfigService，由 Shell 在 LoadAll 之前已注册。
        services.AddSingleton<IQuickActionService, QuickActionService>();

        // v3.64 / NT-061：本模块设置（脚本目录 + 三个解释器路径）——**本模块配置的唯一写入点**，
        // 落 config\script-manager.json（经 Core 的 IOptionsStore）。旧值原来在 app.json，首次运行自动搬迁。
        services.AddSingleton<IScriptManagerSettingsService, ScriptManagerSettingsService>();

        // NT-067 的底座：脚本执行（静默跑、捕获输出）。
        // NT-088：已上提 NewToys.Core.Services —— system 系统工具模块（收藏夹 / 快捷命令）要执行条目，
        // 而**模块禁互引**，执行契约只能放框架层（行为零变化，纯搬家）。
        // ⛔ 不留转发 shim / 别名 —— 留了等于两套入口，必然漂移。
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
        // ==================== NT-089：抓取能力整体平移（自 Module.WebTools 迁入，功能与交互零变化） ====================
        // 规则集 = 磁盘 .py 文件（RuleFileService）+ 目录元数据（RuleCatalogService）。
        services.AddSingleton<RuleFileService>();
        services.AddSingleton<RuleCatalogService>();
        // 流水线（flow）配置读写 —— 五段槽位落 flows\<名>.json（⛔ 不开 DB 表）；目录留给 NT-090 迁。
        services.AddSingleton<FlowService>();
        // 抓取 + 跑规则（规则解析全在 Python 侧，C# 只搬运）。
        // 🔴 抓取默认项（UA / 超时）仍由「网络工具设置」页写进 config\web-tools.json（菜单 Key ⛔ 不改）⇒
        //    模块禁互引，这里经 Core 的 IOptionsStore 只读同一份文件（NT-094 设置拆分后归位）。
        services.AddSingleton(sp => new RuleRunService(
            sp.GetRequiredService<IPythonRuntime>(),
            sp.GetRequiredService<RuleFileService>(),
            sp.GetRequiredService<ConfigStoreService>(),
            sp.GetRequiredService<IOptionsStore>()));
        // 运行记录 runs\<脚本>\<ts>_<hash>\（result.json + page.html + diag.txt，保留最近 10 条）。
        services.AddSingleton<RunRecordService>();
        // 内置下载器（并发 / 限速 / 去重 / 续跑 / 取消；⛔ 下载行为不脚本化）。
        services.AddSingleton<DownloadService>();
        // 跨页一次性传参：书签 / Ping（网络工具）↔ 抓取（本模块）跨模块共享 ⇒
        // `WebTransferService` 上提 NewToys.UI，两侧各注册一次单例（同一容器 ⇒ 同一实例）。
        services.AddSingleton<WebTransferService>();
        // 规则目录进备份（🔴 默认勾选 —— 规则是唯一无法重建的资产；只备 rules\，⛔ 不备 cache\）。
        services.AddSingleton<IBackupSection, RulesBackupSection>();

        // 三页（Key 与标题 ⛔ 不改）
        services.AddTransient<Views.RuleCatalogView>(); services.AddTransient<RuleCatalogViewModel>();
        services.AddTransient<Views.RuleEditorView>();  services.AddTransient<RuleEditorViewModel>();
        services.AddTransient<Views.CrawlPanelView>();  services.AddTransient<CrawlPanelViewModel>();

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
        // 🔴 NT-091：**收藏夹 / 命令库已迁出本组**（→ 新「系统工具」模块，页键 `script-favorites` /
        // `script-commands` 与标题一律 ⛔ 不改 —— 页键落 columnlayout.json，改了用户列偏好失效）。
        // NT-068 的原顺序（收藏夹 / 脚本库 / 命令库 / 软件包 / 信息面板 / 设置）⇒
        // NT-091 迁出收藏夹 / 命令库，NT-096 迁出软件包 ⇒ 本组只剩 脚本库 / 集合 / 信息面板 + 抓取三页。
        menu.RegisterFunction("script", "script-library",   "脚本库",   "▤",  "ScriptLibraryView",  1, "较复杂的系统设置项与常用脚本，可运行 / 增删改查 / 收藏");
        menu.RegisterFunction("script", "script-collections", "脚本集合", "🗂", "CollectionView",     4, "按需组合采集器的容器：一个集合 = 一组条目，可在信息面板里整屏切换");
        menu.RegisterFunction("script", "script-info",      "信息面板", "💻", "ScriptInfoView",     99, "按集合分节展示采集器输出；顶部「集合 ▾」一次显示一个集合");

        // 🔴 NT-089：抓取三页从「网络工具」迁入本组（Key `web-scrape` / `web-rule-editor` / `web-crawl`
        // 与标题一律 ⛔ 不改 —— 页键落 columnlayout.json，改了用户列偏好失效）。
        menu.RegisterFunction("script", "web-scrape",   "抓取规则",   "🕷", "RuleCatalogView", 5, "管理抓取规则集（一条规则集 = 一个 .py 文件）");
        menu.RegisterFunction("script", "web-rule-editor", "调试集合", "🧩", "RuleEditorView", 6, "把段脚本装配成流水线：五段槽位 + 试跑推荐 + 表达式检验");
        menu.RegisterFunction("script", "web-crawl", "运行结果", "🕸", "CrawlPanelView", 7,
            "跑抓取脚本看目录条目：动态列 + 双击下钻 + 下载，④ 节可回看最近 10 次运行（目录 / 日志 / 诊断）");

        // v3.64 / NT-061：**模块设置页通则** —— 每个功能模块一个设置页、View/VM 归本模块。
        // v3.66 / NT-062：菜单项**挂回本模块的一级菜单**（⛔ 不再进「设置」组 —— 该一级菜单项已删除）。
        menu.RegisterFunction("script", "settings-script", "脚本管理器设置", "🛠", "SettingsScriptView", 100,
            "脚本库扫描的根目录与设置文件位置");

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

            // NT-089：随包预设规则集播种（自 WebToolsModule 迁来）——
            // `<程序目录>\defaults\presets\*.py` → `<DataDir>\script\rules\`（只加不覆盖、只播种一次）。
            PresetSeeder.Seed(services.GetRequiredService<RuleFileService>());
        }
        catch
        {
            // 数据层不可用 ⇒ 页面按空数据处理（状态栏由页面自己报 ⚠），⛔ 不让启动失败。
        }
    }
}
