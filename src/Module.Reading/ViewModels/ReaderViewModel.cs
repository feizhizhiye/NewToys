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
using NewToys.Reading.Controls;
using NewToys.Reading.Models;
using NewToys.Reading.Services;
using NewToys.Shared.Services;
// 同名消歧：排版模型在 Controls，Services.ReaderTypography 是静态排版助手
using ReaderTypography = NewToys.Reading.Controls.ReaderTypography;

namespace NewToys.Reading.ViewModels;

/// <summary>
/// 「网络阅读 → 阅读」页（<c>reading-read</c>）：桌面 legado 的阅读器 —— 正文渲染交给
/// <see cref="Controls.ReaderShell"/>（索引书模式），本 VM 管「选书 / 抓取 / 换源 / 下载 / 听书」。
/// <para>入口：书架「继续阅读」、发现页「读这本书」，都靠 <see cref="ReadingSession.PendingBook"/> 带一本书进来。</para>
/// <para>
/// 🔴 章导航 / 目录 / 书签 / 搜索 / 排版都在壳里，壳只发事件不落盘：
/// 进度 → <see cref="ProgressStore"/>（本地文件按绝对路径记，章节键 = <c>#序号</c>）；
/// 排版 → <see cref="ReadingSettingsService"/>；书签 → 书目录的 <c>bookmarks.json</c>。
/// </para>
/// </summary>
public partial class ReaderViewModel : ObservableObject, IStatusProvider, IDisposable
{
    private readonly SourceStore _store;
    private readonly CrawlService _crawl;
    private readonly ShelfStore _shelf;
    private readonly ProgressStore _progress;
    private readonly ReadingSettingsService? _settings;
    private readonly ReadingSession _session;
    private readonly INavigationService? _nav;
    private readonly PrefetchService? _prefetch;
    private readonly BookDownloadService? _download;
    private readonly TtsService? _tts;
    private readonly BookIndexWriter? _books;
    private readonly BookmarksStore _bookmarksStore = new();
    private readonly TxtBookService? _txt;

    private CancellationTokenSource? _prefetchCts;
    private CancellationTokenSource? _downloadCts;
    private bool _syncing;

    /// <summary>当前这本书来自哪个书源（换源 / 抓正文都要它）。</summary>
    private string _sourceUrl = "";

    /// <summary>非空 = 当前读的是本地 TXT（进度按路径记，⛔ 换源 / 整本下载）。</summary>
    private string _localPath = "";

    /// <summary>当前索引书目录（书签落这里）。</summary>
    private string _bookDir = "";

    /// <summary>壳当前章的正文与标题（听书朗读用，壳位置变化时由视图推过来）。</summary>
    private string _readerText = "";
    private string _readerTitle = "";

    /// <summary>正在补抓的章序号（−1 = 空闲；防同一章重复抓）。</summary>
    private int _fetchingIndex = -1;

    private List<ReaderBookmark> _bookmarks = new();

    [ObservableProperty] private string _bookName = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _bookUrl = "";
    [ObservableProperty] private string _sourceName = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyPhase = "";
    [ObservableProperty] private string _statusText = "从书架或发现页打开一本书，或「📂 本地 TXT」直接读本地文件";
    [ObservableProperty] private string _errorText = "";

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

    /// <summary>听书进行中（正文下方控制栏的显示开关）。</summary>
    [ObservableProperty] private bool _isListening;

    /// <summary>听书已暂停（控制栏在「⏸ 暂停 / ▶ 继续」之间切换）。</summary>
    [ObservableProperty] private bool _isListenPaused;

    /// <summary>听书进度读数（「第 12/158 句」）。</summary>
    [ObservableProperty] private string _listenStatus = "";

    /// <summary>朗读语速（−10~10，与设置页同一份；听书中改会从当前句重起）。</summary>
    [ObservableProperty] private int _ttsRate = ReadingSettingsService.DefaultTtsRate;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public ReaderViewModel()
        : this(null!, null!, null!, null!, null!, new ReadingSession())
    { }

