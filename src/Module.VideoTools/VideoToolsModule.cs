using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.ViewModels;
namespace NewToys.VideoTools;
public class VideoToolsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // 媒体信息缓存（SQLite，库文件在 <应用根>\data\newtoys.db；SqliteDatabase 由 Shell 注册）
        services.AddSingleton<Services.VideoMediaIndex>();

        services.AddTransient<Views.VideoListView>();   services.AddTransient<VideoListViewModel>();
        services.AddTransient<Views.VideoCutView>();    services.AddTransient<VideoCutViewModel>();
        services.AddTransient<Views.TranscodeView>();   services.AddTransient<TranscodeViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("video", "视频工具", "🎬", 40);
        menu.RegisterFunction("video", "video-list",      "视频库",   "📁", "VideoListView", 0, "本地视频查看器：分辨率 / 编码 / 码率 / 时长，判断是否剪辑或转码");
        menu.RegisterFunction("video", "video-cut",       "视频剪辑", "✂",  "VideoCutView",  1, "裁剪片段与拼接");
        menu.RegisterFunction("video", "video-transcode", "格式转码", "🔄", "TranscodeView", 2, "转码为常用视频格式");
    }
}
