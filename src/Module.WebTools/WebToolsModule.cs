using Microsoft.Extensions.DependencyInjection;
using NewToys.WebTools.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
namespace NewToys.WebTools;
public class WebToolsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<Views.WebHomeView>();    services.AddTransient<WebHomeViewModel>();
        services.AddTransient<Views.WebHttpView>();    services.AddTransient<WebHttpViewModel>();
        services.AddTransient<Views.BookmarkView>();   services.AddTransient<BookmarkViewModel>();
        services.AddTransient<Views.PingView>();       services.AddTransient<PingViewModel>();
        services.AddTransient<Views.ScrapeView>();     services.AddTransient<ScrapeViewModel>();
        services.AddTransient<Views.DnsView>();        services.AddTransient<DnsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("web", "网络工具", "🌐", 30);
        menu.RegisterFunction("web", "web-home",     "工具箱首页", "🏠", "WebHomeView", 0, "网络工具常用功能入口");
        menu.RegisterFunction("web", "web-http",     "HTTP 测试",  "🧪", "WebHttpView", 1, "构造请求并查看响应");
        menu.RegisterFunction("web", "web-bookmark", "书签管理",   "🔖", "BookmarkView", 2, "收藏与整理常用网址");
        menu.RegisterFunction("web", "web-ping",     "网络 Ping",  "📡", "PingView",     3, "测试主机连通性与延迟");
        menu.RegisterFunction("web", "web-scrape",   "网页抓取",   "🕷", "ScrapeView",   4, "批量抓取网页链接");
        menu.RegisterFunction("web", "web-dns",      "DNS 检测",   "🛡", "DnsView",      5, "查询域名解析与记录");
    }
}
