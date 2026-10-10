# NewToys 模块源码 - Settings

> **用途**：单模块源码快照。在已发送 framework.md 的基础上，改哪个模块就传哪个的 md。
>
> **包含范围**：src/Module.Settings/ 下所有源码
> **不包含**：框架层（Shared/Core/Shell）、其他模块
> **文件格式**：`FILE: <相对路径>` 段标记，可配合 import-sources.ps1 反向还原

---

## 1. 元信息

| 项 | 值 |
|---|---|
| 模块名 | Settings |
| 模块目录 | src/Module.Settings/ |
| 导出生成时间 | 2026-10-09 20:23:19 |
| 仓库根 | C:\Users\zhen\Downloads\NewToys |
| 导出类型 | module |
| 规范版本 | v1.9 |

---

## 2. 模块源码文件

### `FILE: Module.Settings/Module.Settings.csproj`

``````xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>NewToys.Settings</RootNamespace>
    <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Shared/Shared.csproj" />
    <ProjectReference Include="../Core/Core.csproj" />
  </ItemGroup>
</Project>

``````

### `FILE: Module.Settings/SettingsModule.cs`

``````csharp
using Microsoft.Extensions.DependencyInjection;
using NewToys.Module.Settings.ViewModels;
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
        services.AddTransient<Views.DataView>();           services.AddTransient<DataViewModel>();
        services.AddTransient<Views.AboutView>();          services.AddTransient<AboutViewModel>();
        services.AddTransient<Views.AllFunctionsView>();   services.AddTransient<AllFunctionsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("settings", "设置", "⚙", 999, isBottom: true);
        menu.RegisterFunction("settings", "settings-general",      "通用设置", "⚙", "GeneralView",       0);
        menu.RegisterFunction("settings", "settings-theme",        "主题外观", "🎨", "ThemeView",         1);
        menu.RegisterFunction("settings", "settings-path",         "路径配置", "📁", "PathView",          2);
        menu.RegisterFunction("settings", "settings-launch",       "启动配置", "🚀", "LaunchConfigView",  3);
        menu.RegisterFunction("settings", "settings-dns",          "加密 DNS", "🛡", "DnsView",           4);
        menu.RegisterFunction("settings", "settings-data",         "数据管理", "💾", "DataView",          5);
        menu.RegisterFunction("settings", "settings-about",        "关于",     "ℹ", "AboutView",         6);
        menu.RegisterFunction("settings", "settings-all",          "功能清单", "📋", "AllFunctionsView",  7);
    }
}

``````

### `FILE: Module.Settings/ViewModels/AboutViewModel.cs`

``````csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;

namespace NewToys.Module.Settings.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] private string _appName = "NewToys";
    [ObservableProperty] private string _version = "v0.2.0";
    [ObservableProperty] private string _description = "个人高强度使用工具集";

    public ObservableCollection<TechStackItem> TechStack { get; } = new();
    public ObservableCollection<string> CoreConstraints { get; } = new();

    public AboutViewModel()
    {
        TechStack.Add(new TechStackItem { Name = ".NET", Ver = "10" });
        TechStack.Add(new TechStackItem { Name = "Avalonia UI", Ver = "12" });
        TechStack.Add(new TechStackItem { Name = "CommunityToolkit.Mvvm", Ver = "8.x" });
        TechStack.Add(new TechStackItem { Name = "Microsoft.Extensions.DependencyInjection", Ver = "9.x" });

        CoreConstraints.Add("项目分 7 层：Shared / Core / Shell + PcTools / WebTools / VideoTools / Settings 四个模块");
        CoreConstraints.Add("依赖方向单向：Shell → Module.* → Core → Shared；模块禁止反向引用 Shell");
        CoreConstraints.Add("框架层（Shared/Core/Shell）冻结，新增功能走 Module 插件化");
        CoreConstraints.Add("严格三栏仿微信布局：左侧侧栏 / 中间二级菜单 / 右侧内容区");
        CoreConstraints.Add("严格 MVVM：View 不直接操作业务，业务逻辑在 ViewModel / Service");
        CoreConstraints.Add("DI 容器统一管理服务注册，所有 ViewModel 通过 DI 解析");
        CoreConstraints.Add("用户数据持久化在 var/config/ 目录，支持备份与导入");
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

``````

### `FILE: Module.Settings/ViewModels/AllFunctionsViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.Settings.ViewModels;

/// <summary>
/// 功能清单 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class AllFunctionsViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "功能清单";
    [ObservableProperty] private string _description = "查看本软件所有可用功能";
    [ObservableProperty] private string _primaryButtonText = "刷新列表";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public AllFunctionsViewModel()
    {
    }

    [RelayCommand]
    private void PrimaryAction()
    {
        StatusText = "主操作已触发，功能将在后续版本实现。";
    }

    [RelayCommand]
    private void Refresh()
    {
        StatusText = $"已刷新 · {DateTime.Now:HH:mm:ss}";
    }
}

