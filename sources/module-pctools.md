# NewToys 模块源码 - PcTools

> **用途**：单模块源码快照。在已发送 framework.md 的基础上，改哪个模块就传哪个的 md。
>
> **包含范围**：src/Module.PcTools/ 下所有源码
> **不包含**：框架层（Shared/Core/Shell）、其他模块
> **文件格式**：`FILE: <相对路径>` 段标记，可配合 import-sources.ps1 反向还原

---

## 1. 元信息

| 项 | 值 |
|---|---|
| 模块名 | PcTools |
| 模块目录 | src/Module.PcTools/ |
| 导出生成时间 | 2026-10-09 20:23:19 |
| 仓库根 | C:\Users\zhen\Downloads\NewToys |
| 导出类型 | module |
| 规范版本 | v1.9 |

---

## 2. 模块源码文件

### `FILE: Module.PcTools/Models/QuickAction.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
namespace NewToys.PcTools.Models;
public partial class QuickAction : ObservableObject
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsFavorite { get; set; }
}

``````

### `FILE: Module.PcTools/Module.PcTools.csproj`

``````xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>NewToys.PcTools</RootNamespace>
    <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Shared/Shared.csproj" />
    <ProjectReference Include="../Core/Core.csproj" />
  </ItemGroup>
</Project>

``````

### `FILE: Module.PcTools/PcToolsModule.cs`

``````csharp
using Microsoft.Extensions.DependencyInjection;
using NewToys.Module.PcTools.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
namespace NewToys.PcTools;
public class PcToolsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<Views.PcHomeView>();          services.AddTransient<PcHomeViewModel>();
        services.AddTransient<Views.FavoritesView>();      services.AddTransient<FavoritesViewModel>();
        services.AddTransient<Views.MyShortcutsView>();    services.AddTransient<MyShortcutsViewModel>();
        services.AddTransient<Views.WingetView>();         services.AddTransient<WingetViewModel>();
        services.AddTransient<Views.PcInfoView>();         services.AddTransient<PcInfoViewModel>();
        services.AddTransient<Views.QuickSettingsView>();  services.AddTransient<QuickSettingsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("pc", "电脑工具", "🖥", 20);
        menu.RegisterFunction("pc", "pc-home", "工具箱首页", "🏠", "PcHomeView", 0);
        menu.RegisterFunction("pc", "pc-favorites", "收藏夹", "★", "FavoritesView", 1);
        menu.RegisterFunction("pc", "pc-shortcuts", "设置与脚本", "▤", "MyShortcutsView", 2);
        menu.RegisterFunction("pc", "pc-winget", "winget 管理", "❐", "WingetView", 3);
        menu.RegisterFunction("pc", "pc-info", "系统信息", "💻", "PcInfoView", 4);
        menu.RegisterFunction("pc", "pc-quicksettings", "快捷设置", "⚙", "QuickSettingsView", 99);
    }
}

``````

### `FILE: Module.PcTools/ViewModels/FavoritesViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.PcTools.ViewModels;

/// <summary>
/// 收藏夹 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class FavoritesViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "收藏夹";
    [ObservableProperty] private string _description = "你收藏的常用功能，点击直达";
    [ObservableProperty] private string _primaryButtonText = "管理收藏";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public FavoritesViewModel()
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

### `FILE: Module.PcTools/ViewModels/MyShortcutsViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.PcTools.ViewModels;

/// <summary>
/// 我的快捷方式 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class MyShortcutsViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "我的快捷方式";
    [ObservableProperty] private string _description = "自定义常用命令与文件快捷方式";
    [ObservableProperty] private string _primaryButtonText = "添加快捷方式";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public MyShortcutsViewModel()
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

### `FILE: Module.PcTools/ViewModels/PcHomeViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.PcTools.ViewModels;

/// <summary>
/// 电脑工具 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class PcHomeViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "电脑工具";
    [ObservableProperty] private string _description = "系统维护与优化工具集";
    [ObservableProperty] private string _primaryButtonText = "打开工具箱";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public PcHomeViewModel()
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

### `FILE: Module.PcTools/ViewModels/PcInfoViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.PcTools.ViewModels;

/// <summary>
/// 系统信息 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class PcInfoViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "系统信息";
    [ObservableProperty] private string _description = "硬件、操作系统与运行环境概览";
    [ObservableProperty] private string _primaryButtonText = "重新检测";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public PcInfoViewModel()
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

### `FILE: Module.PcTools/ViewModels/QuickSettingsViewModel.cs`