    public ReaderViewModel(
        SourceStore store, CrawlService crawl, ShelfStore shelf, ProgressStore progress,
        ReadingSettingsService? settings, ReadingSession session,
        INavigationService? nav = null, PrefetchService? prefetch = null, BookDownloadService? download = null,
        TtsService? tts = null, BookIndexWriter? books = null, TxtBookService? txt = null)
    {
        _store = store;
        _crawl = crawl;
        _shelf = shelf;
        _progress = progress;
        _settings = settings;
        _session = session;
        _nav = nav;
        _prefetch = prefetch;
        _download = download;
        _tts = tts;
        _books = books;
        _txt = txt;

        if (_tts is not null)
        {
            _tts.StateChanged += OnTtsStateChanged;
            _tts.SegmentStarted += OnTtsSegment;
        }

        if (settings is not null)
        {
            settings.Changed += OnSettingsChanged;
            TtsRate = settings.TtsRate;
        }
    }

    /// <summary>目录全量（换源 / 整本下载 / 进度映射都用它；目录 UI 在壳里）。</summary>
    public ObservableCollection<Chapter> Chapters { get; } = new();

    /// <summary>换源比对的候选源（界面绑这个）。</summary>
    public ObservableCollection<SwitchCandidate> SwitchCandidates { get; } = new();

    /// <summary>
    /// 书已就绪可以装进壳：参数 =（索引书 index.json 绝对路径, 续读章序号, 章内比例 0~1）。
    /// <para>🔴 标签页复用时 VM 不重建 ⇒ 每次打开书都会再发一次，视图收到就重新 LoadIndex。</para>
    /// </summary>
    public event Action<string, int, double>? BookReady;

    /// <summary>缺章补抓落盘完成（视图据此调壳的 <see cref="Controls.ReaderShell.ReloadChapter"/>）。</summary>
    public event Action? ChapterFetched;

    /// <summary>壳的排版参数就绪 / 被设置页改了（视图据此 ApplyTypography）。</summary>
    public event Action<ReaderTypography>? ShellTypographyReady;

    public event EventHandler? StatusChanged;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsNotBusy => !IsBusy;

    public bool HasBook => !string.IsNullOrWhiteSpace(BookUrl);

    public bool HasChapters => Chapters.Count > 0 || _localPath.Length > 0;

    public bool HasSwitchCandidates => SwitchCandidates.Count > 0;

    public bool IsLocalBook => _localPath.Length > 0;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnBookUrlChanged(string value) => OnPropertyChanged(nameof(HasBook));

    /// <summary>
    /// 设置在别处被改了（设置页 / 套用样式预设）⇒ 把排版重新下发给壳（⛔ 与壳回写共用 <c>_syncing</c> 防环）。
    /// </summary>
    private void OnSettingsChanged()
    {
        if (_syncing) return;
        ShellTypographyReady?.Invoke(BuildShellTypography());
    }

    partial void OnTtsRateChanged(int value)
    {
        if (_syncing) return;

        _settings?.SetTtsRate(value);

        // 听书中改语速：从当前句重起（SAPI 的 Rate 只在 Speak 前读，改不了正在读的那句）
        if (!IsListening || _tts is null) return;
        if (_ttsSentences.Count == 0) return;

        var error = _tts.Start(_ttsSentences, _tts.Index, value,
            _settings?.TtsVolume ?? ReadingSettingsService.DefaultTtsVolume, _settings?.TtsVoice ?? "");
        if (error.Length > 0) { StatusText = error; StopListen(); return; }

        IsListenPaused = false;
    }

    // ==================== 打开一本书 ====================

    /// <summary>
    /// 取走并打开「待读的书」。
    /// <para>🔴 标签页复用时 VM 不重建 ⇒ 每次页面重新挂载都要再查一次，否则第二次跳转没反应。</para>
    /// </summary>
    public void CheckPending()
    {
        if (_session.TakePendingBook() is { } pending) _ = OpenAsync(pending);
    }

    /// <summary>按 <see cref="PendingBook"/> 打开：详情 → 目录 → 落盘索引书 → 交给壳续读。</summary>
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

