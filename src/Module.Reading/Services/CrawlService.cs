using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>多源搜索里单个源的结果（含失败原因，界面按源显示）。</summary>
public sealed class SourceSearchResult
{
    public SourceSearchResult(BookSource source, IReadOnlyList<SearchItem> items, StepError? error)
    {
        Source = source;
        Items = items;
        Error = error;
    }

    public BookSource Source { get; }

    public IReadOnlyList<SearchItem> Items { get; }

    public StepError? Error { get; }

    public bool Ok => Error is null;

    public string Summary => Error is null ? $"{Source.Name}：{Items.Count} 条" : $"{Source.Name}：{Error.Message}";
}

/// <summary>四段抓取管线：搜索 → 详情 → 目录 → 正文。（WHY: RD-007）</summary>
public sealed class CrawlService : IDisposable
{
    /// <summary>目录最多跟多少页（防死循环）。</summary>
    public const int MaxTocPages = 20;

    /// <summary>正文最多拼多少页。</summary>
    public const int MaxContentPages = 10;

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate;
    private readonly RuleEvaluator _rules;
    private readonly ReadingSettingsService _settings;
    private readonly ContentCacheStore? _cache;

    public CrawlService(ReadingSettingsService settings, ContentCacheStore? cache = null)
    {
        _settings = settings;
        _cache = cache;
        _gate = new SemaphoreSlim(settings.Concurrency, settings.Concurrency);
        _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", settings.UserAgent);

        // JS 里的 java.ajax 复用同一套请求层（同一 HttpClient / UA / 超时 / 重试）
        _rules = new RuleEvaluator(FetchForJs);
    }

    /// <summary><c>java.ajax</c> 的同步入口：⛔ 不能直接在 UI 线程上阻塞 async（会死锁），必须扔线程池。</summary>
    private string FetchForJs(string url, string method, string body)
    {
        try
        {
            var result = Task.Run(() => FetchAsync(
                    url,
                    string.IsNullOrWhiteSpace(method) ? null : method,
                    string.IsNullOrWhiteSpace(body) ? null : body,
                    referer: null,
                    CancellationToken.None))
                .GetAwaiter().GetResult();

            return result.Error is null ? result.Body : "";
        }
        catch { return ""; }
    }

    // ==================== 搜索 ====================

    /// <summary>搜索一个源。</summary>
    public async Task<StepResult<IReadOnlyList<SearchItem>>> SearchAsync(
        BookSource source, string key, int page, CancellationToken ct)
    {
        if (BlockedReason(source, CrawlStep.Search) is { } searchBlocked)
            return StepResult<IReadOnlyList<SearchItem>>.Fail(searchBlocked);
        if (string.IsNullOrWhiteSpace(source.SearchUrl))
            return StepResult<IReadOnlyList<SearchItem>>.Fail(
                new StepError(CrawlStep.Search, "searchUrl", "", "书源没有搜索地址"));

        var (template, options) = SplitRequest(source.SearchUrl!);

        // 🔴 真实书源里 searchUrl 大量写的是**相对路径**（`/search?q=…`）⇒ 必须以 bookSourceUrl 为基准拼绝对地址
        var gbk = IsGbkText(template) || IsGbkText(options.Body);
        var vars = CreateVariables(source, key, page, gbk);

        var url = InterpolateRule(template, vars, out var needsJs);
        if (needsJs) return Fail<IReadOnlyList<SearchItem>>(CrawlStep.Search, "searchUrl", template, "搜索地址含脚本函数，当前版本不支持");

        url = ResolveUrl(source.Url, url);

        var body = options.Body is null ? null : InterpolateRule(options.Body, vars, out _);

        var fetched = await FetchAsync(url, options.Method, body, source.Url, ct).ConfigureAwait(false);
        if (fetched.Error is not null)
            return Fail<IReadOnlyList<SearchItem>>(CrawlStep.Search, "searchUrl", template, fetched.Error);

        var ctx = BuildDocumentContext(vars, fetched.Body, fetched.Url);
        return ParseItems(source, ctx, source.Search, "ruleSearch");
    }