``````csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.PcTools.ViewModels;

public partial class QuickSettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "电脑工具快捷设置";
    [ObservableProperty] private string _description = "电脑工具模块的常用配置项";
    [ObservableProperty] private string _primaryButtonText = "保存设置";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public QuickSettingsViewModel() { }

    [RelayCommand]
    private void PrimaryAction()
    {
        StatusText = "设置已保存。";
    }

    [RelayCommand]
    private void Refresh()
    {
        StatusText = $"已刷新 · {DateTime.Now:HH:mm:ss}";
    }
}

``````

### `FILE: Module.PcTools/ViewModels/WingetViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.PcTools.ViewModels;

/// <summary>
/// 软件包管理 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class WingetViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "软件包管理";
    [ObservableProperty] private string _description = "通过 winget 管理已安装软件";
    [ObservableProperty] private string _primaryButtonText = "刷新列表";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public WingetViewModel()
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

### `FILE: Module.PcTools/Views/FavoritesView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.PcTools.Views.FavoritesView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="★" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="收藏夹" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="常用快捷指令磁贴墙，点击即执行" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="ghost" Content="⚙ 打开设置与脚本" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><TextBlock Text="（功能：显示被收藏的 QuickAction 磁贴，点击执行；✕ 按钮从收藏移除。后端 QuickActionStore 已就绪，UI 待绑定。）" Foreground="{DynamicResource Text2}" TextWrapping="Wrap"/></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.PcTools/Views/FavoritesView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.PcTools.Views;
public partial class FavoritesView : UserControl
{
    public FavoritesView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.PcTools/Views/MyShortcutsView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.PcTools.Views.MyShortcutsView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="▤" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="设置与脚本" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="快捷指令库管理，可自定义添加 / 编辑参数 / 合并内置" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="➕ 新增命令" Margin="0,0,8,0"/><Button Classes="ghost" Content="📂 打开数据目录" Margin="0,0,8,0"/><Button Classes="ghost" Content="🔄 合并内置" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><TextBlock Text="（功能：搜索/分类浏览 QuickAction，支持运行/编辑/删除/合并/导入导出；后端 MyShortcutsViewModel + QuickActionStore 已就绪，UI 待绑定。）" Foreground="{DynamicResource Text2}" TextWrapping="Wrap"/></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.PcTools/Views/MyShortcutsView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.PcTools.Views;
public partial class MyShortcutsView : UserControl
{
    public MyShortcutsView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.PcTools/Views/PcHomeView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.PcTools.Views.PcHomeView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="🏠" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="工具箱首页" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="PcTools 总入口，快捷进入常用功能" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><TextBlock Text='🚧 该模块的首页汇总卡片将从 ViewModel 驱动加载，这里显示占位。' Foreground='{DynamicResource Text2}'/></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.PcTools/Views/PcHomeView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.PcTools.Views;
public partial class PcHomeView : UserControl
{
    public PcHomeView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.PcTools/Views/PcInfoView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.PcTools.Views.PcInfoView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="💻" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="系统信息" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="硬件、系统、网络概览" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="🔄 刷新" Margin="0,0,8,0"/><Button Classes="ghost" Content="📋 复制到剪贴板" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><Grid RowDefinitions="Auto,Auto,Auto,Auto,Auto" ColumnDefinitions="120,*" Margin="16"><TextBlock Text="操作系统" Foreground="{DynamicResource Text2}"/><TextBlock Grid.Column="1" Text="检测中……" Foreground="{DynamicResource Text1}"/><TextBlock Grid.Row="1" Text="CPU" Foreground="{DynamicResource Text2}"/><TextBlock Grid.Row="1" Grid.Column="1" Text="—" Foreground="{DynamicResource Text1}"/><TextBlock Grid.Row="2" Text="内存" Foreground="{DynamicResource Text2}"/><TextBlock Grid.Row="2" Grid.Column="1" Text="—" Foreground="{DynamicResource Text1}"/><TextBlock Grid.Row="3" Text="磁盘" Foreground="{DynamicResource Text2}"/><TextBlock Grid.Row="3" Grid.Column="1" Text="—" Foreground="{DynamicResource Text1}"/><TextBlock Grid.Row="4" Text="网络" Foreground="{DynamicResource Text2}"/><TextBlock Grid.Row="4" Grid.Column="1" Text="—" Foreground="{DynamicResource Text1}"/></Grid></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.PcTools/Views/PcInfoView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.PcTools.Views;
public partial class PcInfoView : UserControl
{
    public PcInfoView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.PcTools/Views/QuickSettingsView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.PcTools.Views.QuickSettingsView">
  <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="32">
    <StackPanel Spacing="20" MaxWidth="760" HorizontalAlignment="Stretch">

