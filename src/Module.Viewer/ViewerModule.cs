using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Viewer.Services;
using NewToys.Viewer.ViewModels;

namespace NewToys.Viewer;

/// <summary>
/// **查看器**模块（菜单组键 <c>viewer</c>，Order 55）：为本软件提供基础的「打开并显示」能力。
/// <para>
/// 🔴 本模块只是**薄壳**：渲染器（文本 / 图片 / 将来音视频）全部在 <c>NewToys.UI/Viewers/</c>，
/// 媒体资产 / 视频工具将来要「用查看器打开」时直接复用那套控件，⛔ 不必引用本模块
/// （模块禁互引，§3.1）。
/// </para>
/// <para>
/// 🔴 依赖只有 Shared / Core / UI：⛔ 不引 Shell、⛔ 不引其它模块。设置落 <c>config\viewer.json</c>
/// （经 Core 的 <c>IOptionsStore</c>），本模块只读、唯一写入点 = <see cref="ViewerSettingsService"/>。
/// </para>
/// </summary>
public class ViewerModule : IModule
{
    /// <summary>菜单组键（页 Key 前缀 <c>viewer-</c>）。</summary>
    private const string GroupKey = "viewer";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ViewerSettingsService>();
        services.AddSingleton<TxtBookService>();
        services.AddSingleton<ArchiveImageService>();
        services.AddSingleton<AudioLibraryService>();

        // 播放后端是跨平台契约的实现（Windows = NAudio）；单例 = 同一时间只播一首
        services.AddSingleton<Shared.Services.IAudioPlayback, NAudioPlayback>();

        services.AddTransient<Views.ViewerView>();
        services.AddTransient<ViewerViewModel>();
    }

    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();

        // Order 55 = 媒体资产（50）之后、备份中心（60）之前；图标 IconViewer 定义在 Shell/App.axaml。
        menu.RegisterModule(GroupKey, "查看器", "IconViewer", 55);

        menu.RegisterFunction<Views.ViewerView>(GroupKey, "viewer-main", "查看器", "📄", 1,
            "打开文本（txt / md 带目录、书签、全书搜索）、源码、图片、压缩包内图片（zip / rar / 7z）、音频播放；视频走系统播放器");
    }
}
