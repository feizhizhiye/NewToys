using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets;

/// <summary>
/// 媒体资产模块（需求 §6.6）：把本地视频 / 音乐 / 文档 / 书籍 / 图片等文件索引进 SQLite，
/// 并在库里完成标签、分类、集合、关联与重命名 / 移动 / 复制 / 删除管理的**通用文件资产库**。
/// <para>
/// 与「视频工具」共库（<c>data\newtoys.db</c>）但**表级隔离、互不引用**（对方是 ffmpeg 命令 + 批量执行）；
/// 与「设置 → 数据管理」的区别：那边管配置文件，这边管**用户文件本体**（只建索引 + 显式物理操作）。
/// </para>
/// <para>
/// ⚠️ 本模块的设置页 <c>settings-media-assets</c> 由本模块 <c>Initialize</c> **自注册**进**本模块分组**
/// （跨组注册由框架**延迟补登**保障，规范 §3.2）——已落地：View/VM 均在本模块（⛔ 不依赖框架程序集）。
/// </para>
/// </summary>
public class MediaAssetsModule : IModule
{
    /// <summary>一级分组 key（同时是菜单一级的标识与左树分组键）。</summary>
    private const string GroupKey = "assets";

    public void RegisterServices(IServiceCollection services)
    {
        // ===== 数据层：13 张表落同一个 data\newtoys.db（与视频工具共库、表级隔离） =====
        // AssetDatabase 负责建表 / 补列；三个服务都是**单例**（库是全局资源，任务队列要跨页共享）。
        services.AddSingleton<Services.AssetDatabase>();
        services.AddSingleton<Services.AssetService>();
        services.AddSingleton<Services.ScanService>();
        services.AddSingleton<Services.TaskQueue>();
        // 逻辑操作 / 标签 / 分类 / 集合 / 关联 / 操作日志 / 跨页投递（全部模块内单例，页面共享一份）
        services.AddSingleton<Services.TagService>();
        services.AddSingleton<Services.CategoryService>();
        services.AddSingleton<Services.CollectionService>();
        services.AddSingleton<Services.RelationService>();
        services.AddSingleton<Services.OperationLogService>();
        services.AddSingleton<Services.AssetNavigationStore>();
        // 物理操作（唯一动磁盘的地方）+ 任务队列已在上面注册
        services.AddSingleton<Services.FileOperationService>();
        // 存储源（在线探测 + 统计编排）
        services.AddSingleton<Services.StorageService>();
        // 模块设置（config\media-assets.json）+ 元数据 / 缩略图（ffprobe / ffmpeg 最小封装）
        services.AddSingleton<Services.MediaAssetsSettingsIO>();
        services.AddSingleton<Services.MetadataService>();
        services.AddSingleton<Services.ThumbnailService>();
        // 资产列表 JSON 导入 / 导出 + 用户数据（标签/分类/集合/关联）导出 / 恢复
        services.AddSingleton<Services.AssetImportExport>();
        services.AddSingleton<Services.UserDataExport>();
        // 多库融合（ATTACH 只读合并 + VACUUM INTO 另存）
        services.AddSingleton<Services.AssetMergeService>();

        // 本模块在总库里的 schema 贡献者（DDL 仍在 AssetDatabase 构造里，贡献者只负责「库打开时就建表」）
        services.AddSingleton<ILibrarySchemaContributor, Services.MediaAssetsSchemaContributor>();

        // ===== 页面 View / VM（骨架：全是空页面；页面实现 ~022 依次落地） =====
        services.AddTransient<Views.AssetAllView>();        services.AddTransient<ViewModels.AssetAllViewModel>();
        services.AddTransient<Views.AssetTagsView>();       services.AddTransient<ViewModels.AssetTagsViewModel>();
        services.AddTransient<Views.AssetCategoriesView>(); services.AddTransient<ViewModels.AssetCategoriesViewModel>();
        services.AddTransient<Views.AssetCollectionsView>(); services.AddTransient<ViewModels.AssetCollectionsViewModel>();
        services.AddTransient<Views.AssetStoragesView>();   services.AddTransient<ViewModels.AssetStoragesViewModel>();
        services.AddTransient<Views.AssetTasksView>();      services.AddTransient<ViewModels.AssetTasksViewModel>();
        services.AddTransient<Views.AssetTrashView>();      services.AddTransient<ViewModels.AssetTrashViewModel>();

        // 模块设置页（View/VM 归本模块，自注册进**本模块分组** —— 见 Initialize）
        services.AddTransient<Views.SettingsMediaAssetsView>();
        services.AddTransient<ViewModels.SettingsMediaAssetsViewModel>();
    }

    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();

        // 一级分组：Order 50（视频工具 40 之后、系统设置 999 之前）。
        // 一级图标已矢量化（IconMediaAssets）——与其他一级分组同为 24 网格描边 Path；
        //    左树仍保留「矢量键取不到 → 回退 emoji 文本」机制作兜底。
        menu.RegisterModule(GroupKey, "媒体资产", "IconMediaAssets", 50);

