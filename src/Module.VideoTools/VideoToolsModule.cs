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

        // 剪切 / 转码的**任务清单**（单例）：页面 VM 是瞬态的，任务挂在 VM 上切一次页就没了。
        // 落 config\video-tasks.json（会被「设置 → 数据管理」一起备份）。
        services.AddSingleton<Services.VideoTaskStore>();

        services.AddTransient<Views.VideoListView>();   services.AddTransient<VideoListViewModel>();
        services.AddTransient<Views.VideoCutView>();    services.AddTransient<VideoCutViewModel>();
        services.AddTransient<Views.TranscodeView>();   services.AddTransient<TranscodeViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("video", "视频工具", "🎬", 40);
        // 「视频库」→「视频列表」（v1.35）：本页不是"库"（没有默认目录、不做登记），
        // 只是一个"比资源管理器显示更多信息"的查看器，名字照实改。
        menu.RegisterFunction("video", "video-list",      "视频列表", "📁", "VideoListView", 0, "本地视频查看器：分辨率 / 编码 / 码率 / 时长，可多选发送到转码 / 剪切");
        menu.RegisterFunction("video", "video-cut",       "视频剪辑", "✂",  "VideoCutView",  1, "关键帧预览定区间，任务可暂存后批量剪切");
        menu.RegisterFunction("video", "video-transcode", "格式转码", "🔄", "TranscodeView", 2, "批量生成转码任务、单独改参数、前后对比后批量运行");
    }
}
