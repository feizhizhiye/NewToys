using System;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Viewer.Services;

namespace NewToys.Viewer;

/// <summary>查看器模块（菜单组键 viewer，Order 55）：为本软件提供基础的「打开并显示」能力。（WHY: VW-019）</summary>
public class ViewerModule : IModule
{
    /// <summary>菜单组键（页 Key 前缀 <c>viewer-</c>）。</summary>
    private const string GroupKey = "viewer";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ViewerSettingsService>();
        services.AddSingleton<ArchiveImageService>();
        services.AddSingleton<AudioLibraryService>();

        // 播放后端是跨平台契约的实现：Windows = NAudio（平台底座），其它平台先用明确失败的空实现
        if (OperatingSystem.IsWindows())
            services.AddSingleton<Shared.Services.IAudioPlayback, NAudioPlayback>();
        else
            services.AddSingleton<Shared.Services.IAudioPlayback, NullAudioPlayback>();

        services.AddTransient<Views.ViewerTextView>();
        services.AddTransient<Views.ViewerImageView>();
        services.AddTransient<Views.ViewerAudioView>();
    }

    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();

        // Order 55 = 媒体资产（50）之后、备份中心（60）之前；图标 IconViewer 定义在 Shell/App.axaml。
        menu.RegisterModule(GroupKey, "查看器", "IconViewer", 55);

        // 一页只处理一类：类型不符的文件由 VM 转去对应页并切过去（含拖入 / 选择器 / 最近打开）。
        menu.RegisterFunction<Views.ViewerTextView>(GroupKey, "viewer-text", "文档查看", "📄", 1,
            "打开源码 / 短文本 / HTML（去标签取纯文本）；长文本阅读（txt / md）在「网络阅读」；本地 PDF / EPUB / MOBI 支持规划中");

        menu.RegisterFunction<Views.ViewerImageView>(GroupKey, "viewer-image", "图片查看", "🖼", 2,
            "打开图片与压缩包（zip / rar / 7z）内的图片：缩放、适应窗口、逐张翻页");

        menu.RegisterFunction<Views.ViewerAudioView>(GroupKey, "viewer-audio", "音乐播放", "🎵", 3,
            "本地音乐播放：播放列表（m3u / m3u8）/ 顺序 · 单曲 · 随机 / 进度与音量 / LRC 歌词（可双语）；视频走系统播放器");
    }
}