        // 二级项：Order 0~11，与需求 §6.6 的菜单表逐条对应；二级用 emoji 占位（功能清单页 / 首页收藏卡要用它）。
        // 🔴 v3.69：5 个类型二级项（assets-video / music / book / image / document）**已撤销**
        //    （用户 2026-10-16 拍板，菜单 Key 铁律的一次例外）—— 类型切换改由「全部资产」页**筛选栏的类型控件**承担，
        //    列集合 / 列表数据 / 详情卡字段集 / 状态栏前缀随之切换（需求 §6.6「类型切换」）。
        menu.RegisterFunction<Views.AssetAllView>(GroupKey, "assets-all", "全部资产", "🗂", 0, "混合视图：文件名 / 类型 / 标签 / 分类 / 集合 / 大小 / 修改时间 / 存储源");
        menu.RegisterFunction<Views.AssetTagsView>(GroupKey, "assets-tags", "标签管理", "🏷", 6, "标签：名称 / 命名空间 / 颜色 / 使用数；支持合并与清理未使用");
        menu.RegisterFunction<Views.AssetCategoriesView>(GroupKey, "assets-categories", "分类管理", "🗃", 7, "分类树 + 成员列表（本模块唯一的非列表页）");
        menu.RegisterFunction<Views.AssetCollectionsView>(GroupKey, "assets-collections", "集合管理", "📦", 8, "手动 / 智能集合；成员可拖拽排序");
        menu.RegisterFunction<Views.AssetStoragesView>(GroupKey, "assets-storages", "存储源", "💾", 9, "local / removable / nas；在线状态；移除只移索引、不删磁盘文件");
        menu.RegisterFunction<Views.AssetTasksView>(GroupKey, "assets-tasks", "任务中心", "⏳", 10, "扫描 / 重命名 / 移动 / 复制 / 删除 / 元数据 / 缩略图任务：取消 / 重试 / 日志");
        menu.RegisterFunction<Views.AssetTrashView>(GroupKey, "assets-trash", "回收站", "🗑", 11, "已软删除资产：恢复 / 永久删除（二次确认）/ 清空");

        // 模块设置页的 View / VM **归本模块**（需求 §6.6「模块设置页归本模块」）。
        // 菜单项**挂回本模块的一级菜单**（⛔ 不再进「设置」组 —— 该一级菜单项已删除）：
        //     全应用口径 = 应用级设置在**一页**（Shell 的设置页），模块设置在各模块**一级菜单下**。
        // Key `settings-media-assets` ⛔不改（首页跳转 / 收藏白名单按 Key 寻址）；Order 99 = 本组末位。
        menu.RegisterFunction<Views.SettingsMediaAssetsView>(GroupKey, "settings-media-assets", "媒体资产设置", "🎬", 99,
            "扫描忽略规则 / 缩略图 / 命名模板 / 回收站 / 物理操作 / 元数据提取");

        // ===== 把模块设置接到扫描器（忽略规则 / 采样大小）+ 元数据 / 缩略图增强回调 =====
        // 🔴 （启动提速）：这段接线要读 config\media-assets.json 并可能扫库入队，
        // 已整体搬到 InitializeDeferred（首帧之后）。上面的菜单注册全部保留在首帧前。
    }

    /// <summary>
    /// （启动提速）：模块设置读取 + 扫描器接线 + 回收站到期清理入队 —— 首帧之后执行。
    /// <para>
    /// ⚠️ 顺序不变：<c>ApplySettings</c> 仍要在用户第一次点「扫描」之前生效。
    /// 延迟层排在首帧渲染之后，而窗口可交互也在首帧之后 —— 实际不会漏。
    /// </para>
    /// </summary>
    public void InitializeDeferred(IServiceProvider services)
    {
        // ===== 把模块设置接到扫描器（忽略规则 / 采样大小）+ 元数据 / 缩略图增强回调 =====
        var io = services.GetRequiredService<Services.MediaAssetsSettingsIO>();
        var scan = services.GetRequiredService<Services.ScanService>();
        scan.ApplySettings(io.Settings);

        var meta = services.GetRequiredService<Services.MetadataService>();
        var thumbs = services.GetRequiredService<Services.ThumbnailService>();
        scan.Enricher = async (loc, ct) =>
        {
            var s = io.Settings;
            if (s.AutoExtractMetadata) await meta.ApplyAsync(loc, ct).ConfigureAwait(false);
            if (s.AutoGenerateThumbnails) await thumbs.GenerateAsync(loc, ct).ConfigureAwait(false);
        };

        // 回收站「到期自动清理」默认关；开了才在启动时把过期资产排进永久删除队列（任务中心可见、可取消）
        if (io.Settings.AutoCleanupTrash)
        {
            var fileOps = services.GetRequiredService<Services.FileOperationService>();
            var n = fileOps.EnqueueExpiredTrashPurge(io.Settings.TrashRetentionDays);
            if (n > 0)
                System.Diagnostics.Debug.WriteLine($"[NewToys] 回收站到期清理：启动时入队 {n} 个资产。");
        }
    }
}