    /// <summary>把一页列表解析成条目（搜索与发现共用）。（WHY: RD-008）</summary>
    private StepResult<IReadOnlyList<SearchItem>> ParseItems(
        BookSource source, RuleContext ctx, SearchRule? rule, string ruleOwner, SearchRule? fallback = null)
    {
        var effective = MergeRule(rule, fallback);
        var list = _rules.Evaluate(effective?.BookList, ctx);

        var items = new List<SearchItem>();
        if (list.Kind == RuleValueKind.Nodes)
            items.AddRange(list.Nodes.Select(node => ReadSearchItemFromNode(source, effective, ctx.ForElement(node))));
        else if (list.Kind == RuleValueKind.Json)
            items.AddRange(list.JsonItems.Select(item => ReadSearchItemFromJson(source, effective, ctx.ForJson(item))));

        items = items.Where(i => !string.IsNullOrWhiteSpace(i.Name) || !string.IsNullOrWhiteSpace(i.BookUrl)).ToList();

        if (items.Count == 0)
            return Fail<IReadOnlyList<SearchItem>>(CrawlStep.Search, ruleOwner + ".bookList", effective?.BookList ?? "",
                ctx.Errors.Count > 0 ? ctx.Errors[0]
                    : list.Kind == RuleValueKind.Empty ? "书单选择器未命中" : "命中条目但书名与详情链接都为空");

        return StepResult<IReadOnlyList<SearchItem>>.Success(items);
    }

    /// <summary>逐字段合并：<paramref name="primary"/> 的空字段由 <paramref name="fallback"/> 补位。</summary>
    private static SearchRule? MergeRule(SearchRule? primary, SearchRule? fallback)
    {
        if (primary is null) return fallback;
        if (fallback is null) return primary;

        static string? Pick(string? first, string? second)
            => string.IsNullOrWhiteSpace(first) ? second : first;

        return new SearchRule
        {
            BookList = Pick(primary.BookList, fallback.BookList),
            Name = Pick(primary.Name, fallback.Name),
            Author = Pick(primary.Author, fallback.Author),
            Kind = Pick(primary.Kind, fallback.Kind),
            LastChapter = Pick(primary.LastChapter, fallback.LastChapter),
            UpdateTime = Pick(primary.UpdateTime, fallback.UpdateTime),
            BookUrl = Pick(primary.BookUrl, fallback.BookUrl),
            CoverUrl = Pick(primary.CoverUrl, fallback.CoverUrl),
            Intro = Pick(primary.Intro, fallback.Intro),
            WordCount = Pick(primary.WordCount, fallback.WordCount),
        };
    }

    // ==================== 发现页 ====================

    /// <summary>解析 exploreUrl 的分类表。（WHY: RD-009）</summary>
    public IReadOnlyList<ExploreCategory> ParseExploreCategories(BookSource source)
    {
        var raw = source.ExploreUrl;
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<ExploreCategory>();

        var categories = new List<ExploreCategory>();
        foreach (var part in Regex.Split(raw!, "&&|\\r\\n|\\n|\\r"))
        {
            var text = part.Trim();
            if (text.Length == 0) continue;

            // 🔴 `@js:` 动态分类表：执行 JS 生成「分类::地址」列表，再逐项解析；失败只跳过这一段
            if (RuleSyntax.ContainsJs(text))
            {
                var (_, code) = RuleSyntax.SplitJs(text);
                if (string.IsNullOrWhiteSpace(code)) continue;

                var jsCtx = CreateVariables(source, "", 1);
                var jsResult = _rules.EvaluateJsBlock(code!, jsCtx);
                if (string.IsNullOrWhiteSpace(jsResult)) continue;

                foreach (var line in Regex.Split(jsResult!, "&&|\\r\\n|\\n|\\r"))
                {
                    var lineText = line.Trim();
                    if (lineText.Length == 0) continue;
                    if (TryParseCategory(source, lineText, categories)) continue;
                }
                continue;
            }

            TryParseCategory(source, text, categories);
        }

        return categories;
    }