``````

### `FILE: Module.Settings/ViewModels/DataViewModel.cs`

``````csharp
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.Settings.ViewModels;

public partial class DataViewModel : ObservableObject
{
    public ObservableCollection<ConfigFileDisplay> ConfigFiles { get; } = new();

    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _lastBackupPath = "";
    [ObservableProperty] private bool _isBusy;

    public DataViewModel()
    {
        RefreshFileList();
    }

    [RelayCommand]
    private void RefreshFileList()
    {
        // 真正的文件枚举由 DataView.axaml.cs 在加载时调用 Refresh 命令完成；
        // 这里先清空展示，等待 code-behind 填充。
        ConfigFiles.Clear();
        StatusText = $"配置列表已刷新 · {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private void BackupNow()
    {
        StatusText = "请点击「备份到文件」按钮选择保存位置。";
    }

    [RelayCommand]
    private void ImportNow()
    {
        StatusText = "请点击「从备份导入」按钮选择备份文件。";
    }
}

public partial class ConfigFileDisplay : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private DateTime _lastWrite;
    public string SizeDisplay
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:0.0} KB";
            return $"{SizeBytes / 1024.0 / 1024.0:0.1} MB";
        }
    }
}

``````

### `FILE: Module.Settings/ViewModels/DnsViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.Settings.ViewModels;

/// <summary>
/// 加密 DNS 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class DnsViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "加密 DNS";
    [ObservableProperty] private string _description = "配置系统加密 DNS 服务器";
    [ObservableProperty] private string _primaryButtonText = "应用 DNS";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public DnsViewModel()
    {
    }

    [RelayCommand]
    private void PrimaryAction()
    {
        StatusText = "主操作已触发，功能将在后续版本实现。";
    }

    [RelayCommand]
    private void Refresh()
    {
        StatusText = $"已刷新 · {DateTime.Now:HH:mm:ss}";
    }
}

``````

### `FILE: Module.Settings/ViewModels/GeneralViewModel.cs`

``````csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.Settings.ViewModels;

public partial class GeneralViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "通用设置";
    [ObservableProperty] private string _description = "开机启动、窗口、更新等通用选项";
    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _checkUpdateOnStartup = true;
    [ObservableProperty] private bool _confirmOnExit = true;
    [ObservableProperty] private string _statusText = "就绪";

    public GeneralViewModel() { }

    [RelayCommand]
    private void Save()
    {
        StatusText = "通用设置已保存。";
    }
}

``````

### `FILE: Module.Settings/ViewModels/LaunchConfigViewModel.cs`

``````csharp
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Module.Settings.ViewModels;

public partial class LaunchConfigViewModel : ObservableObject
{
    private readonly ILaunchConfigService? _launch;

    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _startMinimized;
    public ObservableCollection<LaunchProgramItem> Programs { get; } = new();
    [ObservableProperty] private string _statusText = "就绪";

    public LaunchConfigViewModel() { }
    public LaunchConfigViewModel(ILaunchConfigService launch)
    {
        _launch = launch;
        var cfg = launch.Config;
        StartWithSystem = cfg.StartWithSystem;
        StartMinimized = cfg.StartMinimized;
        foreach (var p in cfg.Programs)
        {
            Programs.Add(new LaunchProgramItem { Name = p.Name, ProgramPath = p.ProgramPath, Arguments = p.Arguments });
        }
    }

    [RelayCommand]
    private void AddProgram()
    {
        Programs.Add(new LaunchProgramItem { Name = "新程序", ProgramPath = "", Arguments = "" });
    }

    [RelayCommand]
    private void RemoveProgram(LaunchProgramItem? item)
    {
        if (item != null) Programs.Remove(item);
    }

