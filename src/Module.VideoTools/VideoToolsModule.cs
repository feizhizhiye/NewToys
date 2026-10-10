using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.ViewModels;
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
        menu.RegisterFunction("video", "video-list",      "视频库",   "📁", "VideoListView", 0, "本地视频管理与预览");
        menu.RegisterFunction("video", "video-cut",       "视频剪辑", "✂",  "VideoCutView",  1, "裁剪片段与拼接");
        menu.RegisterFunction("video", "video-transcode", "格式转码", "🔄", "TranscodeView", 2, "转码为常用视频格式");
    }
}
