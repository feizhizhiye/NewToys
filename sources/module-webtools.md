# NewToys 模块源码 - WebTools

> **用途**：单模块源码快照。在已发送 framework.md 的基础上，改哪个模块就传哪个的 md。
>
> **包含范围**：src/Module.WebTools/ 下所有源码
> **不包含**：框架层（Shared/Core/Shell）、其他模块
> **文件格式**：`FILE: <相对路径>` 段标记，可配合 import-sources.ps1 反向还原

---

## 1. 元信息

| 项 | 值 |
|---|---|
| 模块名 | WebTools |
| 模块目录 | src/Module.WebTools/ |
| 导出生成时间 | 2026-10-09 20:23:19 |
| 仓库根 | C:\Users\zhen\Downloads\NewToys |
| 导出类型 | module |
| 规范版本 | v1.9 |

---

## 2. 模块源码文件

### `FILE: Module.WebTools/Module.WebTools.csproj`

``````xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>NewToys.WebTools</RootNamespace>
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

### `FILE: Module.WebTools/ViewModels/BookmarkViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.WebTools.ViewModels;

/// <summary>
/// 书签管理 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class BookmarkViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "书签管理";
    [ObservableProperty] private string _description = "批量检测书签可用性与导入导出";
    [ObservableProperty] private string _primaryButtonText = "检测全部";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public BookmarkViewModel()
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

### `FILE: Module.WebTools/ViewModels/DnsViewModel.cs`

``````csharp
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.WebTools.ViewModels;

public partial class DnsViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "DNS 检测";
    [ObservableProperty] private string _description = "检测当前系统 DNS 配置与连通性";
    [ObservableProperty] private string _targetDomain = "www.baidu.com";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DnsResultItem> Results { get; } = new();

    public DnsViewModel() { }

    [RelayCommand]
    private async System.Threading.Tasks.Task TestDns()
    {
        IsBusy = true;
        Results.Clear();
        // DNS 真实查询逻辑后续补丁实现；这里仅占位
        await System.Threading.Tasks.Task.Delay(300);
        Results.Add(new DnsResultItem { DnsServer = "119.29.29.29 (DNSPod)", Status = "（待接入实现）", LatencyMs = 0 });
        StatusText = "DNS 检测完成（功能待接入）。";
        IsBusy = false;
    }
}

public partial class DnsResultItem : ObservableObject
{
    [ObservableProperty] private string _dnsServer = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _latencyMs;
}

``````

### `FILE: Module.WebTools/ViewModels/PingViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.WebTools.ViewModels;

/// <summary>
/// Ping 检测 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class PingViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "Ping 检测";
    [ObservableProperty] private string _description = "检测主机连通性与延迟";
    [ObservableProperty] private string _primaryButtonText = "开始 Ping";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public PingViewModel()
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

### `FILE: Module.WebTools/ViewModels/ScrapeViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.WebTools.ViewModels;

/// <summary>
/// 网页抓取 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class ScrapeViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "网页抓取";
    [ObservableProperty] private string _description = "抓取指定 URL 的内容并提取数据";
    [ObservableProperty] private string _primaryButtonText = "开始抓取";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public ScrapeViewModel()
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

### `FILE: Module.WebTools/ViewModels/WebHomeViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.WebTools.ViewModels;

/// <summary>
/// 网络工具 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class WebHomeViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "网络工具";
    [ObservableProperty] private string _description = "HTTP/Ping/书签/DNS/抓取等网络工具";
    [ObservableProperty] private string _primaryButtonText = "打开工具";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public WebHomeViewModel()
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

### `FILE: Module.WebTools/ViewModels/WebHttpViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.WebTools.ViewModels;

/// <summary>
/// HTTP 测试 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class WebHttpViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "HTTP 测试";
    [ObservableProperty] private string _description = "发送 HTTP 请求并查看响应";
    [ObservableProperty] private string _primaryButtonText = "发送请求";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public WebHttpViewModel()
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