        StopListen();
        ErrorText = "";
        BookUrl = pending.BookUrl;
        BookName = pending.Name;
        Author = "";
        SourceName = source.Name;
        _sourceUrl = source.Url;
        _localPath = "";
        _bookDir = "";
        _bookmarks.Clear();
        Chapters.Clear();
        OnPropertyChanged(nameof(HasChapters));
        OnPropertyChanged(nameof(IsLocalBook));
        IsBusy = true;
        BusyPhase = "正在读目录…";

        try
        {
            var detail = await _crawl.DetailAsync(source, pending.BookUrl, CancellationToken.None).ConfigureAwait(true);
            if (detail.Value is not null)
            {
                if (!string.IsNullOrWhiteSpace(detail.Value.Name)) BookName = detail.Value.Name;
                Author = detail.Value.Author;
                SourceName = source.Name;
            }

            var tocUrl = string.IsNullOrWhiteSpace(detail.Value?.TocUrl) ? pending.BookUrl : detail.Value!.TocUrl;
            var chapters = await _crawl.ChaptersAsync(source, tocUrl, CancellationToken.None).ConfigureAwait(true);

            if (chapters.Value is not null)
                foreach (var chapter in chapters.Value) Chapters.Add(chapter);

            OnPropertyChanged(nameof(HasChapters));

            ErrorText = string.Join("\n", new[] { detail.Error, chapters.Error }
                .Where(e => e is not null).Select(e => e!.Display));

            if (Chapters.Count == 0)
            {
                StatusText = "⚠ 目录为空";
                return;
            }

            // 续读：换源带来的章节优先 → 否则进度表 → 否则第 1 章
            var saved = _settings is { RememberProgress: false } ? null : _progress?.Get(BookUrl);
            Chapter? resume = null;

            if (!string.IsNullOrWhiteSpace(_resumeChapterUrl))
            {
                resume = Chapters.FirstOrDefault(c => c.Url == _resumeChapterUrl);
                _resumeChapterUrl = "";
            }

            // 换源后章节地址必变 ⇒ 按章节名对齐（进度表里的地址同样只对原源有效）
            resume ??= MatchChapter(Chapters, _resumeChapterName);
            _resumeChapterName = "";

            if (resume is null && saved is not null)
                resume = Chapters.FirstOrDefault(c => c.Url == saved.ChapterUrl);

            resume ??= MatchChapter(Chapters, saved?.ChapterName);

            var resumeIndex = resume is null ? 0 : Math.Max(0, Chapters.IndexOf(resume));
            var ratio = resume is not null && saved is not null && resume.Url == saved.ChapterUrl
                ? saved.Offset
                : 0;

            StatusText = resume is null
                ? $"《{BookName}》共 {Chapters.Count} 章"
                : $"《{BookName}》续读：{resume.Name}";

            // 目录到手 ⇒ 落盘「索引书」（已缓存章直接落盘，⛔ 不联网），落完交给壳
            BusyPhase = "正在整理这本书的索引…";
            var url = BookUrl;
            var title = BookName;
            var author = Author;
            var src = _sourceUrl;
            var snapshot = Chapters.ToList();

            var (indexPath, exportError) = await Task.Run(() =>
            {
                string err = "书籍服务未初始化";
                var p = _books?.ExportBook(url, title, author, src, snapshot, out err);
                return (p, err);
            }).ConfigureAwait(true);

            if (indexPath is null)
            {
                ErrorText = exportError;
                StatusText = "⚠ " + exportError;
                return;
            }

            _bookDir = Path.GetDirectoryName(indexPath) ?? "";
            BookReady?.Invoke(indexPath, resumeIndex, ratio);
        }
        catch (Exception ex) { ErrorText = "打开失败：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }
    }

    // ==================== 壳对接（视图转发） ====================