    [RelayCommand]
    private void Save()
    {
        if (_launch == null) { StatusText = "启动配置已保存（运行时未就绪）。"; return; }
        var cfg = new LaunchConfig
        {
            StartWithSystem = StartWithSystem,
            StartMinimized = StartMinimized,
            CheckUpdateOnStartup = true
        };
        foreach (var p in Programs)
            cfg.Programs.Add(new LaunchButtonConfig { Name = p.Name, ProgramPath = p.ProgramPath, Arguments = p.Arguments });
        var (ok, msg) = _launch.Save(cfg);
        StatusText = ok ? "启动配置已保存。" : $"保存失败：{msg}";
    }
}

public partial class LaunchProgramItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _programPath = "";
    [ObservableProperty] private string _arguments = "";
}

``````

### `FILE: Module.Settings/ViewModels/PathViewModel.cs`

``````csharp
using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Module.Settings.ViewModels;

public partial class PathViewModel : ObservableObject
{
    private readonly ConfigStoreService? _cfg;

    [ObservableProperty] private string _browser1Path = "";
    [ObservableProperty] private string _browser2Path = "";
    [ObservableProperty] private string _downloadFolder = "";
    [ObservableProperty] private string _statusText = "就绪";

    public PathViewModel() { }
    public PathViewModel(ConfigStoreService cfg)
    {
        _cfg = cfg;
        DownloadFolder = cfg.DataDir;
    }

    [RelayCommand]
    private void Save()
    {
        StatusText = "路径配置已保存。";
    }

    [RelayCommand]
    private void OpenConfigDir()
    {
        try
        {
            var dir = _cfg?.ConfigDir ?? Path.Combine(AppContext.BaseDirectory, "var", "config");
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch { StatusText = "无法打开目录。"; }
    }
}

``````

### `FILE: Module.Settings/ViewModels/ThemeViewModel.cs`

``````csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;

namespace NewToys.Module.Settings.ViewModels;

public partial class ThemeViewModel : ObservableObject
{
    private readonly IThemeService? _theme;

    [ObservableProperty] private string _title = "主题外观";
    [ObservableProperty] private string _description = "深色 / 浅色主题与强调色";
    [ObservableProperty] private bool _isDark = true;
    [ObservableProperty] private bool _followSystem;
    [ObservableProperty] private string _statusText = "就绪";

    public ThemeViewModel() { }

    public ThemeViewModel(IThemeService theme)
    {
        _theme = theme;
        IsDark = theme.IsDark;
    }

    partial void OnIsDarkChanged(bool value)
    {
        _theme?.SetDark(value);
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        _theme?.SetDark(IsDark);
        StatusText = $"已切换到 {(IsDark ? "深色" : "浅色")}主题。";
    }
}

``````

### `FILE: Module.Settings/Views/AboutView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Settings.Views.AboutView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="ℹ" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="关于" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="版本、技术栈、核心约束" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="🔄 检查更新" Margin="0,0,8,0"/><Button Classes="ghost" Content="📂 打开项目主页" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="12"><Border Classes="card"><StackPanel Spacing="4"><TextBlock Text="NewToys" FontSize="22" FontWeight="Bold" Foreground="{DynamicResource Text1}"/><TextBlock Text="{Binding Version}" Foreground="{DynamicResource Accent}" FontWeight="SemiBold"/><TextBlock Text="个人高强度使用工具集 · 跨平台（Windows / macOS / Linux）" Foreground="{DynamicResource Text2}" TextWrapping="Wrap"/></StackPanel></Border><Border Classes="card"><StackPanel Spacing="4"><TextBlock Text="技术栈" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/><TextBlock Text=".NET 10 · Avalonia 12.1.3 · CommunityToolkit.Mvvm 8.4.2 · Microsoft.Extensions.DependencyInjection 9.0.0" Foreground="{DynamicResource Text2}" TextWrapping="Wrap"/></StackPanel></Border></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.Settings/Views/AboutView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Settings.Views;
public partial class AboutView : UserControl
{
    public AboutView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.Settings/Views/AllFunctionsView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Settings.Views.AllFunctionsView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="📋" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="功能清单" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="所有功能总览，点击可跳转" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><TextBlock Text="所有模块与功能的清单树，点击跳转对应页面。" Foreground="{DynamicResource Text2}"/></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.Settings/Views/AllFunctionsView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Settings.Views;
public partial class AllFunctionsView : UserControl
{
    public AllFunctionsView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.Settings/Views/DataView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Settings.Views.DataView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="💾" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="数据管理" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="配置备份 / 导入 / 查看" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0" Spacing="8">
        <Button x:Name="BtnRefresh" Classes="ghost" Content="🔄 刷新"/>
        <Button x:Name="BtnBackup" Classes="primary" Content="📤 备份到文件"/>
        <Button x:Name="BtnImport" Classes="ghost" Content="📥 从备份导入"/>
      </StackPanel>
    </Grid>

    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" Padding="24">
      <StackPanel Spacing="16">
        <Border Classes="card" Padding="20">
          <StackPanel Spacing="8">
            <TextBlock Text="当前配置文件" FontWeight="SemiBold" FontSize="14" Foreground="{DynamicResource Text1}"/>
            <TextBlock Text="{Binding StatusText}" Foreground="{DynamicResource Text2}" FontSize="12"/>
            <ItemsControl ItemsSource="{Binding ConfigFiles}">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <Border Padding="10,8" BorderBrush="{DynamicResource Border}" BorderThickness="0,0,0,1">
                    <Grid ColumnDefinitions="*,120,180">
                      <TextBlock Grid.Column="0" Text="{Binding Name}" Foreground="{DynamicResource Text1}" VerticalAlignment="Center"/>
                      <TextBlock Grid.Column="1" Text="{Binding SizeDisplay}" Foreground="{DynamicResource Text2}" FontSize="12" VerticalAlignment="Center"/>
                      <TextBlock Grid.Column="2" Text="{Binding LastWrite, StringFormat='{}{0:yyyy-MM-dd HH:mm:ss}'}" Foreground="{DynamicResource Text2}" FontSize="12" VerticalAlignment="Center"/>
                    </Grid>
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </StackPanel>
        </Border>