### `FILE: Module.WebTools/Views/BookmarkView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.WebTools.Views.BookmarkView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="🔖" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="书签管理" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="导入/导出/检测书签可用性" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="📥 导入书签" Margin="0,0,8,0"/><Button Classes="ghost" Content="▶ 检测可用性" Margin="0,0,8,0"/><Button Classes="ghost" Content="📤 导出清单" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="8"><TextBox PlaceholderText="粘贴浏览器导出的书签 html / 纯文本网址，一行一个" Height="120" AcceptsReturn="True" TextWrapping="Wrap"/><TextBlock Text="支持批量检测链接可用性（200/超时/404）" Foreground="{DynamicResource Text2}"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.WebTools/Views/BookmarkView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.WebTools.Views;
public partial class BookmarkView : UserControl
{
    public BookmarkView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.WebTools/Views/DnsView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.WebTools.Views.DnsView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="🛡" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="加密 DNS" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="切换 DoH 提供商，仅本软件生效" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="✅ 启用加密 DNS" Margin="0,0,8,0"/><Button Classes="ghost" Content="🧹 清除 DNS 缓存" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="10"><RadioButton Content="腾讯 DNSPod (1.12.12.12)" GroupName="dns" IsChecked="True"/><RadioButton Content="阿里 DNS (223.5.5.5)" GroupName="dns"/><RadioButton Content="Cloudflare (1.1.1.1)" GroupName="dns"/><RadioButton Content="Google (8.8.8.8)" GroupName="dns"/><RadioButton Content="自定义" GroupName="dns"/><TextBox PlaceholderText="自定义 DoH URL（https://…）" IsEnabled="False"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.WebTools/Views/DnsView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.WebTools.Views;
public partial class DnsView : UserControl
{
    public DnsView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.WebTools/Views/PingView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.WebTools.Views.PingView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="📡" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="网络 Ping" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="对目标主机批量 Ping / TCP 端口检测，显示延迟" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="▶ 开始检测" Margin="0,0,8,0"/><Button Classes="ghost" Content="⏹ 停止" Margin="0,0,8,0"/><Button Classes="ghost" Content="🗑 清空" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="8"><TextBox PlaceholderText="输入目标（一行一个：域名或 IP，支持 :端口）" Height="100" AcceptsReturn="True"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.WebTools/Views/PingView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.WebTools.Views;
public partial class PingView : UserControl
{
    public PingView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.WebTools/Views/ScrapeView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.WebTools.Views.ScrapeView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="🕷" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="网页抓取" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="按 XPath / CSS 选择器 / 正则 抓取页面链接" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="▶ 开始抓取" Margin="0,0,8,0"/><Button Classes="ghost" Content="📋 复制结果" Margin="0,0,8,0"/><Button Classes="ghost" Content="📤 导出" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="8"><TextBox PlaceholderText="起始 URL（页面 / sitemap.xml / JSON 都行）"/><ComboBox SelectedIndex="0" HorizontalAlignment="Left"><ComboBoxItem Content="XPath"/><ComboBoxItem Content="CSS 选择器"/><ComboBoxItem Content="正则表达式"/></ComboBox><TextBox PlaceholderText="表达式，如 //loc/text()"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.WebTools/Views/ScrapeView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.WebTools.Views;
public partial class ScrapeView : UserControl
{
    public ScrapeView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.WebTools/Views/WebHomeView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.WebTools.Views.WebHomeView">
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
          <TextBlock Text="WebTools 总入口" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><TextBlock Text="选择左侧二级菜单进入网络工具。" Foreground="{DynamicResource Text2}"/></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.WebTools/Views/WebHomeView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.WebTools.Views;
public partial class WebHomeView : UserControl
{
    public WebHomeView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.WebTools/Views/WebHttpView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.WebTools.Views.WebHttpView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="🧪" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="HTTP 测试" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="HTTP 请求测试（GET/POST/PUT/DELETE），查看响应头与状态" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="▶ 发送 GET" Margin="0,0,8,0"/><Button Classes="ghost" Content="▶ POST" Margin="0,0,8,0"/><Button Classes="ghost" Content="🗑 清空" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="8"><TextBox PlaceholderText="输入 URL，如 https://api.example.com/v1/test"/><TextBox PlaceholderText="请求头（Key: Value 一行一个）" Height="80" TextWrapping="Wrap" AcceptsReturn="True"/><TextBox PlaceholderText="请求体" Height="80" TextWrapping="Wrap" AcceptsReturn="True"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.WebTools/Views/WebHttpView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.WebTools.Views;
public partial class WebHttpView : UserControl
{
    public WebHttpView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.WebTools/WebToolsModule.cs`

``````csharp
using Microsoft.Extensions.DependencyInjection;
using NewToys.Module.WebTools.ViewModels;
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
        menu.RegisterFunction("web", "web-home",      "工具箱首页", "🏠", "WebHomeView", 0);
        menu.RegisterFunction("web", "web-http",      "HTTP 测试", "🧪", "WebHttpView", 1);
        menu.RegisterFunction("web", "web-bookmark",  "书签管理", "🔖", "BookmarkView", 2);
        menu.RegisterFunction("web", "web-ping",      "网络 Ping", "📡", "PingView", 3);
        menu.RegisterFunction("web", "web-scrape",    "网页抓取", "🕷", "ScrapeView", 4);
        menu.RegisterFunction("web", "web-dns",       "DNS 检测", "🛡", "DnsView", 5);
    }
}

``````

---

## 3. 导出统计

| 项 | 值 |
|---|---|
| 文件数 | 20 |
| 模块 | Module.WebTools |

