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
        // v3.8：原「格式转码」页改名改造为「批量处理」页（列表 = 剪辑 + 转码混排的统一队列）
        services.AddTransient<Views.VideoBatchView>();  services.AddTransient<VideoBatchViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("video", "视频工具", "IconVideo", 40);
        // 「视频库」→「视频列表」（v1.35）：本页不是"库"（没有默认目录、不做登记），
        // 只是一个"比资源管理器显示更多信息"的查看器，名字照实改。
        menu.RegisterFunction("video", "video-list",  "视频列表", "📁", "VideoListView",  0, "本地视频查看器：分辨率 / 编码 / 码率 / 时长，可多选发送到批量处理");
        // v3.12：「命令生成」→「视频预览」（key 与 View/VM 类名不变，仍是 video-cut / VideoCutView）。
// 改名理由：这一页的独有价值是**边看画面边定参数**，详情卡与批量页完全一致；叫「命令生成」名不副实。
        menu.RegisterFunction("video", "video-cut",   "视频预览", "✂",  "VideoCutView",   1, "边预览画面边制定单个剪辑 / 转码任务，生成 ffmpeg 命令后发送到批量处理");
        // v3.8：原「格式转码」→「批量处理」（剪辑 + 转码混排的任务队列）
        menu.RegisterFunction("video", "video-batch", "批量处理", "🧺", "VideoBatchView", 2, "剪辑 + 转码混排的任务队列：逐条改参数 / 编辑命令、批量运行");
    }
}
