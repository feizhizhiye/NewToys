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
        services.AddSingleton<ScrapeService>();

        services.AddTransient<Views.WebHttpView>();    services.AddTransient<WebHttpViewModel>();
        services.AddTransient<Views.BookmarkView>();   services.AddTransient<BookmarkViewModel>();
        services.AddTransient<Views.PingView>();       services.AddTransient<PingViewModel>();
        services.AddTransient<Views.ScrapeView>();     services.AddTransient<ScrapeViewModel>();
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
        menu.RegisterFunction("web", "web-scrape",   "网页抓取",   "🕷", "ScrapeView",   4, "多配置批量抓取并整合");
        menu.RegisterFunction("web", "web-hosts",    "Hosts 管理", "🗂", "HostsView",    5, "切换 hosts 方案、网络导入、编辑系统 hosts");

        // v3.64 / NT-061：模块设置页通则 —— View/VM 归本模块。
        // v3.66 / NT-062：菜单项**挂回本模块的一级菜单**（⛔ 不再进「设置」组 —— 该一级菜单项已删除）。
        menu.RegisterFunction("web", "settings-web-tools", "网络工具设置", "🌐", "SettingsWebToolsView", 100,
            "网页抓取的默认 User-Agent 与单次抓取超时");
    }
}