    /// <summary>壳位置变了：存进度 + 给听书换文本。参数 =（章序号, 章内比例, 章标题）。</summary>
    public void SaveShellProgress(int chapterIndex, double ratio, string chapterTitle)
    {
        _readerTitle = chapterTitle;

        if (_settings is { RememberProgress: false }) return;
        if (_progress is null) return;

        if (_localPath.Length > 0)
        {
            _progress.Save(_localPath, "#" + chapterIndex, chapterTitle, ratio);
            return;
        }

        if (chapterIndex < 0 || chapterIndex >= Chapters.Count || string.IsNullOrWhiteSpace(BookUrl)) return;

        var chapter = Chapters[chapterIndex];
        if (string.IsNullOrWhiteSpace(chapter.Url)) return;

        _progress.Save(BookUrl, chapter.Url, chapter.Name, ratio);
    }

    /// <summary>壳把当前章正文推过来（听书朗读的就是它）。</summary>
    public void SetReaderText(string text, string title)
    {
        _readerText = text ?? "";
        _readerTitle = title ?? "";
    }

    /// <summary>当前要下发的壳排版（读 <see cref="ReadingSettingsService"/> 同一份）。</summary>
    public ReaderTypography BuildShellTypography() => _settings is null
        ? ReaderTypography.Default
        : new ReaderTypography
        {
            FontSize = _settings.FontSize,
            LineSpacing = _settings.LineSpacing,
            Padding = _settings.ReaderPadding,
            ParagraphSpacing = _settings.ParagraphSpacing > 0 ? 1 : 0,
            Indent = _settings.Indent > 0,
            FontFamily = _settings.FontFamily,
            Theme = _settings.ShellTheme,
        };

    /// <summary>壳的排版浮层改完了 → 写回设置（写盘触发 Changed ⇒ 自动重下发，<c>_syncing</c> 防环）。</summary>
    public void SaveShellTypography(ReaderTypography typography)
    {
        if (_settings is null || _syncing || typography is null) return;

        _syncing = true;
        try
        {
            _settings.SetFontSize((int)Math.Round(typography.FontSize));
            _settings.SetLineSpacing(typography.LineSpacing);
            _settings.SetReaderPadding((int)Math.Round(typography.Padding));
            _settings.SetIndent(typography.Indent ? 1 : 0);
            _settings.SetParagraphSpacing(typography.ParagraphSpacing > 0 ? 1 : 0);
            _settings.SetFontFamily(typography.FontFamily);
            _settings.SetShellTheme(typography.Theme);
        }
        catch (Exception ex) { StatusText = "⚠ 保存排版失败：" + ex.Message; }
        finally { _syncing = false; }
    }

    // ==================== 缺章补抓 ====================

    /// <summary>
    /// 壳翻到了文件缺失的章（网络书）：静默抓正文 → 落盘 → 通知视图重载当前章。
    /// 本地书文件必然存在，⛔ 走这里。
    /// </summary>
    public async Task FetchChapterAsync(int chapterIndex)
    {
        if (_localPath.Length > 0) return;
        if (_crawl is null || _books is null) return;
        if (chapterIndex < 0 || chapterIndex >= Chapters.Count) return;
        if (_fetchingIndex == chapterIndex) return;

        var chapter = Chapters[chapterIndex];
        if (string.IsNullOrWhiteSpace(chapter.Url)) return;

        var source = FindSource(FindSourceUrl());
        if (source is null) { StatusText = "⚠ 找不到这本书对应的书源，无法补抓本章。"; return; }

        _fetchingIndex = chapterIndex;
        StatusText = $"正在补抓：{chapter.Name}…";

        try
        {
            var content = await _crawl.ContentAsync(source, chapter.Url, CancellationToken.None).ConfigureAwait(true);

            if (content.Value is { Length: > 0 })
            {
                _books.WriteChapter(BookUrl, chapterIndex, content.Value);
                StatusText = $"已补抓：{chapter.Name}";
                ChapterFetched?.Invoke();
            }
            else
            {
                StatusText = "⚠ 本章补抓失败：" + (content.Error?.Display ?? "正文为空");
            }

            StartPrefetch(source, chapter);
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 本章补抓异常：" + ex.Message;
        }
        finally
        {
            _fetchingIndex = -1;
        }
    }

    // ==================== 书签（壳的增删跳都落到书目录 bookmarks.json） ====================

    /// <summary>当前书的全量书签（视图装进壳）。</summary>
    public IReadOnlyList<ReaderBookmark> LoadBookmarks() => _bookmarks;

