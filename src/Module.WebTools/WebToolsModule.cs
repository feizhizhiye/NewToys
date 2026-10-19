using Microsoft.Extensions.DependencyInjection;
using NewToys.WebTools.Services;
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
        services.AddSingleton<HostsService>();
        services.AddSingleton<BookmarkService>();
        services.AddSingleton<PingService>();
        // NT-084 批 A/B：规则集 = 磁盘 .py 文件（RuleFileService）+ 元数据 / 迁移（RuleCatalogService）。
        // 旧的 ScrapeService（单条 RuleType+Rule）已删 —— 见 NT-084 命名表。
        services.AddSingleton<RuleFileService>();
        services.AddSingleton<RuleCatalogService>();
        // NT-084 批 C：抓取 + 跑规则（规则解析全在 Python 侧，C# 只搬运）
        services.AddSingleton<RuleRunService>();

        // NT-084：规则目录进备份（🔴 默认勾选 —— 规则是唯一无法重建的资产；只备 rules\，⛔ 不备 cache\）
        services.AddSingleton<IBackupSection, RulesBackupSection>();

        // NT-076 第二批：本模块的总库 schema（web_docs + 4 份旧 JSON 一次性入表）。
        services.AddSingleton<ILibrarySchemaContributor, WebToolsSchemaContributor>();

        services.AddTransient<Views.WebHttpView>();    services.AddTransient<WebHttpViewModel>();
        services.AddTransient<Views.BookmarkView>();   services.AddTransient<BookmarkViewModel>();
        services.AddTransient<Views.PingView>();       services.AddTransient<PingViewModel>();
        services.AddTransient<Views.RuleCatalogView>(); services.AddTransient<RuleCatalogViewModel>();
        services.AddTransient<Views.RuleEditorView>();  services.AddTransient<RuleEditorViewModel>();
        services.AddTransient<Views.HostsView>();      services.AddTransient<HostsViewModel>();

        // v3.64 / NT-061：本模块设置（网页抓取默认 UA + 单次抓取超时）——**本模块配置的唯一写入点**，
        // 落 config\web-tools.json（经 Core 的 IOptionsStore）；原来这两个值硬编码在 ScrapeService 里。
        services.AddSingleton<IWebToolsSettingsService, WebToolsSettingsService>();
        services.AddTransient<Views.SettingsWebToolsView>();  services.AddTransient<SettingsWebToolsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("web", "网络工具", "IconWeb", 30);
        menu.RegisterFunction("web", "web-http",     "HTTP 测试",  "🧪", "WebHttpView", 1, "构造请求并查看响应");
        menu.RegisterFunction("web", "web-bookmark", "书签管理",   "🔖", "BookmarkView", 2, "收藏与整理常用网址、导入浏览器书签");
        menu.RegisterFunction("web", "web-ping",     "网络 Ping",  "📡", "PingView",     3, "测试主机连通性与延迟，支持切 DNS");
        // 🔴 NT-084：Key 仍是 web-scrape（落 columnlayout.json 页键，改了用户偏好失效）；
        // 标题改为「抓取规则」，指向新的目录页 RuleCatalogView（旧的 ScrapeView 已删）。
        menu.RegisterFunction("web", "web-scrape",   "抓取规则",   "🕷", "RuleCatalogView", 4, "管理抓取规则集（一条规则集 = 一个 .py 文件）");
        // NT-084 批 C：新增「规则编辑器」（三段式自绘页）—— 目录页点「✏ 编辑规则」跳这里。
        menu.RegisterFunction("web", "web-rule-editor", "规则编辑器", "🧩", "RuleEditorView", 5, "编辑命令列表、抓 HTML、调规则");
        menu.RegisterFunction("web", "web-hosts",    "Hosts 管理", "🗂", "HostsView",    6, "切换 hosts 方案、网络导入、编辑系统 hosts");

        // v3.64 / NT-061：模块设置页通则 —— View/VM 归本模块。
        // v3.66 / NT-062：菜单项**挂回本模块的一级菜单**（⛔ 不再进「设置」组 —— 该一级菜单项已删除）。
        menu.RegisterFunction("web", "settings-web-tools", "网络工具设置", "🌐", "SettingsWebToolsView", 100,
            "网页抓取的默认 User-Agent 与单次抓取超时");
    }
}
