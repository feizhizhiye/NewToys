using Microsoft.Extensions.DependencyInjection;
using NewToys.Reading.Services;
using NewToys.Reading.ViewModels;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Reading;

/// <summary>
/// **网络阅读**模块（菜单组键 <c>reading</c>）：桌面 legado —— 导入书源、搜索 / 发现、书架、
/// 阅读器（索引书渲染 / 目录 / 书签 / 搜索 / 排版 / 听书）、本地 TXT 导入。
/// <para>
/// 依赖只有 Shared / Core / UI：⛔ 不引 Shell，⛔ 不引别的模块（阅读器壳住在本模块
/// <c>Controls/ReaderShell</c>，查看器 / 其它模块要用阅读能力走各自的实现）。
/// </para>
/// <para>库 = <c>store\reading\reading.db</c>；设置 = <c>config\reading.json</c>（设置页与阅读页共同写入）。</para>
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
        services.AddSingleton<ProgressStore>();
        services.AddSingleton<ContentCacheStore>();
        services.AddSingleton<SourceImportService>();
        services.AddSingleton<ReadingSettingsService>();
        services.AddSingleton<ReaderStyleService>();
        services.AddSingleton<CrawlService>();
        services.AddSingleton<CoverLoader>();
        services.AddSingleton<ReadingSession>();
        services.AddSingleton<PrefetchService>();
        services.AddSingleton<BookDownloadService>();
        services.AddSingleton<SourceDebugService>();
        services.AddSingleton<SourceCheckService>();
        services.AddSingleton<ShelfUpdateService>();
        services.AddSingleton<TtsService>();
        services.AddSingleton<BookIndexWriter>();
        services.AddSingleton<BookmarksStore>();
        services.AddSingleton<TxtBookService>();

        services.AddTransient<Views.SourceListView>();
        services.AddTransient<SourceListViewModel>();

        services.AddTransient<Views.ExploreView>();
        services.AddTransient<ExploreViewModel>();

        services.AddTransient<Views.SearchView>();
        services.AddTransient<SearchViewModel>();

        services.AddTransient<Views.ReaderView>();
        services.AddTransient<ReaderViewModel>();

        services.AddTransient<Views.ShelfView>();
        services.AddTransient<ShelfViewModel>();

        services.AddTransient<Views.SettingsReadingView>();
        services.AddTransient<SettingsReadingViewModel>();
    }

    public void Initialize(IServiceProvider services)
    {
        var menu = services.GetRequiredService<IMenuService>();

        // Order 56 = 查看器（55）之后、备份中心（60）之前；图标 IconReading 定义在 Shell/App.axaml。
        menu.RegisterModule(GroupKey, "网络阅读", "IconReading", 56);

        menu.RegisterFunction<Views.SourceListView>(GroupKey, "reading-sources", "书源", "📚", 1,
            "导入 legado 书源、按名称搜索、启用停用与导出；每条源标注「可用 / 需 WebView / 不完整」");

        menu.RegisterFunction<Views.ExploreView>(GroupKey, "reading-explore", "发现", "🧭", 2,
            "按书源自带的分类表浏览书单；点一本书弹书籍卡，卡里可直接开读或加入书架");

        menu.RegisterFunction<Views.SearchView>(GroupKey, "reading-search", "网上搜索", "🔍", 3,
            "勾几个可用书源搜书名 → 点结果弹书籍卡 → 进阅读页看目录与正文");

        menu.RegisterFunction<Views.ShelfView>(GroupKey, "reading-shelf", "书架", "🔖", 4,
            "搜到后加进书架的书；双击接着读，可分组");

        menu.RegisterFunction<Views.ReaderView>(GroupKey, "reading-read", "阅读", "📖", 5,
            "桌面 legado 阅读器：正文就地渲染，目录 / 书签 / 全书搜索 / 排版 / 听书；可导入本地 TXT，进度自动记忆");

        menu.RegisterFunction<Views.SettingsReadingView>(GroupKey, "settings-reading", "网络阅读设置", "⚙", 100,
            "抓取：并发 / 超时 / UA / 正文缓存；阅读器：字号 / 行距 / 字体 / 宽度 / 配色 / 进度；听书：语速 / 音量 / 音色");
    }
}