    /// <summary>视图装完壳后调一次：从书目录把书签读进来。</summary>
    public void ReloadBookmarks()
    {
        _bookmarks = _bookDir.Length > 0
            ? _bookmarksStore.Load(_bookDir).ToList()
            : new List<ReaderBookmark>();
    }

    public void AddBookmark(ReaderBookmark bookmark)
    {
        _bookmarks.Add(bookmark);
        _bookmarksStore.Save(_bookDir, _bookmarks);
        StatusText = "已加书签：" + bookmark.ChapterTitle;
    }

    public void RemoveBookmark(string id)
    {
        _bookmarks.RemoveAll(b => string.Equals(b.Id, id, StringComparison.Ordinal));
        _bookmarksStore.Save(_bookDir, _bookmarks);
        StatusText = "已删除书签。";
    }

    // ==================== 本地 TXT ====================

    /// <summary>
    /// 「📂 本地 TXT」：读文件（BOM → 严格 UTF-8 → GB18030）→ 切章 → 落盘索引书 → 交给壳。
    /// <para>🔴 落盘根 = <c>store/reading/local/&lt;hash&gt;/</c>，hash 含大小与修改时间 ⇒ 文件没变就复用。</para>
    /// </summary>
    [RelayCommand]
    private async Task OpenLocalTxtAsync()
    {
        if (_txt is null || _books is null) { StatusText = "⚠ 导入服务未就绪。"; return; }

        var files = await DialogHooks.PickFilesAsync("打开本地 TXT", null, false, "*.txt").ConfigureAwait(true);
        if (files.Count == 0) { StatusText = "已取消打开本地文件"; return; }

        var path = files[0];
        StopListen();
        ErrorText = "";
        IsBusy = true;
        BusyPhase = "正在导入本地文件…";

        try
        {
            var (indexPath, exportError) = await Task.Run(() =>
            {
                var text = _txt.ReadText(path);
                var chapters = _txt.BuildChapters(text, null);
                var exported = _books.ExportLocalText(path, text, chapters, out var err);
                return (exported, err);
            }).ConfigureAwait(true);

            if (indexPath is null)
            {
                ErrorText = exportError;
                StatusText = "⚠ " + exportError;
                return;
            }

            _localPath = path;
            _bookDir = Path.GetDirectoryName(indexPath) ?? "";
            _bookmarks.Clear();
            BookUrl = path;
            BookName = Path.GetFileNameWithoutExtension(path);
            Author = "";
            SourceName = "本地文件";
            Chapters.Clear();
            OnPropertyChanged(nameof(HasChapters));
            OnPropertyChanged(nameof(IsLocalBook));

            // 续读：进度按绝对路径记，章节键 = #序号
            var saved = _settings is { RememberProgress: false } ? null : _progress?.Get(path);
            var resumeIndex = 0;
            var ratio = 0.0;
            if (saved is not null && saved.ChapterUrl.StartsWith("#", StringComparison.Ordinal)
                && int.TryParse(saved.ChapterUrl[1..], out var parsed))
            {
                resumeIndex = parsed;
                ratio = saved.Offset;
            }

            StatusText = $"《{BookName}》已打开";
            BookReady?.Invoke(indexPath, resumeIndex, ratio);
        }
        catch (Exception ex)
        {
            ErrorText = "导入失败：" + ex.Message;
            StatusText = "⚠ 导入失败";
        }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }
    }

    // ==================== 换源 ====================

    /// <summary>
    /// 「🔀 换源」：以当前书名并发搜所有可用源 → 按「书名匹配度 → 响应速度」排序 → 弹换源浮层，
    /// 选一个源后原地切源续读当前章（⛔ 不再跳到搜索页手动搜）。
    /// </summary>
    [RelayCommand]
    private async Task SwitchSourceAsync()
    {
        if (_localPath.Length > 0) { StatusText = "本地文件不支持换源。"; return; }
        if (string.IsNullOrWhiteSpace(BookName)) { StatusText = "先打开一本书。"; return; }
        if (_crawl is null || _store is null) { StatusText = "⚠ 换源服务未就绪。"; return; }

        // 找所有「可用 + 已启用」的源（当前源也参与，方便回到它）
        var sources = _store.LoadAll()
            .Where(s => s.Enabled && s.Capability == SourceCapability.Ready)
            .ToList();
        if (sources.Count == 0) { StatusText = "没有可用书源可换。"; return; }

        SwitchCandidates.Clear();
        OnPropertyChanged(nameof(HasSwitchCandidates));
        ShowSwitchPanel = true;
        IsSwitching = true;
        StatusText = $"正在用 {sources.Count} 个书源比对「{BookName}」…";

        var start = DateTimeOffset.UtcNow;
        try
        {
            var results = await _crawl.SearchManyAsync(sources, BookName, 1, CancellationToken.None).ConfigureAwait(true);

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
        catch (Exception ex) { StatusText = "⚠ 换源异常：" + ex.Message; }
        finally
        {
            IsSwitching = false;
        }
    }

    /// <summary>按章节名在新目录里找同一章（换源后地址必变，只能按名对齐；空白差异与「第N章 xxx」前缀差异都要容忍）。</summary>
    private static Chapter? MatchChapter(IEnumerable<Chapter> chapters, string? name)
    {
        var key = Normalize(name);
        if (key.Length == 0) return null;

        Chapter? contains = null;
        foreach (var chapter in chapters)
        {
            var candidate = Normalize(chapter.Name);
            if (candidate.Length == 0) continue;
            if (string.Equals(candidate, key, StringComparison.Ordinal)) return chapter;
            contains ??= candidate.Contains(key, StringComparison.Ordinal) || key.Contains(candidate, StringComparison.Ordinal)
                ? chapter
                : null;
        }

        return contains;
    }

    private static string Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));

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

        var oldBookUrl = BookUrl;
        var currentTitle = _readerTitle;

        // 书架 / 进度都以 book_url 为主键：换源 = 换地址 ⇒ 先重挂再重开，否则书架留旧行、续读丢失
        if (!string.IsNullOrWhiteSpace(oldBookUrl) &&
            !string.Equals(oldBookUrl, candidate.BookUrl, StringComparison.Ordinal))
        {
            _shelf?.SetBookIdentity(oldBookUrl, candidate.BookUrl, candidate.SourceUrl);
            _progress?.MoveTo(oldBookUrl, candidate.BookUrl);
        }

        _session.PendingBook = new PendingBook(candidate.SourceUrl, candidate.BookUrl, candidate.Name);
        StatusText = $"切换书源：{candidate.SourceName}";

        // 不同源的章节地址必然不同 ⇒ 记章节名对齐；同源重开仍可走地址精确命中
        _resumeChapterUrl = "";
        _resumeChapterName = currentTitle;
        _ = OpenAsync(new PendingBook(candidate.SourceUrl, candidate.BookUrl, candidate.Name));
    }

    /// <summary>换源后要优先续读的章节地址（保留字段，地址对齐目前只按名）。</summary>
    private string _resumeChapterUrl = "";

    /// <summary>换源后要优先续读的章节名（地址垮源失效，按名对齐）。</summary>
    private string _resumeChapterName = "";

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
        if (_localPath.Length > 0) { DownloadPhase = "本地文件不需要下载。"; return; }

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

            // 整本下载完 ⇒ 索引书补齐（全部章已进正文缓存，后台落盘不联网）
            ExportIndexInBackground();
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

    /// <summary>后台导「索引书」（index.json + 已缓存章落盘，⛔ 不联网、不阻塞阅读）。</summary>
    private void ExportIndexInBackground()
    {
        var books = _books;
        if (books is null || _localPath.Length > 0) return;

        var url = BookUrl;
        var title = BookName;
        var author = Author;
        var source = _sourceUrl;
        var snapshot = Chapters.ToList();
        _ = Task.Run(() => books.ExportBook(url, title, author, source, snapshot, out _));
    }

    /// <summary>加入书架（已在则刷新书名作者；本地文件暂不支持）。</summary>
    [RelayCommand]
    private void AddToShelf()
    {
        if (_localPath.Length > 0) { StatusText = "本地文件暂不支持加入书架。"; return; }
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

    // ==================== 预取 ====================

    /// <summary>
    /// 后台把后面几章抓进正文缓存（章数来自设置）。补抓完一章就重起一轮：⛔ 不要在旧队列上叠加，
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

    private string FindSourceUrl() => _sourceUrl;

    private BookSource? FindSource(string? url)
    {
        if (_store is null || string.IsNullOrWhiteSpace(url)) return null;
        return _store.LoadAll().FirstOrDefault(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    // ==================== 听书 ====================

    /// <summary>从当前章第一句开始朗读（顶栏「🎧 听书」）。</summary>
    [RelayCommand]
    private void StartListen()
    {
        if (_tts is null) { StatusText = "⚠ 听书服务未就绪。"; return; }
        if (_tts.IsSpeaking) return;

        var sentences = TtsService.SplitSentences(_readerText);
        if (sentences.Count == 0) { StatusText = "⚠ 当前没有可朗读的正文（先打开一本书）。"; return; }

        _ttsSentences = sentences;
        var error = _tts.Start(sentences, 0, TtsRate,
            _settings?.TtsVolume ?? ReadingSettingsService.DefaultTtsVolume, _settings?.TtsVoice ?? "");
        if (error.Length > 0) { StatusText = error; return; }

        IsListening = true;
        IsListenPaused = false;
        ListenStatus = $"第 1/{sentences.Count} 句";
        StatusText = $"听书中 · {_readerTitle}";
    }

    /// <summary>暂停 / 继续（在句子边界生效）。</summary>
    [RelayCommand]
    private void ToggleListenPause()
    {
        if (_tts is null || !_tts.IsSpeaking) return;

        if (_tts.IsPaused) { _tts.Resume(); IsListenPaused = false; }
        else { _tts.Pause(); IsListenPaused = true; }
    }

    /// <summary>停止朗读（当前句读完即止）。</summary>
    [RelayCommand]
    private void StopListen()
    {
        var wasListening = IsListening;

        _tts?.Stop();
        IsListening = false;
        IsListenPaused = false;
        ListenStatus = "";

        if (wasListening) StatusText = $"{_readerTitle} · 已停止听书";
    }

    [RelayCommand]
    private void TtsFaster() => TtsRate = Math.Min(TtsRate + 1, ReadingSettingsService.MaxTtsRate);

    [RelayCommand]
    private void TtsSlower() => TtsRate = Math.Max(TtsRate - 1, ReadingSettingsService.MinTtsRate);

    /// <summary>当前章的朗读句子（语速变了要从当前句重起，所以留一份）。</summary>
    private IReadOnlyList<string> _ttsSentences = Array.Empty<string>();

    /// <summary>换句通知在朗读线程上发 ⇒ 一律切回 UI 线程再改属性。</summary>
    private void OnTtsSegment(object? sender, TtsProgress progress)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsListening) return;
            ListenStatus = $"第 {progress.Index + 1}/{progress.Count} 句";
        });
    }

    /// <summary>朗读结束 / 失败：收起控制栏；有失败原因就报到状态栏。</summary>
    private void OnTtsStateChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_tts is null || _tts.IsSpeaking || !IsListening) return;

            IsListening = false;
            IsListenPaused = false;
            ListenStatus = "";
            StatusText = _tts.UnavailableReason.Length > 0
                ? "⚠ " + _tts.UnavailableReason
                : $"{_readerTitle} · 听书结束";
        });
    }

    public void Dispose()
    {
        try { _prefetchCts?.Cancel(); } catch { /* 已结束 */ }
        try { _downloadCts?.Cancel(); } catch { /* 已结束 */ }
        _prefetchCts?.Dispose();
        _downloadCts?.Dispose();

        if (_tts is not null)
        {
            _tts.StateChanged -= OnTtsStateChanged;
            _tts.SegmentStarted -= OnTtsSegment;
            _tts.Stop();
        }

        if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        BookReady = null;
        ChapterFetched = null;
        ShellTypographyReady = null;
        StatusChanged = null;
    }
}