    /// <summary>把一段「分类名::地址[, 请求配置]」解析成一个分类并加入列表；解析不出返回 false（⛔ 不抛）。</summary>
    private static bool TryParseCategory(BookSource source, string text, List<ExploreCategory> categories)
    {
        var sep = text.IndexOf("::", StringComparison.Ordinal);
        if (sep <= 0) return false;

        var name = text.Substring(0, sep).Trim();
        var urlText = text.Substring(sep + 2).Trim();
        if (name.Length == 0) return false;
        if (urlText.Length == 0) urlText = source.SearchUrl ?? "";   // 只写分类名 ⇒ 退回搜索地址
        if (urlText.Length == 0) return false;

        var (clean, options) = SplitRequest(urlText);
        categories.Add(new ExploreCategory(name, ResolveUrl(source.Url, clean), options.Method, options.Body));
        return true;
    }

    /// <summary>按分类抓一页书单（规则用 <c>ruleExplore</c>，字段空的由 <c>ruleSearch</c> 补位）。</summary>
    public async Task<StepResult<IReadOnlyList<SearchItem>>> ExploreAsync(
        BookSource source, ExploreCategory category, int page, CancellationToken ct)
    {
        // 发现页只看「能不能发请求」：⛔ 不用三段完整性判定（只带分类表、没有 searchUrl 的源同样能发现）
        if (!source.Enabled)
            return Fail<IReadOnlyList<SearchItem>>(CrawlStep.Search, "enabled", "", "书源已停用");
        if (source.Capability == SourceCapability.NeedsWebView)
            return Fail<IReadOnlyList<SearchItem>>(CrawlStep.Search, "source", "",
                "该源依赖 WebView，桌面端无法执行：" + source.CapabilityReason);

        var vars = CreateVariables(source, "", page, IsGbkText(category.Url));
        var url = InterpolateRule(category.Url, vars, out var needsJs);
        if (needsJs)
            return Fail<IReadOnlyList<SearchItem>>(CrawlStep.Search, "exploreUrl", category.Url, "分类地址含脚本函数，当前版本不支持");

        url = ResolveUrl(source.Url, url);
        var body = category.Body is null ? null : InterpolateRule(category.Body, vars, out _);

        var fetched = await FetchAsync(url, category.Method, body, source.Url, ct).ConfigureAwait(false);
        if (fetched.Error is not null)
            return Fail<IReadOnlyList<SearchItem>>(CrawlStep.Search, "exploreUrl", category.Url, fetched.Error);

        var ctx = BuildDocumentContext(vars, fetched.Body, fetched.Url);
        return ParseItems(source, ctx, source.Explore, "ruleExplore", source.Search);
    }