        <Border Classes="card" Padding="20">
          <StackPanel Spacing="6">
            <TextBlock Text="📤 备份" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
            <TextBlock Text="把 var/config 下所有 .json 打包为单个 JSON 备份文件，便于跨机迁移。" Foreground="{DynamicResource Text2}" TextWrapping="Wrap"/>
          </StackPanel>
        </Border>

        <Border Classes="card" Padding="20">
          <StackPanel Spacing="6">
            <TextBlock Text="📥 导入" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
            <TextBlock Text="导入前会自动把当前所有配置备份到 var/config/.bak-时间戳/ 目录；导入后建议重启应用。" Foreground="{DynamicResource Text2}" TextWrapping="Wrap"/>
          </StackPanel>
        </Border>
      </StackPanel>
    </ScrollViewer>
  </Grid>
</UserControl>

``````

### `FILE: Module.Settings/Views/DataView.axaml.cs`

``````csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NewToys.Core.Services;
using NewToys.Module.Settings.ViewModels;

namespace NewToys.Settings.Views;

public partial class DataView : UserControl
{
    public DataView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DataViewModel vm) vm.RefreshFileListCommand.Execute(null);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // 找按钮并挂事件（通过名字）
        var btnBackup = this.FindControl<Button>("BtnBackup");
        var btnImport = this.FindControl<Button>("BtnImport");
        var btnRefresh = this.FindControl<Button>("BtnRefresh");
        if (btnBackup != null) btnBackup.Click += OnBackup;
        if (btnImport != null) btnImport.Click += OnImport;
        if (btnRefresh != null) btnRefresh.Click += (_, _) => RefreshList();
        RefreshList();
    }

    private void RefreshList()
    {
        try
        {
            var cfgStore = AppServices.Get<ConfigStoreService>();
            if (cfgStore == null || DataContext is not DataViewModel vm) return;
            vm.ConfigFiles.Clear();
            if (!Directory.Exists(cfgStore.ConfigDir)) return;
            foreach (var f in Directory.GetFiles(cfgStore.ConfigDir, "*.json").OrderBy(x => x))
            {
                var fi = new FileInfo(f);
                vm.ConfigFiles.Add(new ConfigFileDisplay { Name = fi.Name, SizeBytes = fi.Length, LastWrite = fi.LastWriteTime });
            }
            vm.StatusText = $"已列出 {vm.ConfigFiles.Count} 个配置文件";
        }
        catch (Exception ex)
        {
            if (DataContext is DataViewModel vm) vm.StatusText = $"刷新失败：{ex.Message}";
        }
    }

