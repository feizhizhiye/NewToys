using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NewToys.UI.Readers;
using NewToys.UI.Viewers;
using NewToys.Viewer.Services;
using NewToys.Viewer.ViewModels;

namespace NewToys.Viewer.Views;

/// <summary>
/// 查看器页的公共逻辑（文本 / 图片 / 音频三页共用一份）：控件操作与文件选择都在这里，
/// VM ⛔ 不碰 StorageProvider、⛔ 不 new 控件。
/// <para>
/// 🔴 子类 XAML 必须给这些元件起同名：<c>Host</c> / <c>RecentList</c> / <c>ArchiveList</c> /
/// <c>RulePanel</c> / <c>RuleList</c> / <c>RuleNameBox</c> / <c>RulePatternBox</c> / <c>SampleBox</c> /
/// <c>TestResult</c>。基类按名字取控件（XAML 生成的字段在子类里，基类拿不到）。
/// </para>
/// <para>
/// 🔴 本类不做范围判断 —— 收不收这个文件由 <see cref="ViewerViewModel.TryRouteElsewhere"/> 决定，
/// 不属于本页的文件会转去目标页。
/// </para>
/// </summary>
public abstract class ViewerPageBase : UserControl
{
    private ViewerViewModel? _vm;

    private ArchiveImageSession? _session;                                    // 压缩包会话（zip 常开）
    private IReadOnlyList<string> _siblings = Array.Empty<string>();          // 同目录图片
    private int _entryIndex;                                                  // 当前第几张（两种来源共用）
    private int _navToken;                                                    // 丢弃过期的异步翻页结果
    private bool _suppress;

    private string _readerPath = "";
    private List<ReaderBookmark> _bookmarks = new();
    private ReaderShell? _wiredReader;

    private ViewerHost? _host;
    private ListBox? _recentList;
    private ListBox? _archiveList;
    private Border? _rulePanel;
    private ListBox? _ruleList;
    private TextBox? _ruleNameBox;
    private TextBox? _rulePatternBox;
    private TextBox? _sampleBox;
    private TextBlock? _testResult;

    protected ViewerPageBase()
    {
        // 拖放：把文件直接拖进来
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // 页面卸载 / 切走时把进度存下来
        DetachedFromVisualTree += (_, _) => SaveProgress();
    }

    private ViewerHost Host => _host ??= Find<ViewerHost>("Host");

    private ListBox RecentList => _recentList ??= Find<ListBox>("RecentList");

    private ListBox ArchiveList => _archiveList ??= Find<ListBox>("ArchiveList");

    private Border RulePanel => _rulePanel ??= Find<Border>("RulePanel");

    private ListBox RuleList => _ruleList ??= Find<ListBox>("RuleList");

    private TextBox RuleNameBox => _ruleNameBox ??= Find<TextBox>("RuleNameBox");

    private TextBox RulePatternBox => _rulePatternBox ??= Find<TextBox>("RulePatternBox");

    private TextBox SampleBox => _sampleBox ??= Find<TextBox>("SampleBox");

    private TextBlock TestResult => _testResult ??= Find<TextBlock>("TestResult");

    private T Find<T>(string name) where T : Control
        => this.FindControl<T>(name)
           ?? throw new InvalidOperationException($"ViewerPage 的 XAML 缺少名为 {name} 的 {typeof(T).Name}。");

