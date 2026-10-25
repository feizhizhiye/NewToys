using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Services;
using NewToys.VideoTools.ViewModels;
namespace NewToys.VideoTools;
public class VideoToolsModule : IModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // 媒体信息缓存（SQLite，库文件在 <应用根>\data\newtoys.db；SqliteDatabase 由 Shell 注册）
        services.AddSingleton<Services.VideoMediaIndex>();

        // 本模块在总库里的 schema 贡献者（旧表清理由它按版本号做一次，建表仍由 VideoMediaIndex 构造完成）
        services.AddSingleton<ILibrarySchemaContributor, Services.VideoToolsSchemaContributor>();

        // 剪切 / 转码的**任务清单**（单例）：页面 VM 是瞬态的，任务挂在 VM 上切一次页就没了。
        // 落 config\video-tasks.json（会被「设置 → 数据管理」一起备份）。
        services.AddSingleton<Services.VideoTaskStore>();

        services.AddTransient<Views.VideoListView>();    services.AddTransient<VideoListViewModel>(s =>
            new VideoListViewModel(
                s.GetRequiredService<IAppConfigService>(),
                s.GetRequiredService<IColumnLayoutService>(),
                s.GetRequiredService<VideoMediaIndex>(),
                s.GetRequiredService<VideoTaskStore>(),
                s.GetRequiredService<INavigationService>(),
                s.GetRequiredService<IVideoToolsSettingsService>()));
        services.AddTransient<Views.VideoPreviewView>(); services.AddTransient<VideoPreviewViewModel>();
        // 原「格式转码」页改名改造为「批量处理」页（列表 = 剪辑 + 转码混排的统一队列）
        services.AddTransient<Views.VideoBatchView>();  services.AddTransient<VideoBatchViewModel>();

        // 本模块设置（并发数）——**本模块配置的唯一写入点**，落 config\video-tools.json
        // （经 Core 的 IOptionsStore）。ffmpeg / 外部播放器 / 导入导出目录是全局项，⛔ 不在这里另存一份。
        services.AddSingleton<IVideoToolsSettingsService, VideoToolsSettingsService>();
        services.AddTransient<Views.SettingsVideoToolsView>();  services.AddTransient<SettingsVideoToolsViewModel>();
    }
    public void Initialize(IServiceProvider services)
    {
        // 🔴 （启动提速）：建库/删表的 IO 已搬到 InitializeDeferred（首帧之后）。
        // 这里只留菜单注册（纯内存，必须首帧前完成）。
        var menu = services.GetRequiredService<IMenuService>();
        menu.RegisterModule("video", "视频工具", "IconVideo", 40);
        // 「视频库」→「视频列表」（v1.35）：本页不是"库"（没有默认目录、不做登记），
        // 只是一个"比资源管理器显示更多信息"的查看器，名字照实改。
        menu.RegisterFunction<Views.VideoListView>("video", "video-list", "视频列表", "📁", 0, "本地视频查看器：分辨率 / 编码 / 码率 / 时长，可多选发送到批量处理");
        // 「命令生成」→「视频预览」。View / VM 的类名与文件名一并改成 VideoPreview*；
        // ⚠️ **菜单 key 仍是 video-cut** —— 它落config\columnlayout.json 的页键（详情栏开合 / 列显隐都按它记），
        // 改名会让已有用户的偏好失效；类名 / 文件名改名则零成本。
        // 改名理由：这一页的独有价值是**边看画面边定参数**，详情卡与批量页完全一致；叫「命令生成」名不副实。
        menu.RegisterFunction<Views.VideoPreviewView>("video", "video-cut", "视频预览", "✂", 1, "边预览画面边制定单个剪辑 / 转码任务，生成 ffmpeg 命令后发送到批量处理");
        // 原「格式转码」→「批量处理」（剪辑 + 转码混排的任务队列）
        menu.RegisterFunction<Views.VideoBatchView>("video", "video-batch", "批量处理", "🧺", 2, "剪辑 + 转码混排的任务队列：逐条改参数 / 编辑命令、批量运行");

        // 模块设置页通则 —— View/VM 归本模块。
        // 菜单项**挂回本模块的一级菜单**（⛔ 不再进「设置」组 —— 该一级菜单项已删除）。
        menu.RegisterFunction<Views.SettingsVideoToolsView>("video", "settings-video-tools", "视频工具设置", "🎬", 100,
            "并发数与 ffmpeg / 播放器 / 导入导出目录（后三项是全局项，本页只展示）");
    }

    /// <summary>
    /// 旧表清理 + 建表搬到 <see cref="Services.VideoToolsSchemaContributor"/>（总库 schema 统一入口）。
    /// <para>
    /// 为什么搬：总库是多模块共享的，原来这段 DROP 在 `InitializeDeferred` 里**每次启动都跑**，
    /// 属于无护栏的破坏性语句；现在改成「缓存版本号变化才清一次」，并且排在建表之前。
    /// </para>
    /// </summary>
    public void InitializeDeferred(IServiceProvider services) { }
}
