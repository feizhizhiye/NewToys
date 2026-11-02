using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Reading.Models;
using NewToys.Reading.Services;
using NewToys.Shared.Services;

namespace NewToys.Reading.ViewModels;

/// <summary>
/// 「网络阅读 → 阅读」页（<c>reading-read</c>）：沉浸式读一本书 —— 左目录、右正文、顶部排版条。
/// <para>入口：书架「继续阅读」、发现页「读这本书」，都靠 <see cref="ReadingSession.PendingBook"/> 带一本书进来。</para>
/// <para>排版参数与「设置 → 网络阅读设置」是**同一份**（<c>config\reading.json</c>），本页改完即时重排并落盘。</para>
/// </summary>
public partial class ReaderViewModel : ObservableObject, IStatusProvider, IDisposable
{
    private readonly SourceStore _store;
    private readonly CrawlService _crawl;
    private readonly ShelfStore _shelf;
    private readonly ProgressStore _progress;
    private readonly ReadingSettingsService _settings;
    private readonly ReaderStyleService _styles;
    private readonly ReadingSession _session;
    private readonly INavigationService? _nav;
    private readonly PrefetchService? _prefetch;
    private readonly BookDownloadService? _download;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _prefetchCts;
    private CancellationTokenSource? _downloadCts;
    private bool _syncing;

    /// <summary>当前这本书来自哪个书源（换源 / 抓正文都要它）。</summary>
    private string _sourceUrl = "";

    [ObservableProperty] private string _bookName = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _bookUrl = "";
    [ObservableProperty] private string _sourceName = "";
    [ObservableProperty] private Chapter? _selectedChapter;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyPhase = "";
    [ObservableProperty] private string _statusText = "从书架或发现页打开一本书";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _chapterTitle = "";
    [ObservableProperty] private string _contentText = "";
    [ObservableProperty] private bool _showToc;
    [ObservableProperty] private string _tocFilter = "";

    /// <summary>换源浮层是否展开（顶栏「🔀 换源」弹出；选一个源 / 关闭收起）。</summary>
    [ObservableProperty] private bool _showSwitchPanel;

    /// <summary>整本下载浮层是否展开。</summary>
    [ObservableProperty] private bool _downloadIsOpen;

    /// <summary>整本下载进行中（按钮 / 输入项据此置灰）。</summary>
    [ObservableProperty] private bool _isDownloading;

    /// <summary>下载进度读数（「已下 / 总数 · 章节名」）。</summary>
    [ObservableProperty] private string _downloadPhase = "";

    /// <summary>下载进度（0~100）。</summary>
    [ObservableProperty] private double _downloadProgress;

    /// <summary>换源比对进行中（浮层里显示「正在比对…」）。</summary>
    [ObservableProperty] private bool _isSwitching;

    [ObservableProperty] private int _fontSize = ReadingSettingsService.DefaultFontSize;
    [ObservableProperty] private double _lineSpacing = ReadingSettingsService.DefaultLineSpacing;
    [ObservableProperty] private int _contentWidth = ReadingSettingsService.DefaultContentWidth;
    [ObservableProperty] private string _fontFamily = ReadingSettingsService.DefaultFontFamily;
    [ObservableProperty] private ReaderTheme _theme = ReaderTheme.Light;

    /// <summary>首行缩进（全角空格个数）。</summary>
    [ObservableProperty] private int _indent = ReadingSettingsService.DefaultIndent;

    /// <summary>段间距（段与段之间的空行数）。</summary>
    [ObservableProperty] private int _paragraphSpacing = ReadingSettingsService.DefaultParagraphSpacing;

    /// <summary>正文左右留白（px）。</summary>
    [ObservableProperty] private int _readerPadding = 28;

    /// <summary>样式卡是否展开（顶栏「🎨 样式」弹出；遮罩 / 关闭按钮收起）。</summary>
    [ObservableProperty] private bool _showStylePanel;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public ReaderViewModel() : this(null!, null!, null!, null!, null!, null!, new ReadingSession()) { }

    public ReaderViewModel(
        SourceStore store, CrawlService crawl, ShelfStore shelf, ProgressStore progress,
        ReadingSettingsService settings, ReaderStyleService styles, ReadingSession session,
        INavigationService? nav = null, PrefetchService? prefetch = null, BookDownloadService? download = null)
    {
        _store = store;
        _crawl = crawl;
        _shelf = shelf;
        _progress = progress;
        _settings = settings;
        _styles = styles;
        _session = session;
        _nav = nav;
        _prefetch = prefetch;
        _download = download;

        if (settings is not null)
        {
            settings.Changed += OnSettingsChanged;
            LoadTypography();
        }
    }

