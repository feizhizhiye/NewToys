using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets;

/// <summary>
/// 媒体资产模块（需求 §6.6）：把本地视频 / 音乐 / 文档 / 书籍 / 图片等文件索引进 SQLite，
/// 并在库里完成标签、分类、集合、关联与重命名 / 移动 / 复制 / 删除管理的**通用文件资产库**。
/// <para>
/// 数据落本模块自有库 <c>store\assets\assets.db</c>（13 张表），⛔ 与其它模块共库；
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
        // ===== 数据层：本模块自有库 store\assets\assets.db（见 AssetsDb，⛔ 不与其它模块共库）=====
        // 库实例单例；AssetDatabase 负责建表 / 补列（经 ApplySchema 做版本记账与升级前快照）。
        services.AddSingleton<Services.AssetsDb>();
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
        // 音乐标签（ID3 / FLAC / MP4）读写 + 联网补全（MusicBrainz / Cover Art Archive）
        services.AddSingleton<Services.MusicTagService>();
        // 资产列表 JSON 导入 / 导出 + 用户数据（标签/分类/集合/关联）导出 / 恢复
        services.AddSingleton<Services.AssetImportExport>();
        services.AddSingleton<Services.UserDataExport>();
        // 多库融合（ATTACH 只读合并 + VACUUM INTO 另存）
        services.AddSingleton<Services.AssetMergeService>();
        // 资产浏览（左树 + 双视图）：目录聚合 + 缩略图 LRU 缓存 + 入库动作
        services.AddSingleton<Services.AssetBrowseService>();
        services.AddSingleton<Services.AssetThumbCache>();
        services.AddSingleton<Services.AssetLibraryAddService>();

        // 本模块自有库的 schema 贡献者（DDL 仍在 AssetDatabase 构造里，贡献者只负责「库打开时就建表」）
        services.AddSingleton<ILibrarySchemaContributor, Services.MediaAssetsSchemaContributor>();

        // ===== 页面 View / VM（骨架：全是空页面；页面实现 ~022 依次落地） =====
        services.AddTransient<Views.AssetAllView>();        services.AddTransient<ViewModels.AssetAllViewModel>();
        services.AddTransient<Views.AssetBrowseView>();     services.AddTransient<ViewModels.AssetBrowseViewModel>();
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
        // 资产浏览（需求 §6.6.1）：按**磁盘位置**翻 —— 左目录树 → 右「图标卡 / 详细信息」双视图 → 双击打开。
        // 与「全部资产」分工：那边按元数据找（搜索 / 标签 / 筛选），这里按文件夹翻。
        menu.RegisterFunction<Views.AssetBrowseView>(GroupKey, "assets-browse", "资产浏览", "📁", 1,
            "资源管理器形态：目录树 + 图标卡 / 详细信息双视图；双击用默认程序打开；缩略图按目录生成");
        menu.RegisterFunction<Views.AssetTagsView>(GroupKey, "assets-tags", "标签管理", "🏷", 6, "标签：名称 / 命名空间 / 颜色 / 使用数；支持合并、清理未使用与一键种入推荐标签");
        menu.RegisterFunction<Views.AssetCategoriesView>(GroupKey, "assets-categories", "分类管理", "🗃", 7, "分类：名称 / 路径 / 成员数 / 排序；层级靠路径列 + 缩进，改父级在详情卡");
        menu.RegisterFunction<Views.AssetCollectionsView>(GroupKey, "assets-collections", "集合管理", "📦", 8, "手动 / 智能集合；成员与顺序在详情卡（⬆⬇ 写 sort_order）");
        // 「存储源」页已撤下菜单：目录级的库操作（加入 / 移出 / 重扫 / 扫描全部）全部在「资产浏览」页。
        // View / VM 暂留（数据层的 storages 仍是索引的根），不再注册菜单项。
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
        var queue = services.GetRequiredService<Services.TaskQueue>();

        // 🔴 扫描**不读元数据、不生成缩略图**（同步起 ffprobe/ffmpeg 会把扫描拖成几分钟）。
        // 元数据按设置里的开关在后台任务队列补；缩略图一律「浏览页滚动到才生成」。
        scan.OnNewLocations = list =>
        {
            if (!io.Settings.AutoExtractMetadata || list.Count == 0) return;
            queue.Enqueue($"补元数据（{list.Count} 个）", async (ct, p) =>
            {
                var done = 0;
                foreach (var loc in list)
                {
                    ct.ThrowIfCancellationRequested();
                    try { await meta.ApplyAsync(loc, ct).ConfigureAwait(false); }
                    catch { /* 单个文件失败：跳过，不影响队列 */ }
                    if (++done % 20 == 0) p.Report($"已补 {done}/{list.Count}");
                }
            });
        };

        // 首次建库：种入推荐标签（只种一次；用户之后把标签清空，⛔ 不再自动种回 —— 要补点标签页「🌱 推荐标签」）
        if (!io.Settings.SeededRecommendedTags)
        {
            var tags = services.GetRequiredService<Services.TagService>();
            if (tags.IsAvailable && tags.List().Count == 0)
            {
                var seeded = tags.SeedRecommended();
                if (seeded > 0)
                    System.Diagnostics.Debug.WriteLine($"[NewToys] 已种入推荐标签 {seeded} 个。");
            }
            io.Settings.SeededRecommendedTags = true;
            io.Save();
        }

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