    private async void OnBackup(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cfgStore = AppServices.Get<ConfigStoreService>();
            if (cfgStore == null || DataContext is not DataViewModel vm) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "备份配置",
                SuggestedFileName = $"newtoys-config-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON 备份") { Patterns = new[] { "*.json" } } }
            });
            if (file == null) return;

            var entries = new List<object>();
            foreach (var f in Directory.GetFiles(cfgStore.ConfigDir, "*.json"))
            {
                entries.Add(new
                {
                    path = Path.GetFileName(f),
                    content = File.ReadAllText(f)
                });
            }
            var json = JsonSerializer.Serialize(new { version = 1, exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), files = entries },
                new JsonSerializerOptions { WriteIndented = true });
            await using var stream = await file.OpenWriteAsync();
            await using var sw = new StreamWriter(stream);
            await sw.WriteAsync(json);

            vm.LastBackupPath = file.Path.LocalPath;
            vm.StatusText = $"备份成功：{file.Path.LocalPath}";
            RefreshList();
        }
        catch (Exception ex)
        {
            if (DataContext is DataViewModel vm) vm.StatusText = $"备份失败：{ex.Message}";
        }
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cfgStore = AppServices.Get<ConfigStoreService>();
            if (cfgStore == null || DataContext is not DataViewModel vm) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择备份文件",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON 备份") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0) return;
            var pick = files[0];

            // 二次确认
            var dialog = new Window
            {
                Width = 460, Height = 200,
                Title = "确认导入",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new SolidColorBrush(Color.Parse("#1F1F1F")),
                Content = new Border
                {
                    Padding = new Thickness(20),
                    Child = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "导入将覆盖当前配置文件，导入前会自动备份现有配置到 .bak-*/ 目录。是否继续？",
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = new SolidColorBrush(Color.Parse("#EFEFEF"))
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Spacing = 8,
                                Children =
                                {
                                    new Button { Content = "取消", Width = 80 },
                                    new Button { Content = "确认导入", Width = 90 }
                                }
                            }
                        }
                    }
                }
            };
            var canceled = true;
            if (dialog.Content is Border b && b.Child is StackPanel sp2 && sp2.Children[1] is StackPanel btns)
            {
                ((Button)btns.Children[0]).Click += (_, _) => { dialog.Close(); };
                ((Button)btns.Children[1]).Click += (_, _) => { canceled = false; dialog.Close(); };
            }
            await dialog.ShowDialog(topLevel as Window ?? throw new InvalidOperationException("no owner window"));
            if (canceled) { vm.StatusText = "已取消导入。"; return; }

            // 执行导入
            string json;
            await using (var stream = await pick.OpenReadAsync())
            using (var sr = new StreamReader(stream))
                json = await sr.ReadToEndAsync();

            var bakDir = Path.Combine(cfgStore.ConfigDir, $".bak-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(bakDir);
            foreach (var f in Directory.GetFiles(cfgStore.ConfigDir, "*.json"))
            {
                File.Copy(f, Path.Combine(bakDir, Path.GetFileName(f)), overwrite: true);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int imported = 0;
            if (root.TryGetProperty("files", out var arr))
            {
                foreach (var entry in arr.EnumerateArray())
                {
                    var rel = entry.GetProperty("path").GetString()!;
                    var content = entry.GetProperty("content").GetString() ?? "";
                    // 防路径穿越
                    var target = Path.GetFullPath(Path.Combine(cfgStore.ConfigDir, rel));
                    if (!target.StartsWith(Path.GetFullPath(cfgStore.ConfigDir) + Path.DirectorySeparatorChar))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, content);
                    imported++;
                }
            }

            vm.StatusText = $"导入完成：{imported} 个文件，旧配置已备份到 {bakDir}。建议重启应用。";
            RefreshList();
        }
        catch (Exception ex)
        {
            if (DataContext is DataViewModel vm) vm.StatusText = $"导入失败：{ex.Message}";
        }
    }
}

``````

### `FILE: Module.Settings/Views/DnsView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Settings.Views.DnsView">
  <Grid RowDefinitions="Auto,*">
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0">
        <TextBlock Text="🛡" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="加密 DNS" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="为软件内 HTTP 请求启用 DoH（仅本软件生效）" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="✅ 应用" Command="{Binding ApplyCommand}" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <StackPanel Margin="16">
          <CheckBox Content="启用加密 DNS (DoH)" IsChecked="{Binding Enabled}"/>
          <TextBlock Text="提供商" Foreground="{DynamicResource Text2}"/>
          <ComboBox SelectedIndex="{Binding ProviderIndex}" ItemsSource="{Binding Providers}" HorizontalAlignment="Left" Width="260"/>
          <TextBox Text="{Binding CustomUrl}" PlaceholderText="自定义 DoH URL（https://…）" IsEnabled="{Binding CustomEnabled}" Margin="0,8,0,0"/>
        </StackPanel>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.Settings/Views/DnsView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Settings.Views;