    /// <summary>目录全量（<c>Chapters</c> 是它的过滤视图）。</summary>
    public ObservableCollection<Chapter> Chapters { get; } = new();

    /// <summary>过滤后的目录（界面绑这个）。</summary>
    public ObservableCollection<Chapter> VisibleChapters { get; } = new();

    /// <summary>换源比对的候选源（界面绑这个）。</summary>
    public ObservableCollection<SwitchCandidate> SwitchCandidates { get; } = new();

    /// <summary>正文字变了（视图据此推给 <c>TextViewer</c>，并带上要续读的位置）。</summary>
    public event EventHandler? ContentChanged;

    /// <summary>排版参数变了（字号 / 行距 / 字体 / 配色 / 页宽）。</summary>
    public event EventHandler? TypographyChanged;

    public event EventHandler? StatusChanged;

    /// <summary>正文重新加载后要滚到的位置（0~1）。</summary>
    public double PendingProgress { get; private set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsNotBusy => !IsBusy;

    public bool HasBook => !string.IsNullOrWhiteSpace(BookUrl);

    public bool HasChapters => Chapters.Count > 0;

    public bool HasSwitchCandidates => SwitchCandidates.Count > 0;

    /// <summary>阅读区背景（自定义色优先，否则配色档）。</summary>
    public string ReaderBackground => _settings is null
        ? ReadingSettingsService.BackgroundOf(Theme)
        : _settings.EffectiveBackground(Theme);

    /// <summary>阅读区前景。</summary>
    public string ReaderForeground => _settings is null
        ? ReadingSettingsService.ForegroundOf(Theme)
        : _settings.EffectiveForeground(Theme);

    /// <summary>顶部排版条上的读数。</summary>
    public string TypographyText =>
        $"{FontSize}px · 行距 {LineSpacing:0.0} · {ReadingSettingsService.ThemeText(Theme)} · 缩进 {Indent} · 段距 {ParagraphSpacing}";

    /// <summary>抓回来的正文按当前「缩进 / 段距」重排后的结果（视图推给 <c>TextViewer</c> 的就是这个）。</summary>
    public string FormattedContent => ReaderTypography.Apply(ContentText, Indent, ParagraphSpacing);

    /// <summary>样式卡里的内置预设。</summary>
    public IReadOnlyList<Models.ReaderStyle> StylePresets =>
        _styles?.Presets ?? (IReadOnlyList<Models.ReaderStyle>)Array.Empty<Models.ReaderStyle>();

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnBookUrlChanged(string value) => OnPropertyChanged(nameof(HasBook));

    partial void OnThemeChanged(ReaderTheme value)
    {
        OnPropertyChanged(nameof(ReaderBackground));
        OnPropertyChanged(nameof(ReaderForeground));
        OnPropertyChanged(nameof(TypographyText));
        TypographyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 设置在别处被改了（设置页 / 导入样式 / 套用预设）⇒ 灌一次排版参数。
    /// <para>🔴 只走 <see cref="TypographyChanged"/>（视图据此「保住当前滚动位置」重排），
    /// ⛔ 不走 <see cref="ContentChanged"/> —— 那条路会把滚动位置拉回本章开头。</para>
    /// </summary>
    private void OnSettingsChanged() => LoadTypography();

    partial void OnFontSizeChanged(int value) => RaiseTypography("reader-font-size");

    partial void OnLineSpacingChanged(double value) => RaiseTypography("reader-line-spacing");

    partial void OnFontFamilyChanged(string value) => RaiseTypography("reader-font-family");

    partial void OnContentWidthChanged(int value) => RaiseTypography("reader-content-width");

    partial void OnReaderPaddingChanged(int value) => RaiseTypography("reader-padding");

    partial void OnIndentChanged(int value) => RaiseTypography("reader-indent");

    partial void OnParagraphSpacingChanged(int value) => RaiseTypography("reader-paragraph-spacing");

    partial void OnContentTextChanged(string value) => ContentChanged?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedChapterChanged(Chapter? value)
    {
        // 点选章节就把弹出来的目录收起（目录是覆盖层，不收起会一直挡着正文）
        if (ShowToc) ShowToc = false;
        if (value is not null) _ = LoadContentAsync(value);
    }

    partial void OnTocFilterChanged(string value) => RebuildVisibleChapters();

    // ==================== 打开一本书 ====================

    /// <summary>
    /// 取走并打开「待读的书」。
    /// <para>🔴 标签页复用时 VM 不重建 ⇒ 每次页面重新挂载都要再查一次，否则第二次跳转没反应。</para>
    /// </summary>
    public void CheckPending()
    {
        if (_session.TakePendingBook() is { } pending) _ = OpenAsync(pending);
    }

    /// <summary>按 <see cref="PendingBook"/> 打开：详情 → 目录 → 续读章节。</summary>
    public async Task OpenAsync(PendingBook pending)
    {
        if (_crawl is null) { StatusText = "⚠ 抓取服务未就绪。"; return; }

        var source = FindSource(pending.SourceUrl);
        if (source is null)
        {
            ErrorText = $"《{pending.Name}》的来源书源已删除或已停用 —— 回书架换一本，或先到「书源」启停它。";
            StatusText = "⚠ 书源不可用";
            return;
        }

        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        BookUrl = pending.BookUrl;
        BookName = pending.Name;
        Author = "";
        SourceName = source.Name;
        _sourceUrl = source.Url;
        Chapters.Clear();
        VisibleChapters.Clear();
        SelectedChapter = null;
        ContentText = "";
        ChapterTitle = "";
        ErrorText = "";
        OnPropertyChanged(nameof(HasChapters));
        IsBusy = true;
        BusyPhase = "正在读目录…";

        try
        {
            var detail = await _crawl.DetailAsync(source, pending.BookUrl, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            if (detail.Value is not null)
            {
                if (!string.IsNullOrWhiteSpace(detail.Value.Name)) BookName = detail.Value.Name;
                Author = detail.Value.Author;
                SourceName = source.Name;
            }

            var tocUrl = string.IsNullOrWhiteSpace(detail.Value?.TocUrl) ? pending.BookUrl : detail.Value!.TocUrl;
            var chapters = await _crawl.ChaptersAsync(source, tocUrl, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            if (chapters.Value is not null)
                foreach (var chapter in chapters.Value) Chapters.Add(chapter);

            OnPropertyChanged(nameof(HasChapters));
            RebuildVisibleChapters();

            ErrorText = string.Join("\n", new[] { detail.Error, chapters.Error }
                .Where(e => e is not null).Select(e => e!.Display));

            if (Chapters.Count == 0)
            {
                StatusText = "⚠ 目录为空";
                return;
            }

            // 续读：换源带来的章节优先 → 否则进度表 → 否则第 1 章
            var saved = _progress?.Get(BookUrl);
            Chapter? resume = null;

            if (!string.IsNullOrWhiteSpace(_resumeChapterUrl))
            {
                resume = Chapters.FirstOrDefault(c => c.Url == _resumeChapterUrl);
                _resumeChapterUrl = "";
            }

            if (resume is null && saved is not null)
                resume = Chapters.FirstOrDefault(c => c.Url == saved.ChapterUrl);

            if (resume is not null)
            {
                PendingProgress = saved is not null && resume.Url == saved.ChapterUrl ? saved.Offset : 0;
                StatusText = $"《{BookName}》续读：{resume.Name}";
            }
            else
            {
                PendingProgress = 0;
                StatusText = $"《{BookName}》共 {Chapters.Count} 章";
            }

            // 目录就绪 ⇒ 交棒给正文加载（🔴 先放掉目录流程的令牌，否则正文加载的 Cancel() 会把还没收尾的目录流程也取消）
            try { _cts?.Dispose(); } catch { /* 已释放 */ }
            _cts = null;

            SelectedChapter = resume ?? Chapters[0];
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = "打开失败：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }
    }

    // ==================== 翻章 / 目录 ====================

    [RelayCommand]
    private void PrevChapter()
    {
        if (Chapters.Count == 0) return;
        var index = SelectedChapter is null ? 0 : Chapters.IndexOf(SelectedChapter);
        if (index > 0) SelectedChapter = Chapters[index - 1];
    }

    [RelayCommand]
    private void NextChapter()
    {
        if (Chapters.Count == 0) return;
        var index = SelectedChapter is null ? -1 : Chapters.IndexOf(SelectedChapter);
        if (index >= 0 && index < Chapters.Count - 1) SelectedChapter = Chapters[index + 1];
    }

    [RelayCommand]
    private void ToggleToc() => ShowToc = !ShowToc;

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { /* 已释放 */ }
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>把当前进度写库（视图定时 / 离开页面时调）。</summary>
    public void SaveProgress(double offset)
    {
        if (_settings is not null && !_settings.RememberProgress) return;
        if (_progress is null || SelectedChapter is null || string.IsNullOrWhiteSpace(BookUrl)) return;
        if (string.IsNullOrWhiteSpace(SelectedChapter.Url)) return;

        _progress.Save(BookUrl, SelectedChapter.Url, SelectedChapter.Name, offset);
    }

    /// <summary>加入书架（已在则刷新书名作者）。</summary>
    [RelayCommand]
    private void AddToShelf()
    {
        if (_shelf is null || string.IsNullOrWhiteSpace(BookUrl)) return;

        var ok = _shelf.Add(new ShelfBook
        {
            BookUrl = BookUrl,
            SourceUrl = FindSourceUrl(),
            Name = BookName,
            Author = Author,
        });

        StatusText = ok ? $"《{BookName}》已加入书架" : "⚠ 加入书架失败（书库不可用）";
    }

    /// <summary>
    /// 「🔀 换源」：以当前书名并发搜所有可用源 → 按「书名匹配度 → 响应速度」排序 → 弹换源浮层，
    /// 选一个源后原地切源续读当前章（⛔ 不再跳到搜索页手动搜）。
    /// </summary>
    [RelayCommand]
    private async Task SwitchSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(BookName)) { StatusText = "先打开一本书。"; return; }
        if (_crawl is null || _store is null) { StatusText = "⚠ 换源服务未就绪。"; return; }

        // 找所有「可用 + 已启用」的源（当前源也参与，方便回到它）
        var sources = _store.LoadAll()
            .Where(s => s.Enabled && s.Capability == SourceCapability.Ready)
            .ToList();
        if (sources.Count == 0) { StatusText = "没有可用书源可换。"; return; }

        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        SwitchCandidates.Clear();
        OnPropertyChanged(nameof(HasSwitchCandidates));
        ShowSwitchPanel = true;
        IsSwitching = true;
        StatusText = $"正在用 {sources.Count} 个书源比对「{BookName}」…";

        var start = DateTimeOffset.UtcNow;
        try
        {
            var results = await _crawl.SearchManyAsync(sources, BookName, 1, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            var candidates = new List<SwitchCandidate>();
            foreach (var result in results)
            {
                if (!result.Ok) continue;
                var elapsed = (DateTimeOffset.UtcNow - start).TotalMilliseconds;
                foreach (var item in result.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.BookUrl)) continue;
                    candidates.Add(new SwitchCandidate
                    {
                        SourceName = result.Source.Name,
                        SourceUrl = result.Source.Url,
                        BookUrl = item.BookUrl,
                        Name = item.Name,
                        Author = item.Author,
                        Match = MatchScore(BookName, item.Name),
                        ElapsedMs = elapsed,
                    });
                }
            }

            // 排序：匹配度降序 → 耗时升序；同名同源去重（取匹配度更高者）
            candidates = candidates
                .GroupBy(c => (c.SourceUrl, c.BookUrl))
                .Select(g => g.OrderByDescending(c => c.Match).ThenBy(c => c.ElapsedMs).First())
                .OrderByDescending(c => c.Match)
                .ThenBy(c => c.ElapsedMs)
                .ToList();

            foreach (var c in candidates) SwitchCandidates.Add(c);
            OnPropertyChanged(nameof(HasSwitchCandidates));

            StatusText = candidates.Count == 0
                ? "⚠ 没有源命中这本书 —— 换一个书名，或到「书源」补源"
                : $"比对出 {candidates.Count} 个候选源，点一个即切换";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText = "⚠ 换源异常：" + ex.Message; }
        finally
        {
            IsSwitching = false;
        }
    }

    /// <summary>书名匹配度：2 = 精确相等，1 = 包含，0 = 其它。</summary>
    private static int MatchScore(string keyword, string? name)
    {
        var k = (keyword ?? "").Trim();
        var n = (name ?? "").Trim();
        if (k.Length == 0 || n.Length == 0) return 0;
        if (string.Equals(k, n, StringComparison.OrdinalIgnoreCase)) return 2;
        if (n.Contains(k, StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    /// <summary>换源浮层里选一个候选源 → 原地重开这本书（续读当前章）。</summary>
    [RelayCommand]
    private void ChooseSwitchCandidate(SwitchCandidate? candidate)
    {
        if (candidate is null) return;
        ShowSwitchPanel = false;

        var chapter = SelectedChapter;
        _session.PendingBook = new PendingBook(candidate.SourceUrl, candidate.BookUrl, candidate.Name);
        StatusText = $"切换书源：{candidate.SourceName}";

        // 记下当前章节地址，OpenAsync 续读时优先回到它（找不到则从第 1 章）
        _resumeChapterUrl = chapter?.Url ?? "";
        _ = OpenAsync(new PendingBook(candidate.SourceUrl, candidate.BookUrl, candidate.Name));
    }

    /// <summary>换源后要优先续读的章节地址（<see cref="ChooseSwitchCandidate"/> 记下，<see cref="OpenAsync"/> 消费）。</summary>
    private string _resumeChapterUrl = "";

    /// <summary>关闭换源浮层。</summary>
    [RelayCommand]
    private void CloseSwitch() => ShowSwitchPanel = false;

    // ==================== 整本下载 ====================

    /// <summary>
    /// 把这本书的全部章节抓下来合成一份 TXT（legado 的「缓存全书」→ 导出）。
    /// <para>先选保存位置再开跑：下载可能几分钟，⛔ 别让用户等完了才发现没地方存。</para>
    /// </summary>
    [RelayCommand]
    private async Task DownloadBookAsync()
    {
        var downloadService = _download;
        if (downloadService is null) { DownloadPhase = "⚠ 下载服务未就绪。"; return; }
        if (IsDownloading) return;

        var source = FindSource(FindSourceUrl());
        if (source is null) { DownloadPhase = "⚠ 找不到这本书对应的书源。"; return; }
        if (Chapters.Count == 0) { DownloadPhase = "⚠ 还没有目录，先打开一本书。"; return; }

        var path = await DialogHooks.SaveFileAsync("整本下载", null,
            BookDownloadService.SuggestFileName(BookName), "*.txt").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;

        try { _downloadCts?.Cancel(); } catch { /* 上一轮已结束 */ }
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var token = _downloadCts.Token;

        var chapters = Chapters.ToList();

        DownloadIsOpen = true;
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadPhase = $"准备下载 {chapters.Count} 章…";

        var progress = new Progress<(int Done, int Total, string Chapter)>(p =>
        {
            DownloadProgress = p.Total == 0 ? 0 : p.Done * 100.0 / p.Total;
            DownloadPhase = $"{p.Done} / {p.Total} 章 · {p.Chapter}";
        });

        try
        {
            var result = await downloadService
                .DownloadAsync(source, BookName, Author, chapters, progress, token).ConfigureAwait(true);

            File.WriteAllText(path!, result.Text, new UTF8Encoding(true));

            DownloadProgress = 100;
            DownloadPhase = result.IsComplete
                ? $"✅ 已保存 {result.Downloaded} 章到 {path}"
                : $"⚠ 已保存 {result.Downloaded}/{result.Total} 章到 {path}"
                  + $"，{result.Failures.Count} 章失败（再点一次即可，已下载的走正文缓存）\n"
                  + BookDownloadService.DescribeFailures(result.Failures);

            StatusText = result.IsComplete
                ? $"整本下载完成 · {result.Downloaded} 章"
                : $"整本下载完成（{result.Failures.Count} 章失败）";

            // 章节全下过一遍 ⇒ 顺手把预取队列停掉，别和刚下完的缓存较劲
            try { _prefetchCts?.Cancel(); } catch { /* 已结束 */ }
        }
        catch (OperationCanceledException) { DownloadPhase = "已取消"; }
        catch (Exception ex) { DownloadPhase = "⚠ 下载失败：" + ex.Message; }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        try { _downloadCts?.Cancel(); } catch { /* 已释放 */ }
        if (IsDownloading) DownloadPhase = "正在停止…";
    }

    [RelayCommand]
    private void CloseDownload() => DownloadIsOpen = false;


    // ==================== 排版 ====================

    [RelayCommand]
    private void FontBigger() { if (_settings is not null) _settings.SetFontSize(FontSize + 1); else FontSize++; }

    [RelayCommand]
    private void FontSmaller() { if (_settings is not null) _settings.SetFontSize(FontSize - 1); else FontSize--; }

    [RelayCommand]
    private void LineWider() { if (_settings is not null) _settings.SetLineSpacing(LineSpacing + 0.1); else LineSpacing += 0.1; }

    [RelayCommand]
    private void LineNarrower() { if (_settings is not null) _settings.SetLineSpacing(LineSpacing - 0.1); else LineSpacing -= 0.1; }

    /// <summary>在「白底 → 米黄 → 暗色」之间轮换。</summary>
    [RelayCommand]
    private void CycleTheme()
    {
        var next = Theme switch
        {
            ReaderTheme.Light => ReaderTheme.Sepia,
            ReaderTheme.Sepia => ReaderTheme.Dark,
            _ => ReaderTheme.Light,
        };

        if (_settings is not null) _settings.SetTheme(next);
        else Theme = next;
    }

    [RelayCommand]
    private void IndentBigger() { if (_settings is not null) _settings.SetIndent(Indent + 1); else Indent++; }

    [RelayCommand]
    private void IndentSmaller() { if (_settings is not null) _settings.SetIndent(Indent - 1); else Indent--; }

    [RelayCommand]
    private void ParagraphWider() { if (_settings is not null) _settings.SetParagraphSpacing(ParagraphSpacing + 1); else ParagraphSpacing++; }

    [RelayCommand]
    private void ParagraphNarrower() { if (_settings is not null) _settings.SetParagraphSpacing(ParagraphSpacing - 1); else ParagraphSpacing--; }

    // ==================== 样式卡（预设 / 导入 / 导出） ====================

    [RelayCommand]
    private void ToggleStyle() => ShowStylePanel = !ShowStylePanel;

    [RelayCommand]
    private void CloseStyle() => ShowStylePanel = false;

    /// <summary>套用一张内置样式卡（整份覆盖，⛔ 不是挑着改）。</summary>
    [RelayCommand]
    private void ApplyPreset(Models.ReaderStyle? style)
    {
        if (style is null) return;
        if (_styles is null) { StatusText = "⚠ 样式服务未就绪。"; return; }

        StatusText = _styles.Apply(style);   // 写盘 ⇒ settings.Changed ⇒ 自动重排
    }

    /// <summary>导入一个样式文件（<c>.json</c>）并套用；字段缺失的项保持原值。</summary>
    [RelayCommand]
    private async Task ImportStyle()
    {
        if (_styles is null) { StatusText = "⚠ 样式服务未就绪。"; return; }

        var files = await DialogHooks.PickFilesAsync("选择阅读样式 JSON", null, false, "*.json").ConfigureAwait(true);
        if (files.Count == 0) { StatusText = "已取消导入样式"; return; }

        StatusText = _styles.Import(files[0]);
    }

    /// <summary>把当前排版导成一份样式文件，可带走 / 分享。</summary>
    [RelayCommand]
    private async Task ExportStyle()
    {
        if (_styles is null) { StatusText = "⚠ 样式服务未就绪。"; return; }

        var path = await DialogHooks.SaveFileAsync("导出阅读样式", null, "reader-style.json", "*.json").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) { StatusText = "已取消导出样式"; return; }

        StatusText = _styles.Export(path!);
    }

    /// <summary>从设置里灌一次排版参数（⛔ 不回写，避免刚打开就落盘）。</summary>
    public void LoadTypography()
    {
        if (_settings is null) return;

        _syncing = true;
        FontSize = _settings.FontSize;
        LineSpacing = _settings.LineSpacing;
        FontFamily = _settings.FontFamily;
        ContentWidth = _settings.ContentWidth;
        Theme = _settings.Theme;
        ReaderPadding = _settings.ReaderPadding;
        Indent = _settings.Indent;
        ParagraphSpacing = _settings.ParagraphSpacing;
        _syncing = false;

        OnPropertyChanged(nameof(TypographyText));
        OnPropertyChanged(nameof(ReaderBackground));
        OnPropertyChanged(nameof(ReaderForeground));
        TypographyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseTypography(string changedKey)
    {
        if (_syncing) return;

        if (_settings is not null)
        {
            var status = changedKey switch
            {
                "reader-font-size" => _settings.SetFontSize(FontSize),
                "reader-line-spacing" => _settings.SetLineSpacing(LineSpacing),
                "reader-font-family" => _settings.SetFontFamily(FontFamily),
                "reader-indent" => _settings.SetIndent(Indent),
                "reader-paragraph-spacing" => _settings.SetParagraphSpacing(ParagraphSpacing),
                "reader-padding" => _settings.SetReaderPadding(ReaderPadding),
                _ => _settings.SetContentWidth(ContentWidth),
            };

            // 服务会把值钳到范围内 ⇒ 回读一次，界面与磁盘保持一致
            _syncing = true;
            FontSize = _settings.FontSize;
            LineSpacing = _settings.LineSpacing;
            ContentWidth = _settings.ContentWidth;
            ReaderPadding = _settings.ReaderPadding;
            Indent = _settings.Indent;
            ParagraphSpacing = _settings.ParagraphSpacing;
            _syncing = false;

            // 写盘已触发 settings.Changed ⇒ 视图会保住滚动位置重排；这里只报状态
            StatusText = status;
        }

        OnPropertyChanged(nameof(TypographyText));
        OnPropertyChanged(nameof(ReaderBackground));
        OnPropertyChanged(nameof(ReaderForeground));
        TypographyChanged?.Invoke(this, EventArgs.Empty);
    }

    // ==================== 正文 ====================

    private async Task LoadContentAsync(Chapter chapter)
    {
        var source = FindSource(FindSourceUrl());
        if (source is null) { ErrorText = "找不到这本书对应的书源。"; return; }
        if (string.IsNullOrWhiteSpace(chapter.Url))
        {
            ErrorText = "这一章没有链接（目录是纯标题）。";
            return;
        }

        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        BusyPhase = "正在抓正文…";
        ErrorText = "";
        ChapterTitle = chapter.Name;
        PendingProgress = 0;

        try
        {
            var content = await _crawl.ContentAsync(source, chapter.Url, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            ContentText = content.Value ?? "";
            ErrorText = content.Error?.Display ?? "";
            StatusText = ContentText.Length > 0
                ? $"{chapter.Name} · {ContentText.Length} 字"
                : "⚠ 正文为空";

            SaveProgress(0);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = "抓正文异常：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }

        StartPrefetch(source, chapter);
    }

    /// <summary>
    /// 后台把后面几章抓进正文缓存（章数来自设置）。换章就重起一轮：⛔ 不要在旧队列上叠加，
    /// 否则来回翻几章会攒出一堆并行请求。关掉「缓存正文」时服务自己会跳过。
    /// </summary>
    private void StartPrefetch(BookSource source, Chapter chapter)
    {
        var prefetch = _prefetch;
        if (prefetch is null || _settings is null) return;

        var count = _settings.PrefetchCount;
        if (count <= 0) return;

        var index = Chapters.IndexOf(chapter);
        if (index < 0 || index + 1 >= Chapters.Count) return;

        // 集合只能在 UI 线程上取快照，后台只读这份 List
        var snapshot = Chapters.ToList();

        try { _prefetchCts?.Cancel(); } catch { /* 上一轮已结束 */ }
        _prefetchCts?.Dispose();
        _prefetchCts = new CancellationTokenSource();
        var token = _prefetchCts.Token;

        _ = Task.Run(() => prefetch.RunAsync(source, snapshot, index + 1, count, token), token);
    }

    private void RebuildVisibleChapters()
    {
        var keyword = (TocFilter ?? "").Trim();
        VisibleChapters.Clear();

        foreach (var chapter in Chapters)
        {
            if (keyword.Length > 0 && chapter.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
            VisibleChapters.Add(chapter);
        }
    }

    private string FindSourceUrl() => _sourceUrl;

    private BookSource? FindSource(string? url)
    {
        if (_store is null || string.IsNullOrWhiteSpace(url)) return null;
        return _store.LoadAll().FirstOrDefault(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        Cancel();
        try { _prefetchCts?.Cancel(); } catch { /* 已结束 */ }
        try { _downloadCts?.Cancel(); } catch { /* 已结束 */ }
        _prefetchCts?.Dispose();
        _downloadCts?.Dispose();
        if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        ContentChanged = null;
        TypographyChanged = null;
        StatusChanged = null;
    }
}
