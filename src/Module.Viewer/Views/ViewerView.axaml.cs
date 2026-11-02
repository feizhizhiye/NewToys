using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NewToys.UI.Readers;
using NewToys.UI.Viewers;
using NewToys.Viewer.Services;
using NewToys.Viewer.ViewModels;

namespace NewToys.Viewer.Views;

/// <summary>
/// 查看器页。控件操作与文件选择都在本类（VM ⛔ 不碰 StorageProvider、⛔ 不 new 控件）。
/// </summary>
public partial class ViewerView : UserControl
{
    private ViewerViewModel? _vm;

    private string _archivePath = "";
    private IReadOnlyList<ArchiveImageEntry> _archiveEntries = Array.Empty<ArchiveImageEntry>();
    private int _archiveIndex;
    private bool _suppress;

    private string _readerPath = "";
    private List<ReaderBookmark> _bookmarks = new();
    private ReaderShell? _wiredReader;

    public ViewerView()
    {
        InitializeComponent();
        Focusable = true;

        // 拖放：把文件直接拖进来
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // 页面卸载 / 切走时把进度存下来
        DetachedFromVisualTree += (_, _) => SaveProgress();
    }

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

        Host.SetHint("支持：文本（txt / md 带目录与书签）、源码、图片、压缩包内图片（zip / rar / 7z）、音频（本地播放器）。" +
                     "视频走系统默认播放器。");
    }

    // ==================== 打开 ====================

    private async void OnOpenRequested(object? sender, string? path)
    {
        if (_vm is null) return;

        if (!string.IsNullOrEmpty(path))
        {
            Open(path);
            return;
        }

        var paths = await PickFilesAsync();
        if (paths.Count == 0) return;

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

        if (!Host.Open(path, _vm.GetProgress(path), out var error, () => _vm.Playback))
        {
            _vm.OnOpenFailed(error);
            return;
        }

        _archiveEntries = Array.Empty<ArchiveImageEntry>();
        _vm.ClearArchive();
        _vm.OnOpened(path, Host.Kind);
        ApplyTypography();

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

        _archiveEntries = Array.Empty<ArchiveImageEntry>();
        _vm.ClearArchive();
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

    // ==================== 压缩包 ====================

    private void OpenArchive(string path)
    {
        if (_vm is null) return;

        var entries = _vm.Archive.List(path, out var error);
        if (entries.Count == 0)
        {
            _vm.OnOpenFailed(string.IsNullOrWhiteSpace(error) ? "压缩包里没有图片。" : error);
            return;
        }

        _archivePath = path;
        _archiveEntries = entries;
        _archiveIndex = 0;

        _vm.SetArchiveEntries(entries.Select(e => e.Name).ToList(), 0);
        ShowArchiveImage(0);

        _vm.OnOpened(path, ViewerKind.Image, "压缩包 · " + entries.Count + " 张");
        Focus();
    }

    private void ShowArchiveImage(int index)
    {
        if (_vm is null || _archiveEntries.Count == 0) return;

        var target = Math.Clamp(index, 0, _archiveEntries.Count - 1);
        var entry = _archiveEntries[target];

        using var stream = _vm.Archive.Open(_archivePath, entry.Key, out var error);
        if (stream is null)
        {
            _vm.SetStatus("⚠ " + error);
            return;
        }

        if (!Host.OpenImageStream(stream, _archivePath + "!" + entry.Key, out error))
        {
            _vm.SetStatus("⚠ 打不开这张图：" + error);
            return;
        }

        _archiveIndex = target;

        _suppress = true;
        try
        {
            ArchiveList.SelectedIndex = target;
            if (ArchiveList.SelectedItem is { } selected) ArchiveList.ScrollIntoView(selected);
        }
        finally { _suppress = false; }

        _vm.UpdateArchiveIndex(target);
        _vm.SetInfo("缩放 " + (Host.Zoom * 100).ToString("0") + "%");
    }

    private void OnArchiveSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var index = ArchiveList.SelectedIndex;
        if (index >= 0 && index != _archiveIndex) ShowArchiveImage(index);
    }

    private void OnArchiveNavigate(object? sender, int delta)
    {
        if (_archiveEntries.Count == 0) return;
        ShowArchiveImage(_archiveIndex + delta);
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

        _archiveEntries = Array.Empty<ArchiveImageEntry>();
        _vm.ClearArchive();
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
        _archiveEntries = Array.Empty<ArchiveImageEntry>();
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

    private void OnCloseRules(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RulePanel.IsVisible = false;

    private void OnAddRule(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    private void OnRemoveRule(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.RemoveRule(RuleList.SelectedItem as TxtChapterRule);
        TestResult.Text = "已删除。";
    }

    private void OnTestRules(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

        if (paths.Count > 1 && paths.All(ViewerHost.IsAudio)) OpenAudioFiles(paths);
        else Open(paths[0]);
    }

    private void OnRecentSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem is not string path) return;
        RecentList.SelectedItem = null;   // 允许重复点同一条
        Open(path);
    }

    // ==================== 键盘 ====================

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_archiveEntries.Count > 0 && !RulePanel.IsVisible)
        {
            if (e.Key == Key.Left) { e.Handled = true; ShowArchiveImage(_archiveIndex - 1); return; }
            if (e.Key == Key.Right) { e.Handled = true; ShowArchiveImage(_archiveIndex + 1); return; }
        }

        base.OnKeyDown(e);
    }
}