    /// <summary>多源并发搜索（并发上限来自设置，默认 3；<paramref name="progress"/> 报「已完成 / 总数」）。（WHY: RD-010）</summary>
    /// <param name="onSourceDone">
    public async Task<IReadOnlyList<SourceSearchResult>> SearchManyAsync(
        IReadOnlyList<BookSource> sources, string key, int page, CancellationToken ct,
        IProgress<(int Done, int Total)>? progress = null,
        IProgress<SourceSearchResult>? onSourceDone = null)
    {
        if (sources.Count == 0) return Array.Empty<SourceSearchResult>();

        var results = new SourceSearchResult[sources.Count];
        var finished = 0;
        var total = sources.Count;

        // 🔴 每个源整体扔线程池：规则求值（含 @js: 的 Jint 执行）是**同步**的，
        // 直接在 UI 线程上起跑会把界面一起冻住（进度条不动、取消按钮点不动）。
        var tasks = sources.Select((source, index) => Task.Run(async () =>
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            SourceSearchResult outcome;
            try
            {
                var result = await SearchAsync(source, key, page, ct).ConfigureAwait(false);
                outcome = new SourceSearchResult(source, result.Value ?? Array.Empty<SearchItem>(), result.Error);
            }
            catch (OperationCanceledException)
            {
                outcome = new SourceSearchResult(source, Array.Empty<SearchItem>(),
                    new StepError(CrawlStep.Search, "searchUrl", "", "已取消"));
            }
            catch (Exception ex)
            {
                outcome = new SourceSearchResult(source, Array.Empty<SearchItem>(),
                    new StepError(CrawlStep.Search, "searchUrl", "", "抓取异常：" + ex.Message));
            }
            finally
            {
                _gate.Release();
            }

            results[index] = outcome;
            onSourceDone?.Report(outcome);
            progress?.Report((Interlocked.Increment(ref finished), total));
        })).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(r => r is not null).ToList();
    }

    private SearchItem ReadSearchItemFromNode(BookSource source, SearchRule? rule, RuleContext ctx)
    {
        var node = ctx.Root;
        var bookUrl = _rules.EvaluateText(rule?.BookUrl, ctx);
        if (string.IsNullOrWhiteSpace(bookUrl) && node is not null) bookUrl = SelfLink(node);

        return new SearchItem
        {
            Name = _rules.EvaluateText(rule?.Name, ctx),
            Author = _rules.EvaluateText(rule?.Author, ctx),
            CoverUrl = ctx.AbsoluteUrl(_rules.EvaluateText(rule?.CoverUrl, ctx)),
            BookUrl = ctx.AbsoluteUrl(bookUrl),
            Intro = _rules.EvaluateText(rule?.Intro, ctx),
            Kind = _rules.EvaluateText(rule?.Kind, ctx),
            LastChapter = _rules.EvaluateText(rule?.LastChapter, ctx),
            SourceName = source.Name,
            SourceUrl = source.Url,
        };
    }

    private SearchItem ReadSearchItemFromJson(BookSource source, SearchRule? rule, RuleContext ctx)
    {
        return new SearchItem
        {
            Name = _rules.EvaluateText(rule?.Name, ctx),
            Author = _rules.EvaluateText(rule?.Author, ctx),
            CoverUrl = ctx.AbsoluteUrl(_rules.EvaluateText(rule?.CoverUrl, ctx)),
            BookUrl = ctx.AbsoluteUrl(_rules.EvaluateText(rule?.BookUrl, ctx)),
            Intro = _rules.EvaluateText(rule?.Intro, ctx),
            Kind = _rules.EvaluateText(rule?.Kind, ctx),
            LastChapter = _rules.EvaluateText(rule?.LastChapter, ctx),
            SourceName = source.Name,
            SourceUrl = source.Url,
        };
    }

    // ==================== 详情 ====================

    /// <summary>读详情页（书名 / 作者 / 封面 / 简介 / 目录地址）。</summary>
    public async Task<StepResult<BookDetail>> DetailAsync(BookSource source, string bookUrl, CancellationToken ct)
    {
        if (BlockedReason(source, CrawlStep.Detail) is { } detailBlocked)
            return StepResult<BookDetail>.Fail(detailBlocked);
        if (string.IsNullOrWhiteSpace(bookUrl))
            return StepResult<BookDetail>.Fail(new StepError(CrawlStep.Detail, "bookUrl", "", "没有详情页地址"));

        var vars = CreateVariables(source, "", 1);
        var fetched = await FetchAsync(bookUrl, null, null, source.Url, ct).ConfigureAwait(false);
        if (fetched.Error is not null)
            return Fail<BookDetail>(CrawlStep.Detail, "bookUrl", bookUrl, fetched.Error);

        var ctx = BuildDocumentContext(vars, fetched.Body, fetched.Url);
        var info = source.Info;

        var tocUrl = _rules.EvaluateText(info?.TocUrl, ctx);
        var detail = new BookDetail
        {
            Name = _rules.EvaluateText(info?.Name, ctx),
            Author = _rules.EvaluateText(info?.Author, ctx),
            CoverUrl = ctx.AbsoluteUrl(_rules.EvaluateText(info?.CoverUrl, ctx)),
            Intro = _rules.EvaluateText(info?.Intro, ctx),
            Kind = _rules.EvaluateText(info?.Kind, ctx),
            LastChapter = _rules.EvaluateText(info?.LastChapter, ctx),
            TocUrl = ctx.AbsoluteUrl(string.IsNullOrWhiteSpace(tocUrl) ? bookUrl : tocUrl),
        };

        return StepResult<BookDetail>.Success(detail);
    }

    // ==================== 目录 ====================

    /// <summary>读章节目录（含 <c>nextTocUrl</c> 翻页，最多 <see cref="MaxTocPages"/> 页）。</summary>
    public async Task<StepResult<IReadOnlyList<Chapter>>> ChaptersAsync(BookSource source, string tocUrl, CancellationToken ct)
    {
        if (BlockedReason(source, CrawlStep.Toc) is { } tocBlocked)
            return StepResult<IReadOnlyList<Chapter>>.Fail(tocBlocked);
        if (string.IsNullOrWhiteSpace(tocUrl))
            return StepResult<IReadOnlyList<Chapter>>.Fail(new StepError(CrawlStep.Toc, "tocUrl", "", "没有目录页地址"));

        var chapters = new List<Chapter>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var url = tocUrl;
        var vars = CreateVariables(source, "", 1);

        for (var page = 0; page < MaxTocPages && !string.IsNullOrWhiteSpace(url); page++)
        {
            if (!visited.Add(url)) break;

            var fetched = await FetchAsync(url, null, null, source.Url, ct).ConfigureAwait(false);
            if (fetched.Error is not null)
                return chapters.Count > 0
                    ? StepResult<IReadOnlyList<Chapter>>.Partial(chapters,
                        new StepError(CrawlStep.Toc, "nextTocUrl", url, "目录第 " + (page + 1) + " 页失败：" + fetched.Error))
                    : Fail<IReadOnlyList<Chapter>>(CrawlStep.Toc, "tocUrl", url, fetched.Error);

            var ctx = BuildDocumentContext(vars, fetched.Body, fetched.Url);
            var list = _rules.Evaluate(source.Toc?.ChapterList, ctx);

            if (list.Kind == RuleValueKind.Nodes)
            {
                foreach (var node in list.Nodes)
                {
                    var sub = ctx.ForElement(node);
                    var name = _rules.EvaluateText(source.Toc?.ChapterName, sub);
                    var link = _rules.EvaluateText(source.Toc?.ChapterUrl, sub);
                    if (string.IsNullOrWhiteSpace(link)) link = SelfLink(node);

                    if (string.IsNullOrWhiteSpace(name)) name = (node.TextContent ?? "").Trim();
                    name = Regex.Replace(name, @"\s+", " ").Trim();
                    if (name.Length == 0 && string.IsNullOrWhiteSpace(link)) continue;

                    chapters.Add(new Chapter(name, ctx.AbsoluteUrl(link), chapters.Count));
                }
            }
            else if (list.Kind == RuleValueKind.Json)
            {
                foreach (var item in list.JsonItems)
                {
                    var sub = ctx.ForJson(item);
                    var name = _rules.EvaluateText(source.Toc?.ChapterName, sub);
                    var link = _rules.EvaluateText(source.Toc?.ChapterUrl, sub);
                    if (name.Length == 0 && string.IsNullOrWhiteSpace(link)) continue;
                    chapters.Add(new Chapter(name, ctx.AbsoluteUrl(link), chapters.Count));
                }
            }
            else if (page == 0)
            {
                return Fail<IReadOnlyList<Chapter>>(CrawlStep.Toc, "ruleToc.chapterList", source.Toc?.ChapterList ?? "",
                    ctx.Errors.Count > 0 ? ctx.Errors[0] : "目录选择器未命中");
            }

            var next = _rules.EvaluateText(source.Toc?.NextTocUrl, ctx);
            url = string.IsNullOrWhiteSpace(next) ? "" : ctx.AbsoluteUrl(next);
        }

        return chapters.Count > 0
            ? StepResult<IReadOnlyList<Chapter>>.Success(chapters)
            : Fail<IReadOnlyList<Chapter>>(CrawlStep.Toc, "ruleToc.chapterList", source.Toc?.ChapterList ?? "",
                "目录为空");
    }

    // ==================== 正文 ====================

    /// <summary>读正文（含 <c>nextContentUrl</c> 拼接，最多 <see cref="MaxContentPages"/> 页；<c>replaceRule</c> 按原文执行）。</summary>
    public async Task<StepResult<string>> ContentAsync(BookSource source, string chapterUrl, CancellationToken ct)
    {
        if (BlockedReason(source, CrawlStep.Content) is { } contentBlocked)
            return StepResult<string>.Fail(contentBlocked);
        if (string.IsNullOrWhiteSpace(chapterUrl))
            return StepResult<string>.Fail(new StepError(CrawlStep.Content, "chapterUrl", "", "没有章节地址"));

        // 正文缓存：**有缓存就直接用**（预下载写进来的也算），与此处是否「缓存正文」无关 ——
        // 那个开关管的是「每章读完要不要顺手存」，不是「能不能用已存的」。
        if (_cache is not null && _cache.Get(chapterUrl) is { } cached)
            return StepResult<string>.Success(cached);

        var builder = new StringBuilder();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var url = chapterUrl;
        var vars = CreateVariables(source, "", 1);

        for (var page = 0; page < MaxContentPages && !string.IsNullOrWhiteSpace(url); page++)
        {
            if (!visited.Add(url)) break;

            var fetched = await FetchAsync(url, null, null, source.Url, ct).ConfigureAwait(false);
            if (fetched.Error is not null)
                return builder.Length > 0
                    ? StepResult<string>.Partial(ApplyReplaceRules(builder.ToString().Trim(), source.Content?.ReplaceRule),
                        new StepError(CrawlStep.Content, "nextContentUrl", url, "续页失败：" + fetched.Error))
                    : Fail<string>(CrawlStep.Content, "chapterUrl", url, fetched.Error);

            var ctx = BuildDocumentContext(vars, fetched.Body, fetched.Url);
            var value = _rules.EvaluateContent(source.Content?.Content, ctx);
            var text = StripMarkup(value.FirstText);

            if (string.IsNullOrWhiteSpace(text))
            {
                if (page == 0)
                    return Fail<string>(CrawlStep.Content, "ruleContent.content", source.Content?.Content ?? "",
                        ctx.Errors.Count > 0 ? ctx.Errors[0] : "正文选择器未命中");
                break;
            }

            builder.AppendLine(text);

            var next = _rules.EvaluateText(source.Content?.NextContentUrl, ctx);
            url = string.IsNullOrWhiteSpace(next) ? "" : ctx.AbsoluteUrl(next);
        }

        var content = ApplyReplaceRules(builder.ToString().Trim(), source.Content?.ReplaceRule);
        // 本机全局净化规则（设置页「替换规则」）在书源内联规则之后再走一遍
        content = ReplaceRuleService.Apply(content, _settings.ReplaceRules).Trim();
        if (content.Length == 0)
            return Fail<string>(CrawlStep.Content, "ruleContent.content", source.Content?.Content ?? "", "正文为空");

        // 正文缓存：抓成功就写缓存（⛔ 失败 / 部分成功不写，避免把残缺正文固化下来）
        if (_cache is not null && _settings.CacheEnabled) _cache.Put(chapterUrl, content);

        return StepResult<string>.Success(content);
    }

    /// <summary>
    /// 正文兜底清洗：<c>ruleContent.content</c> 常写成 <c>…@html</c>，拿回来的是带标签的片段
    /// ⇒ 去标签、块级收尾转换行、解 HTML 实体（⛔ 不交给渲染器显示原始标签）。
    /// </summary>
    private static string StripMarkup(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.IndexOf('<') < 0 || text.IndexOf('>') < 0) return text;

        var stripped = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"</(p|div|li|h[1-6])>", "\n", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, "<[^>]+>", "");
        stripped = WebUtility.HtmlDecode(stripped);
        stripped = Regex.Replace(stripped, @"[ \t]+\n", "\n");
        stripped = Regex.Replace(stripped, @"\n{3,}", "\n\n");
        return stripped.Trim();
    }

    /// <summary>
    /// 执行书源自带的 <c>ruleContent.replaceRule</c>：每行 <c>规则##正则##替换串</c>（legado 的三段写法）。
    /// <para>🔴 与设置页的**全局替换规则**（两段 <c>正则##替换</c>，见 <see cref="ReplaceRuleService"/>）不是同一套语法，
    /// ⛔ 别把两者的解析混用。</para>
    /// </summary>
    private static string ApplyReplaceRules(string text, string? rules)
    {
        if (string.IsNullOrWhiteSpace(rules)) return text;

        foreach (var line in rules!.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var (_, pattern, replacement) = RuleSyntax.SplitReplace(trimmed);
            if (string.IsNullOrEmpty(pattern)) continue;

            try { text = Regex.Replace(text, pattern, replacement ?? "", RegexOptions.None, TimeSpan.FromSeconds(2)); }
            catch { /* 单条替换写错不影响正文 */ }
        }

        return text;
    }

    // ==================== 请求层 ====================

    private readonly record struct FetchResult(string Body, string Url, string? Error);

    private async Task<FetchResult> FetchAsync(string url, string? method, string? body, string? referer, CancellationToken ct)
    {
        var lastError = "请求失败";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get,
                    url);

                if (body is not null)
                    request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");

                if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
                    request.Headers.Referrer = refererUri;

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    lastError = "HTTP " + (int)response.StatusCode;
                    if ((int)response.StatusCode is 404 or 403) break;   // 不该重试
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                var charset = response.Content.Headers.ContentType?.CharSet;
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                return new FetchResult(Decode(bytes, charset), finalUrl, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new FetchResult("", url, "已取消");
            }
            catch (TaskCanceledException)
            {
                // 超时不重试：重试等于把等待时间翻倍（每源 2×超时），而多源并发搜索里
                // 一个站点不响应就足以让整批"永远在搜索"。
                return new FetchResult("", url, "超时");
            }
            catch (HttpRequestException ex)
            {
                lastError = "网络错误：" + ex.Message;
            }
            catch (Exception ex)
            {
                return new FetchResult("", url, "请求失败：" + ex.Message);
            }
        }

        return new FetchResult("", url, lastError);
    }

    private static bool _encodingReady;

    private static string Decode(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                EnsureEncodingProvider();
                return Encoding.GetEncoding(charset.Trim('"', ' ', ';')).GetString(bytes);
            }
            catch { /* 站点声明的编码名不认识 ⇒ 走嗅探 */ }
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            EnsureEncodingProvider();
            try { return Encoding.GetEncoding("gb18030").GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }

    private static void EnsureEncodingProvider()
    {
        if (_encodingReady) return;
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _encodingReady = true; } catch { }
    }

    // ==================== 请求地址 / 上下文 ====================

    private readonly record struct RequestOptions(string? Method, string? Body);

    /// <summary>拆 <c>地址, {"method":"POST","body":"…"}</c> 这种请求配置尾巴。</summary>
    private static (string Url, RequestOptions Options) SplitRequest(string raw)
    {
        var text = raw.Trim();
        var index = text.IndexOf(", {", StringComparison.Ordinal);
        if (index < 0) index = text.IndexOf(",{", StringComparison.Ordinal);
        if (index < 0) return (text, default);

        var url = text.Substring(0, index).Trim();
        var json = text.Substring(index + 1).Trim();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            return (url, new RequestOptions(method, body));
        }
        catch { return (text, default); }
    }

    /// <summary>插值请求地址 / 请求体：<c>{{key}}</c> 走变量表，<c>{{java.xxx()}}</c> 交给 JS。</summary>
    private string InterpolateRule(string rule, RuleContext ctx, out bool needsJs)
        => RuleSyntax.Interpolate(rule, ctx, expression => _rules.EvaluateExpression(expression, ctx), out needsJs);

    private static RuleContext CreateVariables(BookSource source, string key, int page, bool gbkKey = false)
    {
        var ctx = new RuleContext();
        ctx.Vars["key"] = EscapeKey(key ?? "", gbkKey);
        ctx.Vars["keyUnescaped"] = key ?? "";
        ctx.Vars["page"] = page.ToString(CultureInfo.InvariantCulture);
        ctx.Vars["baseUrl"] = HostOf(source.Url);
        ctx.Vars["sourceUrl"] = source.Url;
        ctx.Vars["sourceName"] = source.Name ?? "";
        return ctx;
    }

    /// <summary>站点要求 GBK 系编码的搜索词时，按 GB18030 逐字节百分号编码（⛔ 不能直接用 UTF-8）。</summary>
    private static string EscapeKey(string key, bool gbk)
    {
        if (!gbk) return Uri.EscapeDataString(key);

        try
        {
            EnsureEncodingProvider();
            var bytes = Encoding.GetEncoding("gb18030").GetBytes(key);
            var sb = new StringBuilder(bytes.Length * 3);
            foreach (var b in bytes) sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
        catch { return Uri.EscapeDataString(key); }
    }

    private static bool IsGbkText(string? text)
        => !string.IsNullOrEmpty(text)
           && (text!.Contains("gbk", StringComparison.OrdinalIgnoreCase)
               || text.Contains("gb2312", StringComparison.OrdinalIgnoreCase)
               || text.Contains("gb18030", StringComparison.OrdinalIgnoreCase));

    /// <summary>把规则里的地址拼成绝对地址：/search?q=… 以书源地址的主机为基准， //host/path 补协议，已经是绝对地址的原样返回。（WHY: RD-011）</summary>
    private static string ResolveUrl(string? baseUrl, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var text = value.Trim();

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return text;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) return text;

        var authority = baseUri.GetLeftPart(UriPartial.Authority);

        if (text.StartsWith("//", StringComparison.Ordinal))
            return baseUri.Scheme + ":" + text;

        if (text.StartsWith("/", StringComparison.Ordinal))
            return authority + text;

        var path = baseUri.AbsolutePath;
        var slash = path.LastIndexOf('/');
        var dir = slash >= 0 ? path.Substring(0, slash + 1) : "/";
        return authority + dir + text;
    }

    private RuleContext BuildDocumentContext(RuleContext vars, string html, string url)
    {
        IElement? root = null;
        try { root = new HtmlParser().ParseDocument(html).DocumentElement; }
        catch { root = null; }

        return vars.WithDocument(root, url, html);
    }

    /// <summary>
    /// 元素自身或内部第一个 <c>&lt;a&gt;</c> 的 href —— <c>ruleSearch.bookUrl</c> / <c>ruleToc.chapterUrl</c>
    /// 留空时（书单 / 目录直接选中链接元素）就靠它。
    /// </summary>
    private static string SelfLink(IElement node)
        => node.GetAttribute("href")
           ?? node.QuerySelector("a")?.GetAttribute("href")
           ?? "";

    private static string HostOf(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.GetLeftPart(UriPartial.Authority);
        return url ?? "";
    }

    // ==================== 前置检查 / 失败构造 ====================

    private static StepError? BlockedReason(BookSource source, CrawlStep step)
    {
        if (!source.Enabled) return new StepError(step, "enabled", "", "书源已停用");

        return source.Capability switch
        {
            SourceCapability.NeedsWebView => new StepError(step, "source", "", "该源依赖 WebView，桌面端无法执行：" + source.CapabilityReason),
            SourceCapability.Incomplete => new StepError(step, "source", "", "书源规则不完整：" + source.CapabilityReason),
            _ => null,
        };
    }

    private static StepResult<T> Fail<T>(CrawlStep step, string ruleName, string rule, string message)
        => StepResult<T>.Fail(new StepError(step, ruleName, rule, message));

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
