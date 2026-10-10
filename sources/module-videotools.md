# NewToys 模块源码 - VideoTools

> **用途**：单模块源码快照。在已发送 framework.md 的基础上，改哪个模块就传哪个的 md。
>
> **包含范围**：src/Module.VideoTools/ 下所有源码
> **不包含**：框架层（Shared/Core/Shell）、其他模块
> **文件格式**：`FILE: <相对路径>` 段标记，可配合 import-sources.ps1 反向还原

---

## 1. 元信息

| 项 | 值 |
|---|---|
| 模块名 | VideoTools |
| 模块目录 | src/Module.VideoTools/ |
| 导出生成时间 | 2026-10-09 20:23:19 |
| 仓库根 | C:\Users\zhen\Downloads\NewToys |
| 导出类型 | module |
| 规范版本 | v1.9 |

---

## 2. 模块源码文件

### `FILE: Module.VideoTools/Module.VideoTools.csproj`

``````xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>NewToys.VideoTools</RootNamespace>
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

### `FILE: Module.VideoTools/VideoToolsModule.cs`

``````csharp
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Module.VideoTools.ViewModels;
namespace NewToys.VideoTools;
public class VideoToolsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<Views.VideoListView>();   services.AddTransient<VideoListViewModel>();
        services.AddTransient<Views.VideoCutView>();    services.AddTransient<VideoCutViewModel>();
        services.AddTransient<Views.TranscodeView>();   services.AddTransient<TranscodeViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("video", "视频工具", "🎬", 40);
        menu.RegisterFunction("video", "video-list", "视频库", "📁", "VideoListView", 0);
        menu.RegisterFunction("video", "video-cut", "视频剪辑", "✂", "VideoCutView", 1);
        menu.RegisterFunction("video", "video-transcode", "格式转码", "🔄", "TranscodeView", 2);
    }
}

``````

### `FILE: Module.VideoTools/ViewModels/TranscodeViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.VideoTools.ViewModels;

/// <summary>
/// 格式转码 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class TranscodeViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "格式转码";
    [ObservableProperty] private string _description = "视频格式转换与压缩";
    [ObservableProperty] private string _primaryButtonText = "开始转码";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public TranscodeViewModel()
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

### `FILE: Module.VideoTools/ViewModels/VideoCutViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.VideoTools.ViewModels;

/// <summary>
/// 视频剪辑 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class VideoCutViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "视频剪辑";
    [ObservableProperty] private string _description = "快速裁剪视频片段";
    [ObservableProperty] private string _primaryButtonText = "添加视频";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public VideoCutViewModel()
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

### `FILE: Module.VideoTools/ViewModels/VideoListViewModel.cs`

``````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Module.VideoTools.ViewModels;

/// <summary>
/// 视频库 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class VideoListViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "视频库";
    [ObservableProperty] private string _description = "本地视频文件浏览与预览";
    [ObservableProperty] private string _primaryButtonText = "扫描视频";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public VideoListViewModel()
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

### `FILE: Module.VideoTools/Views/TranscodeView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.VideoTools.Views.TranscodeView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="🔄" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="格式转码" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="ffmpeg 批量转码（H264/H265/音频提取/压缩）" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="▶ 开始转码" Margin="0,0,8,0"/><Button Classes="ghost" Content="⏹ 停止" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="8"><TextBox PlaceholderText="输入文件/目录（可多选）" Height="60" AcceptsReturn="True" TextWrapping="Wrap"/><ComboBox SelectedIndex="0" HorizontalAlignment="Left" Width="260"><ComboBoxItem Content="H.264 MP4（通用）"/><ComboBoxItem Content="H.265 MP4（更小）"/><ComboBoxItem Content="仅提取音频 MP3"/><ComboBoxItem Content="压缩到指定大小"/></ComboBox></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.VideoTools/Views/TranscodeView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.VideoTools.Views;
public partial class TranscodeView : UserControl
{
    public TranscodeView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.VideoTools/Views/VideoCutView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.VideoTools.Views.VideoCutView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="✂" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="视频剪辑" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="无损剪切（关键帧切片）、抽帧预览" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="ghost" Content="🎞 抽帧预览" Margin="0,0,8,0"/><Button Classes="primary" Content="✂ 开始剪切" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="8"><TextBox PlaceholderText="源视频文件路径"/><Grid ColumnDefinitions="*,*,*"><TextBox PlaceholderText="起始时间 (HH:MM:SS.fff)"/><TextBox Grid.Column="1" PlaceholderText="结束时间"/><TextBox Grid.Column="2" PlaceholderText="输出文件"/></Grid></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.VideoTools/Views/VideoCutView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.VideoTools.Views;
public partial class VideoCutView : UserControl
{
    public VideoCutView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

### `FILE: Module.VideoTools/Views/VideoListView.axaml`

``````xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.VideoTools.Views.VideoListView">
  <Grid RowDefinitions="Auto,*">
    <!-- 页头 -->
    <Grid Grid.Row="0" Height="64" Background="{DynamicResource ContentHeaderBg}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="24,0" Spacing="12">
        <TextBlock Text="📁" FontSize="24" VerticalAlignment="Center"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="视频库" FontSize="18" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="扫描本地视频文件夹，自动识别封面/时长/标签" FontSize="12" Foreground="{DynamicResource Text2}"/>
        </StackPanel>
      </StackPanel>
      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,24,0">
        <Button Classes="primary" Content="📂 选择文件夹" Margin="0,0,8,0"/><Button Classes="ghost" Content="🔄 重新扫描" Margin="0,0,8,0"/>
      </StackPanel>
    </Grid>
    <Border Grid.Row="1" Margin="24,16">
      <Border Classes="card">
        <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel Margin="4"><StackPanel Spacing="8"><TextBox PlaceholderText="视频文件夹路径，如 D:\Videos"/><TextBlock Text="（功能：扫描目录 → 调用 ffprobe 取元信息 → 支持缩略图预览 / 标签筛选 / 搜索）" Foreground="{DynamicResource Text2}"/></StackPanel></StackPanel></ScrollViewer>
      </Border>
    </Border>
  </Grid>
</UserControl>

``````

### `FILE: Module.VideoTools/Views/VideoListView.axaml.cs`

``````csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.VideoTools.Views;
public partial class VideoListView : UserControl
{
    public VideoListView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

``````

---

## 3. 导出统计

| 项 | 值 |
|---|---|
| 文件数 | 11 |
| 模块 | Module.VideoTools |

