using Microsoft.Extensions.DependencyInjection;
using NewToys.WebTools.Services;
using NewToys.UI.Services;   // 跨页传物（书签 / Ping → 抓取）已上提 NewToys.UI
using NewToys.WebTools.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
namespace NewToys.WebTools;
public class WebToolsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // 模块内基础服务（零依赖，供 Ping 切 DNS、跨页传物、hosts 管理复用）
        services.AddSingleton<DnsResolver>();
        services.AddSingleton<WebTransferService>();
        services.AddSingleton<IHostsPlatform, WindowsHostsPlatform>();   // 系统 hosts 路径 / 刷新 DNS（平台底座）
        services.AddSingleton<HostsService>();
        services.AddSingleton<HostsSourceService>();                     // hosts 源订阅（预设 + 自定义）
        services.AddSingleton<HostsCheckService>();                      // 可用性 / 静态安全检测
        services.AddSingleton<BookmarkService>();
        services.AddSingleton<PingService>();
        // 抓取能力（规则文件 / 目录 / 抓取运行 / 运行记录 / 下载器 / 流水线 + 三页
        // `web-scrape` `web-rule-editor` `web-crawl` + 规则备份节）**整体迁出至脚本中心**
        // （`Module.ScriptManager`）；本模块收窄为「网络配置与诊断」（§2.3 §6.6），
        // 且不再依赖 Python 运行时。

        // 本模块自有库 store\web\web.db（web_docs + 4 份旧 JSON 一次性入表）
        services.AddSingleton<Services.WebDb>();
        services.AddSingleton<ILibrarySchemaContributor, WebToolsSchemaContributor>();

        services.AddTransient<Views.WebHttpView>();    services.AddTransient<WebHttpViewModel>();
        services.AddTransient<Views.BookmarkView>();   services.AddTransient<BookmarkViewModel>();
        services.AddTransient<Views.PingView>();       services.AddTransient<PingViewModel>();
        services.AddTransient<Views.HostsView>();      services.AddTransient<HostsViewModel>();

        // 本模块设置（网页抓取默认 UA + 单次抓取超时）——**本模块配置的唯一写入点**，
        // 落 config\web-tools.json（经 Core 的 IOptionsStore）⇒ 可手改、可备份；⛔ 不要硬编码进 ScrapeService。
        services.AddSingleton<IWebToolsSettingsService, WebToolsSettingsService>();
        services.AddTransient<Views.SettingsWebToolsView>();  services.AddTransient<SettingsWebToolsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("web", "网络工具", "IconWeb", 30);
        menu.RegisterFunction<Views.WebHttpView>("web", "web-http", "HTTP 测试", "🧪", 1, "构造请求并查看响应");
        menu.RegisterFunction<Views.BookmarkView>("web", "web-bookmark", "网址收藏", "🔖", 2, "收藏与整理常用网址、导入浏览器书签");
        menu.RegisterFunction<Views.PingView>("web", "web-ping", "网络 Ping", "📡", 3, "测试主机连通性与延迟，支持切 DNS");
        // `web-scrape` / `web-rule-editor` / `web-crawl` 三页迁入脚本中心组（Key 与标题 ⛔ 不改）。
        // 抓取三页迁出后，Hosts 保留原 Order 7（⛔ 不重排，免动既有排序偏好）。
        // 原 `web-hosts-system`「系统 Hosts」页已撤（2026-10-26 用户指令）：双列表工作台、备份 / 还原 /
        // 文本视图全部并入本页（`web-hosts`），⛔ 别再单独开「系统 hosts」页。
        menu.RegisterFunction<Views.HostsView>("web", "web-hosts", "Hosts 管理", "🗂", 7,
            "左看系统 hosts（或备份）、右看拉取的方案，条目级增删改 + 备份 / 还原 + 写入");

        // 模块设置页通则 —— View/VM 归本模块。
        // 菜单项**挂回本模块的一级菜单**（⛔ 不再进「设置」组 —— 该一级菜单项已删除）。
        menu.RegisterFunction<Views.SettingsWebToolsView>("web", "settings-web-tools", "网络工具设置", "🌐", 100,
            "网页抓取的默认 User-Agent 与单次抓取超时");
    }

    // 随包预设规则集的播种已随 `PresetSeeder` 迁至 `ScriptManagerModule.InitializeDeferred`；
    // `IModule.InitializeDeferred` 有默认空实现 ⇒ 本模块不再覆盖。
}
