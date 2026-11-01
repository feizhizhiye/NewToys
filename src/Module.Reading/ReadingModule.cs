using Microsoft.Extensions.DependencyInjection;
using NewToys.Reading.Services;
using NewToys.Reading.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Reading;

/// <summary>
/// **阅读**模块（菜单组键 <c>reading</c>）：把 legado 书源跑起来 —— 导入书源、搜索、看目录与正文。
/// <para>
/// 依赖只有 Shared / Core / UI：⛔ 不引 Shell，⛔ 不引别的模块（正文渲染复用
/// <c>NewToys.UI/Viewers/TextViewer</c>，<c>⛔</c> 不引用 <c>Module.Viewer</c>）。
/// </para>
/// <para>库 = <c>store\reading\reading.db</c>；设置 = <c>config\reading.json</c>（本模块设置页是唯一写入点）。</para>
/// </summary>
public class ReadingModule : IModule
{
    /// <summary>菜单组键（页 Key 前缀 <c>reading-</c>）。</summary>
    private const string GroupKey = "reading";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ReadingDb>();
        services.AddSingleton<ReadingDatabase>();
        services.AddSingleton<ILibrarySchemaContributor, ReadingSchemaContributor>();

        services.AddSingleton<SourceStore>();
        services.AddSingleton<ShelfStore>();
        services.AddSingleton<SourceImportService>();
        services.AddSingleton<ReadingSettingsService>();
        services.AddSingleton<CrawlService>();
        services.AddSingleton<ReadingSession>();

        services.AddTransient<Views.SourceListView>();
        services.AddTransient<SourceListViewModel>();

        services.AddTransient<Views.ExploreView>();
        services.AddTransient<ExploreViewModel>();

        services.AddTransient<Views.SearchView>();
        services.AddTransient<SearchViewModel>();

        services.AddTransient<Views.ShelfView>();
        services.AddTransient<ShelfViewModel>();
    }

    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();

        // Order 56 = 查看器（55）之后、备份中心（60）之前；图标 IconReading 定义在 Shell/App.axaml。
        menu.RegisterModule(GroupKey, "阅读", "IconReading", 56);

        menu.RegisterFunction<Views.SourceListView>(GroupKey, "reading-sources", "书源", "📚", 1,
            "导入 legado 书源、按名称搜索、启用停用与导出；每条源标注「可用 / 需 JS / 不完整」");

        menu.RegisterFunction<Views.ExploreView>(GroupKey, "reading-explore", "发现", "🧭", 2,
            "按书源自带的分类表浏览书单，不用输关键词；点一本直接读");

        menu.RegisterFunction<Views.SearchView>(GroupKey, "reading-search", "网上搜索", "🔍", 3,
            "选几个可用的书源搜书名 → 点开看目录 → 点章节看正文");

        menu.RegisterFunction<Views.ShelfView>(GroupKey, "reading-shelf", "书架", "🔖", 4,
            "搜到后加进书架的书；双击直接接着读");
    }
}