      <!-- 页面标题区 -->
      <StackPanel Spacing="6">
        <StackPanel Orientation="Horizontal" Spacing="8">
          <TextBlock Text="{Binding PageIcon}" FontSize="26" VerticalAlignment="Center"/>
          <TextBlock Text="{Binding PageTitle}" FontSize="24" FontWeight="Bold"
                     Foreground="{DynamicResource TextPrimary}" VerticalAlignment="Center"/>
        </StackPanel>
        <TextBlock Text="{Binding PageDescription}" FontSize="13"
                   Foreground="{DynamicResource TextSecondary}" TextWrapping="Wrap"/>
      </StackPanel>

      <!-- 主内容卡片（占位） -->
      <Border Background="{DynamicResource CardBackground}" CornerRadius="8" Padding="24">
        <StackPanel Spacing="16">
          <TextBlock Text="🚧 功能开发中" FontSize="15" FontWeight="SemiBold"
                     Foreground="{DynamicResource TextPrimary}"/>
          <TextBlock Text="本页面已完成 UI 骨架搭建，具体业务逻辑将在后续版本迭代中实现。"
                     FontSize="13" Foreground="{DynamicResource TextSecondary}" TextWrapping="Wrap"/>
          <Separator/>
          <Grid ColumnDefinitions="Auto,*" ColumnSpacing="12" RowDefinitions="Auto,Auto,Auto" RowSpacing="10">
            <TextBlock Grid.Row="0" Grid.Column="0" Text="页面状态" FontSize="13"
                       Foreground="{DynamicResource TextSecondary}" VerticalAlignment="Center"/>
            <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding StatusText}" FontSize="13"
                       Foreground="{DynamicResource TextPrimary}" VerticalAlignment="Center"/>
            <TextBlock Grid.Row="1" Grid.Column="0" Text="是否加载中" FontSize="13"
                       Foreground="{DynamicResource TextSecondary}" VerticalAlignment="Center"/>
            <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding IsBusy}" FontSize="13"
                       Foreground="{DynamicResource TextPrimary}" VerticalAlignment="Center"/>
          </Grid>
          <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Left">
            <Button Content="刷新" Classes="accent" Height="36" Padding="20,0"
                    IsEnabled="{Binding !IsBusy}"/>
            <Button Content="设置" Classes="ghost" Height="36" Padding="20,0"/>
          </StackPanel>
        </StackPanel>
      </Border>

      <!-- 提示卡片 -->
      <Border Background="{DynamicResource CardBackground}" CornerRadius="8" Padding="20">
        <StackPanel Spacing="6">
          <TextBlock Text="💡 提示" FontSize="14" FontWeight="SemiBold"
                     Foreground="{DynamicResource TextPrimary}"/>
          <TextBlock Text="所有页面严格遵循 MVVM 模式：View（axaml）只负责展示，ViewModel 暴露可绑定属性与命令，业务逻辑由 Service 层承担。"
                     FontSize="12" Foreground="{DynamicResource TextSecondary}" TextWrapping="Wrap"/>
        </StackPanel>
      </Border>
    </StackPanel>
  </ScrollViewer>
</UserControl>

``````

### `FILE: Module.PcTools/Views/QuickSettingsView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.PcTools.Views;
public partial class QuickSettingsView : UserControl
{
    public QuickSettingsView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.PcTools/Views/WingetView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.PcTools.Views.WingetView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="❐" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="winget 管理" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="winget 包管理：源管理、搜索、安装、升级、卸载" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="🔍 搜索" Margin="0,0,8,0"/><Button Classes="ghost" Content="📦 刷新已安装" Margin="0,0,8,0"/><Button Classes="ghost" Content="⬆ 检查全部升级" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="8"><TextBlock Text="Winget 已安装软件清单将在这里显示。" Foreground="{DynamicResource Text2}"/><TextBox PlaceholderText="搜索包名 / id……" Margin="0,4,0,0"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.PcTools/Views/WingetView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.PcTools.Views;
public partial class WingetView : UserControl
{
    public WingetView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

---

## 3. 导出统计

| 项 | 值 |
|---|---|
| 文件数 | 21 |
| 模块 | Module.PcTools |

