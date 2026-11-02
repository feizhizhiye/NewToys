using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private long _lastWheelTick;                                              // 滚轮翻页节流（触控板防连翻）
    private bool _suppress;

    private string _readerPath = "";
    private List<ReaderBookmark> _bookmarks = new();
    private ReaderShell? _wiredReader;

    private ViewerHost? _host;
    private Grid? _splitGrid;
    private ListBox? _recentList;
    private ListBox? _archiveList;
    private Border? _rulePanel;
    private ListBox? _ruleList;
    private TextBox? _ruleNameBox;
    private TextBox? _rulePatternBox;
    private TextBox? _sampleBox;
    private TextBlock? _testResult;

    private readonly List<Bitmap> _splitBitmaps = new();
    private bool _splitActive;        // 网格当前是否真的在显示（区分「VM 想开」与「已铺好」）
    private int _splitFocus = -1;     // 网格里被点选的格（全局序号）
    private int _splitToken;          // 丢弃过期的网格装载结果

    protected ViewerPageBase()
    {
        // 拖放：把文件直接拖进来
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // 图片页滚轮翻页：走隧道阶段抢在 ScrollViewer 之前拦下
        AddHandler(InputElement.PointerWheelChangedEvent, OnWheelNavigate, RoutingStrategies.Tunnel);

        // 页面卸载 / 切走时把进度存下来
        DetachedFromVisualTree += (_, _) => SaveProgress();
    }

    private ViewerHost Host => _host ??= Find<ViewerHost>("Host");

    private Grid SplitGrid => _splitGrid ??= Find<Grid>("SplitGrid");

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
            _vm.SplitChanged -= OnSplitChanged;
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
        _vm.SplitChanged += OnSplitChanged;

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

        ApplySplitState();   // 分屏开着换了新图 → 重铺一屏
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

        // 分屏已开但网格还没铺（从单图切到压缩包）→ 补铺一屏
        if (_vm is { IsSplit: true, IsImage: true } && !_splitActive) ApplySplitState();
    }

    /// <summary>切到第 <paramref name="index"/> 张（按当前来源分派到压缩包 / 同目录）。</summary>
    private void ShowEntry(int index)
    {
        if (_vm is null || EntryCount == 0) return;

        var target = Math.Clamp(index, 0, EntryCount - 1);

        if (_vm.IsSplit && _vm.IsImage)
        {
            _ = RefreshSplitAsync(target);
            return;
        }

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
            var display = session.Path + "!" + entry.Key;
            if (!Host.OpenImageStream(stream, display, out var loadError, entry.Size, session.Path))
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
        if (_vm is { IsSplit: true, IsImage: true }) { GoToScreen(delta); return; }
        ShowEntry(_entryIndex + delta);
    }

    /// <summary>
    /// 滚轮翻页（图片页）：无 Ctrl 且未放大时滚轮 = 上一张 / 下一张。
    /// <para>🔴 隧道阶段拦截并 <c>Handled</c>，否则 <see cref="ScrollViewer"/> 会先把滚轮吃掉。</para>
    /// <para>🔴 放大后（Zoom &gt; 1）放弃拦截 —— 此时用户要的是滚动查看局部，不是翻页。</para>
    /// </summary>
    private void OnWheelNavigate(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null || EntryCount < 2) return;
        if (!_vm.IsSplit && Host.Kind != ViewerKind.Image) return;   // 分屏不经过宿主，别用它判类型
        if (!_vm.IsSplit && Host.IsInfoVisible) return;             // 信息卡片开着 → 不翻页
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;   // Ctrl + 滚轮 = 缩放
        if (!_vm.IsSplit && Host.Zoom > 1.01) return;               // 放大后要的是滚动查看局部；分屏不看缩放
        if (Math.Abs(e.Delta.Y) < 0.01) return;

        var now = Environment.TickCount64;
        if (now - _lastWheelTick < 180) { e.Handled = true; return; }   // 触控板一次滑动只算一格
        _lastWheelTick = now;

        e.Handled = true;

        var step = e.Delta.Y > 0 ? -1 : 1;
        if (_vm.IsSplit) GoToScreen(step);
        else ShowEntry(_entryIndex + step);
    }

    // ==================== 分屏网格（一屏看多张） ====================

    private void OnSplitChanged(object? sender, EventArgs e) => ApplySplitState();

    /// <summary>按 VM 的开关状态切换「单图宿主 ↔ 网格」，退出分屏时把当前张重新装回宿主。</summary>
    private void ApplySplitState()
    {
        if (_vm is null) return;

        if (_vm.IsSplit && _vm.IsImage && EntryCount > 0)
        {
            _splitActive = true;
            SplitGrid.IsVisible = true;
            Host.IsVisible = false;
            _ = RefreshSplitAsync(_entryIndex);
            return;
        }

        var wasActive = _splitActive;
        _splitActive = false;
        SplitGrid.IsVisible = false;
        Host.IsVisible = true;
        ClearSplitTiles();

        if (wasActive && EntryCount > 0) ShowEntry(_entryIndex);   // 回到单图：显示当前张
    }

    /// <summary>按屏翻页（网格模式）：一屏 = N 张，到头即止。</summary>
    private void GoToScreen(int delta)
    {
        if (_vm is null || EntryCount == 0) return;

        var n = Math.Clamp(_vm.SplitColumns, 2, 4);
        var screens = (EntryCount + n - 1) / n;
        var current = _entryIndex / n;
        var target = Math.Clamp(current + delta, 0, screens - 1);
        if (target == current) return;

        _ = RefreshSplitAsync(target * n);
    }

    /// <summary>
    /// 铺一屏：<paramref name="index"/> 所在的那一屏（屏对齐），装载图片走后台解码。
    /// <para>🔴 每次重铺都递增 <c>_splitToken</c>：连按翻页时旧任务的结果直接丢弃（与压缩包翻页同一手法）。</para>
    /// </summary>
    private async Task RefreshSplitAsync(int index)
    {
        if (_vm is null || EntryCount == 0) { ClearSplitTiles(); return; }

        var token = ++_splitToken;
        var n = Math.Clamp(_vm.SplitColumns, 2, 4);
        var total = EntryCount;
        var start = Math.Clamp(index / n * n, 0, Math.Max(total - 1, 0));
        var count = Math.Min(n, total - start);

        _entryIndex = start;
        _splitFocus = Math.Clamp(index, start, start + count - 1);

        var cols = n == 4 ? 2 : n;
        var rows = n == 4 ? 2 : 1;

        ClearSplitTiles();
        var columnDefs = new ColumnDefinitions();
        for (var c = 0; c < cols; c++) columnDefs.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        SplitGrid.ColumnDefinitions = columnDefs;

        var rowDefs = new RowDefinitions();
        for (var r = 0; r < rows; r++) rowDefs.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        SplitGrid.RowDefinitions = rowDefs;

        var accent = Application.Current?.FindResource("AccentBrush") as IBrush ?? Brushes.DodgerBlue;

        for (var i = 0; i < count; i++)
        {
            var global = start + i;
            var picture = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(4) };

            var indexLabel = new TextBlock
            {
                Text = (global + 1).ToString(),
                FontSize = 10,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 4),
                Foreground = this.FindResource("SecondaryTextBrush") as IBrush ?? Brushes.Gray,
            };

            var cell = new Border
            {
                Margin = new Thickness(2),
                Background = this.FindResource("ContentHeaderBrush") as IBrush,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                IsHitTestVisible = true,
                Child = new Grid { Children = { picture, indexLabel } },
            };

            if (global == _splitFocus)
            {
                cell.BorderBrush = accent;
            }

            cell.PointerPressed += (_, _) => SetSplitFocus(cell, global, accent);
            cell.DoubleTapped += (_, _) => OpenFocusFromSplit(global);

            Grid.SetColumn(cell, i % cols);
            Grid.SetRow(cell, i / cols);
            SplitGrid.Children.Add(cell);

            await LoadTileAsync(picture, global, token);
        }

        if (token != _splitToken) return;

        _vm.SetArchiveRange(start, count);
        _vm.SetInfo($"分屏 · 一屏 {count} 张");
        SyncArchiveSelection(start);
    }

    private void SetSplitFocus(Border cell, int index, IBrush accent)
    {
        if (_vm is null) return;

        _splitFocus = index;
        foreach (var child in SplitGrid.Children.OfType<Border>())
            child.BorderBrush = ReferenceEquals(child, cell) ? accent : Brushes.Transparent;

        _vm.SetArchiveRange(_entryIndex, Math.Min(_vm.SplitColumns, EntryCount - _entryIndex));
        SyncArchiveSelection(index);
    }

    /// <summary>网格里双击某格 = 单图看这张（退出分屏）。</summary>
    private void OpenFocusFromSplit(int index)
    {
        if (_vm is null) return;

        _splitActive = false;   // 先降旗：让 ApplySplitState 别再装一遍屏起点那张
        _vm.ExitSplit();
        ShowEntry(index);
    }

    /// <summary>后台取一张图（压缩包内走会话，同目录读文件），解码完再回 UI 线程设源。</summary>
    private async Task LoadTileAsync(Image target, int index, int token)
    {
        var (bitmap, error) = await Task.Run(() => ReadBitmap(index));

        if (token != _splitToken) { bitmap?.Dispose(); return; }

        if (bitmap is null)
        {
            target.Source = null;
            ToolTip.SetTip(target, error);
            return;
        }

        _splitBitmaps.Add(bitmap);
        target.Source = bitmap;
    }

    /// <summary>读第 <paramref name="index"/> 张的解码结果（⛔ 不碰 UI 控件）。</summary>
    private (Bitmap? Bmp, string Error) ReadBitmap(int index)
    {
        try
        {
            if (_session is not null)
            {
                if (index < 0 || index >= _session.Entries.Count) return (null, "序号越界");
                using var stream = _session.Open(_session.Entries[index].Key, out var reason);
                return stream is null ? (null, reason) : (new Bitmap(stream), "");
            }

            if (index < 0 || index >= _siblings.Count) return (null, "序号越界");
            using var file = File.OpenRead(_siblings[index]);
            return (new Bitmap(file), "");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>清空网格并把已解码的位图释放掉（先断源再 Dispose）。</summary>
    private void ClearSplitTiles()
    {
        foreach (var picture in SplitGrid.Children.OfType<Border>()
                     .Select(b => b.Child).OfType<Grid>()
                     .SelectMany(g => g.Children).OfType<Image>())
            picture.Source = null;

        SplitGrid.Children.Clear();

        foreach (var bitmap in _splitBitmaps) bitmap.Dispose();
        _splitBitmaps.Clear();
    }

    /// <summary>条目条选中项跟随当前屏起点（⛔ 不触发翻页）。</summary>
    private void SyncArchiveSelection(int index)
    {
        _suppress = true;
        try
        {
            ArchiveList.SelectedIndex = index;
            if (ArchiveList.SelectedItem is { } selected) ArchiveList.ScrollIntoView(selected);
        }
        finally { _suppress = false; }
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
        if (EntryCount > 1 && !RulePanel.IsVisible && !Host.IsInfoVisible)
        {
            if (e.Key == Key.Left) { e.Handled = true; ShowEntry(_entryIndex - 1); return; }
            if (e.Key == Key.Right) { e.Handled = true; ShowEntry(_entryIndex + 1); return; }
        }

        base.OnKeyDown(e);
    }
}
