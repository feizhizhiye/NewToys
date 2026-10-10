using Microsoft.Extensions.DependencyInjection;
using NewToys.PcTools.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
namespace NewToys.PcTools;
public class PcToolsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<Views.FavoritesView>();      services.AddTransient<FavoritesViewModel>();
        services.AddTransient<Views.MyShortcutsView>();    services.AddTransient<MyShortcutsViewModel>();
        services.AddTransient<Views.PackageManagerView>();  services.AddTransient<PackageManagerViewModel>();
        services.AddTransient<Views.PcInfoView>();         services.AddTransient<PcInfoViewModel>();
        services.AddTransient<Views.QuickSettingsView>();  services.AddTransient<QuickSettingsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("pc", "电脑工具", "🖥", 20);
        // 第 7 个参数是「功能简介」：功能清单页展示，首页收藏卡也复用它（单一真相源）。
        // ⚠️ v1.24 起**删除了「工具箱首页」(pc-home)** —— 模块默认落在「设置与脚本」（order 0 即首项），
        //    所以这里不再有 🏠 入口；功能入口本身由左侧二级菜单承担。
        menu.RegisterFunction("pc", "pc-shortcuts",     "设置与脚本", "▤",  "MyShortcutsView",    0, "系统设置项与常用脚本清单，可增删改查、收藏");
        menu.RegisterFunction("pc", "pc-favorites",     "收藏夹",     "★",  "FavoritesView",      1, "已收藏的系统设置与脚本");
        menu.RegisterFunction("pc", "pc-package",       "软件包管理", "📦", "PackageManagerView", 2, "搜索、安装、升级、卸载软件包");
        menu.RegisterFunction("pc", "pc-info",          "系统信息",   "💻", "PcInfoView",         3, "硬件、系统、网络概览");
        menu.RegisterFunction("pc", "pc-quicksettings", "快捷设置",   "⚙",  "QuickSettingsView",  99, "常用系统开关一键切换");
    }
}