    /// <summary>子类构造函数在 <c>InitializeComponent()</c> 之后调用。</summary>
    protected void InitPage() => Focusable = true;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.OpenRequested -= OnOpenRequested;
            _vm.CloseRequested -= OnCloseRequested;
            _vm.ZoomInRequested -= OnZoomIn;
            _vm.ZoomOutRequested -= OnZoomOut;
            _vm.FitRequested -= OnFit;
            _vm.TypographyChanged -= OnTypographyChanged;
            _vm.ArchiveNavigateRequested -= OnArchiveNavigate;
            _vm.PlayToggleRequested -= OnPlayToggle;
            _vm.RulesEditRequested -= OnRulesEdit;
            _vm.ReaderOptionsChanged -= OnReaderOptionsChanged;
        }

        _vm = DataContext as ViewerViewModel;
        if (_vm is null) return;

        _vm.OpenRequested += OnOpenRequested;
        _vm.CloseRequested += OnCloseRequested;
        _vm.ZoomInRequested += OnZoomIn;
        _vm.ZoomOutRequested += OnZoomOut;
        _vm.FitRequested += OnFit;
        _vm.TypographyChanged += OnTypographyChanged;
        _vm.ArchiveNavigateRequested += OnArchiveNavigate;
        _vm.PlayToggleRequested += OnPlayToggle;
        _vm.RulesEditRequested += OnRulesEdit;
        _vm.ReaderOptionsChanged += OnReaderOptionsChanged;

        RuleList.ItemsSource = _vm.CustomRules;
        Host.SetHint(_vm.ScopeHint);

        // 本页是被跳转过来的：投递的文件已经躺在 VM 里，等页面挂上树再打开（控件此时才算就绪）。
        _vm.AttachView();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_vm is null) return;

        var pending = _vm.TakePending();
        if (pending.Count > 0) OpenMany(pending);
    }

    // ==================== 打开 ====================

    private async void OnOpenRequested(object? sender, IReadOnlyList<string>? paths)
    {
        if (_vm is null) return;

        if (paths is null)
        {
            paths = await PickFilesAsync();
            if (paths.Count == 0) return;
        }

        OpenMany(paths);
    }

    /// <summary>打开一批文件：不属于本页的转去对应页，多首音频进播放列表。</summary>
    private void OpenMany(IReadOnlyList<string> paths)
    {
        if (_vm is null || paths.Count == 0) return;
        if (_vm.TryRouteElsewhere(paths)) return;

        if (paths.Count > 1 && paths.All(ViewerHost.IsAudio)) OpenAudioFiles(paths);
        else Open(paths[0]);
    }

    /// <summary>打开一个文件（拖放 / 最近打开 / 选择器都汇到这里）。</summary>
    private void Open(string path)
    {
        if (_vm is null) return;

        SaveProgress();   // 换文件前先存上一个的进度

        if (ViewerHost.IsArchive(path)) { OpenArchive(path); return; }

        var kind = ViewerHost.KindOf(path);

        if (kind == ViewerKind.Reader) { OpenReaderFile(path); return; }
        if (kind == ViewerKind.Audio) { OpenAudioFiles(new[] { path }); return; }

        IReadOnlyList<string> siblings = Array.Empty<string>();
        var index = 0;
        if (kind == ViewerKind.Image) siblings = LoadSiblings(path, out index);

        if (!Host.Open(path, _vm.GetProgress(path), out var error, () => _vm.Playback))
        {
            _vm.OnOpenFailed(error);
            return;
        }

        ResetEntries();
        _siblings = siblings;
        _entryIndex = index;
        _vm.OnOpened(path, Host.Kind);
        ApplyTypography();
        UpdateEntryList(index);

        if (Host.Kind == ViewerKind.Image)
            _vm.SetInfo("缩放 " + (Host.Zoom * 100).ToString("0") + "%");
    }

    private void OpenReaderFile(string path)
    {
        if (_vm is null) return;

        string text;
        try { text = _vm.Txt.ReadText(path); }
        catch (Exception ex) { _vm.OnOpenFailed("读取失败：" + ex.Message); return; }

        var chapters = _vm.Txt.BuildChapters(text, _vm.CustomRules.ToList());
        var state = _vm.GetReaderPosition(path) ?? new ViewerReaderState(0, 0);

        if (!Host.OpenReader(path, text, chapters, state, out var error))
        {
            _vm.OnOpenFailed(error);
            return;
        }

        ResetEntries();
        _readerPath = path;
        _bookmarks = _vm.LoadBookmarks(path).ToList();

        WireReader();

        _vm.OnOpened(path, ViewerKind.Reader, chapters.Count + " 章");
        ApplyTypography();
        Host.ApplyReaderTypography(_vm.ReaderOptions);
    }

    private void WireReader()
    {
        var reader = Host.Reader;
        if (reader is null) return;

        if (_wiredReader is not null)
        {
            _wiredReader.PositionChanged -= OnReaderPositionChanged;
            _wiredReader.TypographyChanged -= OnReaderTypographyChanged;
            _wiredReader.AddBookmarkRequested -= OnAddBookmark;
            _wiredReader.RemoveBookmarkRequested -= OnRemoveBookmark;
            _wiredReader.BookmarkActivated -= OnBookmarkActivated;
            _wiredReader.StatusChanged -= OnReaderStatus;
        }

        reader.PositionChanged += OnReaderPositionChanged;
        reader.TypographyChanged += OnReaderTypographyChanged;
        reader.AddBookmarkRequested += OnAddBookmark;
        reader.RemoveBookmarkRequested += OnRemoveBookmark;
        reader.BookmarkActivated += OnBookmarkActivated;
        reader.StatusChanged += OnReaderStatus;

        reader.SetBookmarks(_bookmarks);
        _wiredReader = reader;
    }

    // ==================== 阅读器事件 ====================

    private void OnReaderPositionChanged(ReaderPosition position)
    {
        if (_vm is null || string.IsNullOrEmpty(_readerPath)) return;
        try { _vm.SaveReaderPosition(_readerPath, new ViewerReaderState(position.ChapterIndex, position.Ratio)); }
        catch { /* 进度是附属产物 */ }
    }

    private void OnReaderTypographyChanged(ReaderTypography typography)
    {
        if (_vm is null) return;
        _vm.SaveReaderOptions(typography);
    }

    private void OnReaderStatus(string text) => _vm?.SetStatus(text);

    private void OnAddBookmark()
    {
        if (_vm is null || Host.Reader is not { } reader) return;

        var bookmark = reader.CreateBookmarkCore();
        _bookmarks.Add(bookmark);
        _vm.SaveBookmarks(_readerPath, _bookmarks);
        reader.SetBookmarks(_bookmarks);
        _vm.SetStatus("已加书签：" + bookmark.ChapterTitle);
    }

    private void OnRemoveBookmark(string id)
    {
        if (_vm is null || Host.Reader is not { } reader) return;

        _bookmarks.RemoveAll(b => string.Equals(b.Id, id, StringComparison.Ordinal));
        _vm.SaveBookmarks(_readerPath, _bookmarks);
        reader.SetBookmarks(_bookmarks);
        _vm.SetStatus("已删除书签。");
    }

    private void OnBookmarkActivated(ReaderBookmark bookmark)
    {
        Host.Reader?.ShowChapter(bookmark.ChapterIndex, bookmark.Ratio);
    }

    // ==================== 图片翻页（压缩包 / 同目录） ====================

    /// <summary>可翻页条目数（压缩包条目或同目录图片）。</summary>
    private int EntryCount => _session?.Entries.Count ?? _siblings.Count;

    /// <summary>清空翻页来源（换文件 / 关闭时）。会话句柄一并释放。</summary>
    private void ResetEntries()
    {
        _session?.Dispose();
        _session = null;
        _siblings = Array.Empty<string>();
        _entryIndex = 0;
        _navToken++;
        _vm?.ClearArchive();
    }

    /// <summary>同目录图片（按文件名排序），并给回当前文件在其中的序号。</summary>
    private static IReadOnlyList<string> LoadSiblings(string path, out int index)
    {
        index = 0;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return new[] { path };

            var list = System.IO.Directory.EnumerateFiles(dir)
                .Where(ViewerHost.IsImage)
                .OrderBy(p => System.IO.Path.GetFileName(p) ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();

            var self = list.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (self < 0)
            {
                list.Add(path);
                self = list.Count - 1;
            }

            index = self;
            return list;
        }
        catch
        {
            return new[] { path };   // 目录读不了就只管这一张
        }
    }

    /// <summary>把当前翻页来源铺到顶部条目条，并选中第 <paramref name="index"/> 张。</summary>
    private void UpdateEntryList(int index)
    {
        if (_vm is null) return;

        var names = _session is not null
            ? _session.Entries.Select(e => e.Name).ToList()
            : _siblings.Select(p => System.IO.Path.GetFileName(p) ?? "").ToList();

        if (names.Count <= 1)
        {
            _vm.ClearArchive();   // 只有一张就不占顶部条带
            return;
        }

        _suppress = true;
        try
        {
            _vm.SetArchiveEntries(names, index);
            ArchiveList.SelectedIndex = index;
            if (ArchiveList.SelectedItem is { } selected) ArchiveList.ScrollIntoView(selected);
        }
        finally { _suppress = false; }
    }

    private void OpenArchive(string path)
    {
        if (_vm is null) return;

        ResetEntries();
        _vm.SetStatus("正在读取压缩包…");

        var archive = _vm.Archive;
        _ = ReadArchiveAsync(archive, path);
    }

    /// <summary>后台读包（zip 只读中央目录；大包 ⛔ 不要压在主线程）。</summary>
    private async Task ReadArchiveAsync(ArchiveImageService archive, string path)
    {
        var token = _navToken;

        var (session, error) = await Task.Run(() =>
        {
            var opened = archive.OpenSession(path, out var reason);
            return (opened, reason);
        }).ConfigureAwait(true);

        if (_vm is null || token != _navToken) { session?.Dispose(); return; }

        if (session is null)
        {
            _vm.OnOpenFailed(string.IsNullOrWhiteSpace(error) ? "读取压缩包失败。" : error);
            return;
        }

        if (session.Entries.Count == 0)
        {
            session.Dispose();
            _vm.OnOpenFailed("压缩包里没有图片。");
            return;
        }

        _session = session;
        _entryIndex = 0;

        _vm.OnOpened(path, ViewerKind.Image, "压缩包 · " + session.Entries.Count + " 张");
        UpdateEntryList(0);
        Focus();

        ShowEntry(0);
    }

    /// <summary>切到第 <paramref name="index"/> 张（按当前来源分派到压缩包 / 同目录）。</summary>
    private void ShowEntry(int index)
    {
        if (_vm is null || EntryCount == 0) return;

        var target = Math.Clamp(index, 0, EntryCount - 1);
        if (_session is not null) _ = ShowArchiveEntryAsync(_session, target);
        else ShowSibling(target);
    }

    /// <summary>压缩包内取图：解压放后台，解码仍走 UI 线程（<see cref="ViewerHost.OpenImageStream"/>）。</summary>
    private async Task ShowArchiveEntryAsync(ArchiveImageSession session, int index)
    {
        if (_vm is null) return;

        var token = ++_navToken;
        var entry = session.Entries[index];

        var (stream, error) = await Task.Run(() =>
        {
            var opened = session.Open(entry.Key, out var reason);
            return (opened, reason);
        }).ConfigureAwait(true);

        if (_vm is null) { stream?.Dispose(); return; }
        if (token != _navToken) { stream?.Dispose(); return; }   // 连按 → 只认最新一次

        if (stream is null)
        {
            _vm.SetStatus("⚠ " + error);
            return;
        }

        using (stream)
        {
            if (!Host.OpenImageStream(stream, session.Path + "!" + entry.Key, out var loadError))
            {
                _vm.SetStatus("⚠ 打不开这张图：" + loadError);
                return;
            }
        }

        _vm.OnImageNavigated(session.Path + "!" + entry.Key);
        ApplyEntryIndex(index);
    }

    /// <summary>同目录图片：直接按路径打开（宿主内部复用/重建渲染器）。</summary>
    private void ShowSibling(int index)
    {
        if (_vm is null || _siblings.Count == 0) return;

        var path = _siblings[index];
        if (!Host.Open(path, 0, out var error, () => _vm.Playback))
        {
            _vm.SetStatus("⚠ 打不开这张图：" + error);
            return;
        }

        _vm.OnImageNavigated(path);
        ApplyEntryIndex(index);
    }

    private void ApplyEntryIndex(int index)
    {
        _entryIndex = index;

        _suppress = true;
        try
        {
            ArchiveList.SelectedIndex = index;
            if (ArchiveList.SelectedItem is { } selected) ArchiveList.ScrollIntoView(selected);
        }
        finally { _suppress = false; }

        _vm?.UpdateArchiveIndex(index);
        _vm?.SetInfo("缩放 " + (Host.Zoom * 100).ToString("0") + "%");
    }

    protected void OnArchiveSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var index = ArchiveList.SelectedIndex;
        if (index >= 0 && index != _entryIndex) ShowEntry(index);
    }

    private void OnArchiveNavigate(object? sender, int delta)
    {
        if (EntryCount == 0) return;
        ShowEntry(_entryIndex + delta);
    }

    // ==================== 音频 ====================

    private void OpenAudioFiles(IReadOnlyList<string> paths)
    {
        if (_vm is null || paths.Count == 0) return;

        if (!Host.Open(paths[0], 0, out var error, () => _vm.Playback))
        {
            _vm.OnOpenFailed(error);
            return;
        }

        var player = Host.Audio;
        if (player is null)
        {
            _vm.OnOpenFailed("音频控件未就绪。");
            return;
        }

        player.TrackChanged -= OnAudioTrackChanged;
        player.StatusChanged -= OnAudioStatus;
        player.TrackChanged += OnAudioTrackChanged;
        player.StatusChanged += OnAudioStatus;

        _vm.Playback.PlaybackEnded -= OnPlaybackEnded;
        _vm.Playback.PlaybackEnded += OnPlaybackEnded;

        var tracks = _vm.AudioLibrary.ReadMany(paths);
        player.SetVolume(_vm.AudioVolume);
        player.SetPlaylist(tracks, 0);

        ResetEntries();
        _vm.OnOpened(paths[0], ViewerKind.Audio, tracks.Count + " 首");
    }

    private void OnAudioTrackChanged(AudioTrackItem? track)
    {
        if (track is null) return;
        _vm?.SetInfo(track.Display);
    }

    private void OnAudioStatus(string text) => _vm?.SetStatus(text);

    private void OnPlaybackEnded(object? sender, EventArgs e) => Host.Audio?.HandlePlaybackEnded();

    private void OnPlayToggle(object? sender, EventArgs e) => Host.Audio?.TogglePlay();

    // ==================== 关闭 / 缩放 / 排版 ====================

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        SaveProgress();
        Host.Close();
        ResetEntries();
        _bookmarks.Clear();
        _wiredReader = null;
        _vm?.ClearArchive();
        _vm?.OnClosed();
    }

    private void OnZoomIn(object? sender, EventArgs e)
    {
        Host.ZoomBy(1.15);
        UpdateZoomInfo();
    }

    private void OnZoomOut(object? sender, EventArgs e)
    {
        Host.ZoomBy(1 / 1.15);
        UpdateZoomInfo();
    }

    private void OnFit(object? sender, EventArgs e)
    {
        Host.FitToWindow();
        UpdateZoomInfo();
    }

    private void UpdateZoomInfo()
    {
        if (_vm is null || Host.Kind != ViewerKind.Image) return;
        _vm.SetInfo("缩放 " + (Host.Zoom * 100).ToString("0") + "%（双击图片切 1:1 / 适应）");
    }

    private void OnTypographyChanged(object? sender, EventArgs e) => ApplyTypography();

    private void OnReaderOptionsChanged(object? sender, ReaderTypography typography)
    {
        Host.ApplyReaderTypography(typography);
    }

    private void ApplyTypography()
    {
        if (_vm is null) return;
        Host.ApplyTextOptions(_vm.FontSize, _vm.LineSpacing, _vm.PagePadding);
    }

    // ==================== 进度 ====================

    private void SaveProgress()
    {
        if (_vm is null || string.IsNullOrEmpty(Host.CurrentPath)) return;

        try
        {
            if (Host.Kind == ViewerKind.Text)
                _vm.SaveProgress(Host.CurrentPath, Host.ReadProgress);
            else if (Host.Kind == ViewerKind.Reader && Host.ReaderState is { } state)
                _vm.SaveReaderPosition(Host.CurrentPath, state);
        }
        catch { /* 进度是附属产物，写不进去不影响阅读 */ }
    }

    // ==================== 章节规则浮层 ====================

    private void OnRulesEdit(object? sender, EventArgs e)
    {
        RulePanel.IsVisible = !RulePanel.IsVisible;
        if (RulePanel.IsVisible) RulePanel.ZIndex = 10;
    }

    protected void OnCloseRules(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RulePanel.IsVisible = false;

    protected void OnAddRule(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;

        if (!_vm.AddRule(RuleNameBox.Text ?? "", RulePatternBox.Text ?? "", out var error))
        {
            TestResult.Text = "⚠ " + error;
            return;
        }

        RuleNameBox.Text = "";
        RulePatternBox.Text = "";
        TestResult.Text = "已保存。下次打开 txt 时生效。";
    }

    protected void OnRemoveRule(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.RemoveRule(RuleList.SelectedItem as TxtChapterRule);
        TestResult.Text = "已删除。";
    }

    protected void OnTestRules(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;

        var hits = _vm.TestRule(SampleBox.Text ?? "", RulePatternBox.Text ?? "");
        TestResult.Text = hits.Count == 0
            ? "这段样本里没有命中。"
            : "命中 " + hits.Count + " 行：\n" + string.Join("\n", hits);
    }

    // ==================== 文件选择 / 拖放 ====================

    private async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) return Array.Empty<string>();

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要查看的文件",
                AllowMultiple = true,
            });

            return files.Count == 0
                ? Array.Empty<string>()
                : files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Avalonia 12：拖放数据走 DataTransfer + DataFormat（旧的 DataFormats.Files 已过时）
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        var paths = e.DataTransfer.TryGetFiles()?
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        if (paths is null || paths.Count == 0) return;
        OpenMany(paths);
    }

    protected void OnRecentSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem is not string path) return;
        RecentList.SelectedItem = null;   // 允许重复点同一条
        OpenMany(new[] { path });
    }

    // ==================== 键盘 ====================

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (EntryCount > 1 && !RulePanel.IsVisible)
        {
            if (e.Key == Key.Left) { e.Handled = true; ShowEntry(_entryIndex - 1); return; }
            if (e.Key == Key.Right) { e.Handled = true; ShowEntry(_entryIndex + 1); return; }
        }

        base.OnKeyDown(e);
    }
}
