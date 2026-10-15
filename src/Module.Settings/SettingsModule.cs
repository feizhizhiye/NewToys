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
        services.AddTransient<Views.PathView>();           services.AddTransient<PathViewModel>();
        services.AddTransient<Views.LaunchConfigView>();   services.AddTransient<LaunchConfigViewModel>();
        services.AddTransient<Views.DnsView>();            services.AddTransient<DnsViewModel>();
        services.AddTransient<Views.DataView>();           services.AddTransient<DataViewModel>();
        services.AddTransient<Views.AllFunctionsView>();   services.AddTransient<AllFunctionsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("settings", "设置", "IconSettings", 999, isBottom: true);
        menu.RegisterFunction("settings", "settings-general",      "通用设置", "⚙", "GeneralView",       0, "语言、行为与启动项");
        // v3.53 / NT-056：「路径配置」+「配置」合并为一张**设置总表**（分类列区分行类型）。
        // ⚠️ Key 仍是 settings-path（§5.1 Key 一经发布⛔不改；首页跳转 / 收藏白名单按 Key 寻址），只改标题。
        menu.RegisterFunction("settings", "settings-path",         "路径与配置", "📁", "PathView",          2, "运行环境路径 + 多套配置档（分类列区分，可新增 / 切换 / 重命名 / 删除）");
        // 「启动配置」→「一键按钮」：首页三个一键按钮的**全部**配置都在这页（名称 / 程序 / 参数 / 网址）。
        // Key 保持 settings-launch 不变（首页与收藏卡都按它跳转）。
        menu.RegisterFunction("settings", "settings-launch",       "一键按钮", "🚀", "LaunchConfigView",  3, "首页一键按钮的程序与网址");
        menu.RegisterFunction("settings", "settings-dns",          "加密 DNS", "🛡", "DnsView",           4, "加密 DNS 与网络配置");
        menu.RegisterFunction("settings", "settings-data",         "数据管理", "💾", "DataView",          6, "备份 / 导入 config 配置");
        menu.RegisterFunction("settings", "settings-all",          "功能清单", "📋", "AllFunctionsView",  7, "所有模块与功能总览");
    }
}
