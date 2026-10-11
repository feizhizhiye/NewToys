using Microsoft.Extensions.DependencyInjection;
using NewToys.Settings.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
namespace NewToys.Settings;
public class SettingsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<Views.GeneralView>();        services.AddTransient<GeneralViewModel>();
        services.AddTransient<Views.ThemeView>();          services.AddTransient<ThemeViewModel>();
        services.AddTransient<Views.PathView>();           services.AddTransient<PathViewModel>();
        services.AddTransient<Views.LaunchConfigView>();   services.AddTransient<LaunchConfigViewModel>();
        services.AddTransient<Views.DnsView>();            services.AddTransient<DnsViewModel>();
        services.AddTransient<Views.PackageSourceView>();    services.AddTransient<PackageSourceViewModel>();
        services.AddTransient<Views.ProfileView>();        services.AddTransient<ProfileViewModel>();
        services.AddTransient<Views.DataView>();           services.AddTransient<DataViewModel>();
        services.AddTransient<Views.AboutView>();          services.AddTransient<AboutViewModel>();
        services.AddTransient<Views.AllFunctionsView>();   services.AddTransient<AllFunctionsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("settings", "设置", "IconSettings", 999, isBottom: true);
        menu.RegisterFunction("settings", "settings-general",      "通用设置", "⚙", "GeneralView",       0, "语言、行为与启动项");
        menu.RegisterFunction("settings", "settings-theme",        "主题外观", "🎨", "ThemeView",         1, "深浅色模式与强调色");
        menu.RegisterFunction("settings", "settings-path",         "路径配置", "📁", "PathView",          2, "数据与工具路径设置");
        // 「启动配置」→「一键按钮」：首页三个一键按钮的**全部**配置都在这页（名称 / 程序 / 参数 / 网址）。
        // Key 保持 settings-launch 不变（首页与收藏卡都按它跳转）。
        menu.RegisterFunction("settings", "settings-launch",       "一键按钮", "🚀", "LaunchConfigView",  3, "首页一键按钮的程序与网址");
        menu.RegisterFunction("settings", "settings-dns",          "加密 DNS", "🛡", "DnsView",           4, "加密 DNS 与网络配置");
        // 软件包管理模块的源设置（数据落 config\package.json，可被数据管理备份）
        menu.RegisterFunction("settings", "settings-package",        "软件源",   "📦", "PackageSourceView",  5, "软件包来源与默认参数");
        // v3.5 新增：「配置」（多配置；一个配置 = 一套独立的 config+data+logs，切换 = 重启）
        menu.RegisterFunction("settings", "settings-profile",      "配置",     "🧩", "ProfileView",       6, "多套独立数据（config/data/logs），可新增 / 切换 / 重命名 / 删除");
        menu.RegisterFunction("settings", "settings-data",         "数据管理", "💾", "DataView",          7, "备份 / 导入 config 配置");
        menu.RegisterFunction("settings", "settings-about",        "关于",     "ℹ", "AboutView",         8, "版本与项目信息");
        menu.RegisterFunction("settings", "settings-all",          "功能清单", "📋", "AllFunctionsView",  9, "所有模块与功能总览");
    }
}
