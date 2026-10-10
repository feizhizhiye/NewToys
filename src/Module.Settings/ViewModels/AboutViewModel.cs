using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] private string _appName = "NewToys";
    /// <summary>InformationalVersion（编译时由 src\Directory.Build.props 自动生成，禁止手写）。</summary>
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _buildConfiguration = "";
    [ObservableProperty] private string _runtimeDirectory = AppContext.BaseDirectory;
    [ObservableProperty] private string _dataDirectory = "";
    [ObservableProperty] private string _description = "个人高强度使用工具集";

    public ObservableCollection<TechStackItem> TechStack { get; } = new();
    public ObservableCollection<string> CoreConstraints { get; } = new();

    public AboutViewModel(IAppConfigService? cfg = null, ConfigStoreService? store = null)
    {
        _version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                       ?.InformationalVersion ?? "unknown";
#if DEBUG
        _buildConfiguration = "Debug";
#else
        _buildConfiguration = "Release";
#endif
        _dataDirectory = store?.DataDir ?? cfg?.DataDirectory ?? "";

        TechStack.Add(new TechStackItem { Name = ".NET", Ver = "10" });
        TechStack.Add(new TechStackItem { Name = "Avalonia UI", Ver = "12" });
        TechStack.Add(new TechStackItem { Name = "CommunityToolkit.Mvvm", Ver = "8.x" });
        TechStack.Add(new TechStackItem { Name = "Microsoft.Extensions.DependencyInjection", Ver = "9.x" });

        CoreConstraints.Add("项目分 7 层：Shared / Core / Shell + PcTools / WebTools / VideoTools / Settings 四个模块");
        CoreConstraints.Add("依赖方向单向：Module.* → Core → Shared；模块禁止反向引用 Shell，模块之间禁止互相引用");
        CoreConstraints.Add("菜单 100% 由 IMenuService 注册，Shell 里禁止硬编码任何模块名 / 页面名 / 导航键");
        CoreConstraints.Add("Debug 走显式模块类型清单，Release 只扫 <程序目录>\\Modules\\");
        CoreConstraints.Add("View ↔ ViewModel 按约定自动配对：XxxView ↔ 同命名空间 .ViewModels.XxxViewModel");
        CoreConstraints.Add("主题 Token 用 *Brush 语义键；数据持久化在 <应用根>\\data");
    }

    [RelayCommand]
    private void OpenRepo()
    {
        // 预留：打开仓库地址
    }
}

public class TechStackItem
{
    public string Name { get; set; } = "";
    public string Ver { get; set; } = "";
}