public partial class DnsView : UserControl
{
    public DnsView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.Settings/Views/GeneralView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Settings.Views.GeneralView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="⚙" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="通用设置" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="开机启动、最小化到托盘、更新检查、语言" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="💾 保存" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="10"><CheckBox Content="开机时启动 NewToys"/><CheckBox Content="启动时最小化到托盘"/><CheckBox Content="启动时检查更新"/><CheckBox Content="关闭按钮最小化到托盘"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.Settings/Views/GeneralView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Settings.Views;
public partial class GeneralView : UserControl
{
    public GeneralView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.Settings/Views/LaunchConfigView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Settings.Views.LaunchConfigView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="🚀" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="启动配置" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="一键按钮配置（浏览器1/2 + 批量启动软件列表）" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="💾 保存" Margin="0,0,8,0"/><Button Classes="ghost" Content="🔄 还原默认" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="10"><TextBlock Text="浏览器1：网址（一行一个，支持 # 注释行）" Foreground="{DynamicResource Text2}"/><TextBox Height="120" AcceptsReturn="True" TextWrapping="Wrap" FontFamily="Consolas"/><TextBlock Text="批量启动软件列表" Foreground="{DynamicResource Text2}" Margin="0,8,0,0"/><TextBox Height="100" AcceptsReturn="True" TextWrapping="Wrap" PlaceholderText="一行一个，格式：显示名 = 程序路径 参数"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.Settings/Views/LaunchConfigView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Settings.Views;
public partial class LaunchConfigView : UserControl
{
    public LaunchConfigView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.Settings/Views/PathView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Settings.Views.PathView">
  <Grid RowDefinitions="Auto,*">
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="📁" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="路径配置" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="浏览器、ffmpeg、视频默认目录" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="💾 保存" Command="{Binding SaveCommand}" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto">
          <StackPanel Margin="16" Spacing="14">
            <TextBlock Text="浏览器 1" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
            <TextBox Text="{Binding Browser1}" PlaceholderText="C:/Program Files/Google/Chrome/Application/chrome.exe"/>
            <TextBox Text="{Binding Browser1Args}" PlaceholderText="启动参数，网址会自动追加到后面"/>

            <TextBlock Text="浏览器 2" FontWeight="SemiBold" Foreground="{DynamicResource Text1}" Margin="0,6,0,0"/>
            <TextBox Text="{Binding Browser2}" PlaceholderText="Edge / Firefox 路径"/>
            <TextBox Text="{Binding Browser2Args}" PlaceholderText="启动参数"/>

            <TextBlock Text="多媒体与数据" FontWeight="SemiBold" Foreground="{DynamicResource Text1}" Margin="0,6,0,0"/>
            <TextBox Text="{Binding FfmpegPath}" PlaceholderText="ffmpeg 路径（留空则使用 PATH）"/>
            <TextBox Text="{Binding PlayerPath}" PlaceholderText="外部播放器路径（留空则系统默认）"/>
            <TextBox Text="{Binding VideoFolder}" PlaceholderText="视频库默认目录"/>

            <TextBlock Text="{Binding Status}" Foreground="{DynamicResource Accent}" FontSize="12" Margin="0,8,0,0"/>
          </StackPanel>
        </ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.Settings/Views/PathView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Settings.Views;
public partial class PathView : UserControl
{
    public PathView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.Settings/Views/ThemeView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Settings.Views.ThemeView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="🎨" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="主题外观" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="深色 / 浅色模式、强调色" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="💾 应用" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="10"><TextBlock Text="主题模式" Foreground="{DynamicResource Text2}"/><RadioButton Content="🌙 深色主题（推荐）" GroupName="theme" IsChecked="True"/><RadioButton Content="☀ 浅色主题" GroupName="theme"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.Settings/Views/ThemeView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Settings.Views;
public partial class ThemeView : UserControl
{
    public ThemeView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

---

## 3. 导出统计

| 项 | 值 |
|---|---|
| 文件数 | 26 |
| 模块 | Module.Settings |

