using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using Avalonia.Threading;
using Avalonia.VisualTree;
using NewToys.Core.Services;
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
/// <c>TestResult</c> / <c>HistoryPanel</c> / <c>SplitGrid</c> / <c>SectionBox</c>。基类按名字取控件（XAML 生成的字段在子类里，基类拿不到）。
/// </para>
/// <para>
/// 🔴 本类不做范围判断 —— 收不收这个文件由 <see cref="ViewerViewModel.TryRouteElsewhere"/> 决定，
/// 不属于本页的文件会转去目标页。
/// </para>
/// </summary>
public abstract class ViewerPageBase : UserControl
{
    private ViewerViewModel? _vm;

    /// <summary>一般目录的节大小（每节多少张）：超过这个数才分节，给「几千张图」提供跳节。</summary>
    private const int SectionSize = 100;

    /// <summary>分节的图片数门槛：低于这个数不分节（条目条本来就一眼看得完）。</summary>
    private const int SectionMinCount = 20;

    private ArchiveImageSession? _session;                                    // 压缩包会话（zip 常开）
    private IReadOnlyList<string> _siblings = Array.Empty<string>();          // 同目录图片
    private int _entryIndex;                                                  // 当前第几张（两种来源共用）
    private int _sectionSize;                                                 // 条目条分节大小（0 = 不分节）
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
    private ComboBox? _sectionBox;
    private Border? _rulePanel;
    private Border? _historyPanel;
    private TopLevel? _historyDismissHost;
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
        DetachedFromVisualTree += (_, _) =>
        {
            SaveProgress();
            UnhookHistoryDismiss();
        };
    }

    private ViewerHost Host => _host ??= Find<ViewerHost>("Host");

    private Grid SplitGrid => _splitGrid ??= Find<Grid>("SplitGrid");

    private ListBox RecentList => _recentList ??= Find<ListBox>("RecentList");

    private ListBox ArchiveList => _archiveList ??= Find<ListBox>("ArchiveList");

    private ComboBox SectionBox => _sectionBox ??= Find<ComboBox>("SectionBox");

    private Border RulePanel => _rulePanel ??= Find<Border>("RulePanel");

    private Border HistoryPanel => _historyPanel ??= Find<Border>("HistoryPanel");

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
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.OpenRequested -= OnOpenRequested;
            _vm.CloseRequested -= OnCloseRequested;
            _vm.ZoomInRequested -= OnZoomIn;
            _vm.ZoomOutRequested -= OnZoomOut;
            _vm.FitRequested -= OnFit;
            _vm.RotateRequested -= OnRotate;
            _vm.TypographyChanged -= OnTypographyChanged;
            _vm.ArchiveNavigateRequested -= OnArchiveNavigate;
            _vm.AudioPlayRequested -= OnAudioPlayRequested;
            _vm.ImageSetRequested -= OnImageSetRequested;
            _vm.RulesEditRequested -= OnRulesEdit;
            _vm.ReaderOptionsChanged -= OnReaderOptionsChanged;
            _vm.SplitChanged -= OnSplitChanged;
            _vm.SavingAudioSnapshot = null;
        }

        _vm = DataContext as ViewerViewModel;
        if (_vm is null) return;

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.OpenRequested += OnOpenRequested;
        _vm.CloseRequested += OnCloseRequested;
        _vm.ZoomInRequested += OnZoomIn;
        _vm.ZoomOutRequested += OnZoomOut;
        _vm.FitRequested += OnFit;
        _vm.RotateRequested += OnRotate;
        _vm.TypographyChanged += OnTypographyChanged;
        _vm.ArchiveNavigateRequested += OnArchiveNavigate;
        _vm.AudioPlayRequested += OnAudioPlayRequested;
        _vm.ImageSetRequested += OnImageSetRequested;
        _vm.RulesEditRequested += OnRulesEdit;
        _vm.ReaderOptionsChanged += OnReaderOptionsChanged;
        _vm.SplitChanged += OnSplitChanged;

        // 关闭标签页时 VM 会先要一份续播快照再停播（顺序由 VM 保证：先存位置，再 Stop）
        _vm.SavingAudioSnapshot = SaveAudioPlaylist;

        RuleList.ItemsSource = _vm.CustomRules;
        Host.SetHint(_vm.ScopeHint);

        // 本页是被跳转过来的：投递的文件已经躺在 VM 里，等页面挂上树再打开（控件此时才算就绪）。
        _vm.AttachView();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_vm is null) return;

        // 「媒体资产 → ▶ 播放」：投递到达时页面还没建，先攒着，挂树后开播
        var pendingAudio = _vm.TakePendingAudio();
        if (pendingAudio.Count > 0) { OpenAudioFiles(pendingAudio, autoPlay: true); return; }

        // 「媒体资产 → 看这批图」：翻页集 = 投递的这批图（⛔ 不扫同目录）
        var pendingImages = _vm.TakePendingImageSet();
        if (pendingImages.Count > 0) { OpenImageSet(pendingImages); return; }

        var pending = _vm.TakePending();
        if (pending.Count > 0) { OpenMany(pending); return; }

        RestoreAudioPlaylist();
    }

    /// <summary>
    /// 离开视觉树（切到别的功能页 / 关闭标签页）⇒ 存一次续播快照（曲目 / 位置 / 模式）。
    /// <para>🔴 ⛔ 在这里停播：切页要继续放；「关闭标签页即停」由 <c>ViewerViewModel.Dispose</c> 负责。</para>
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is { Scope: ViewerScope.Audio }) SaveAudioPlaylist();
    }

    /// <summary>音乐页空着挂树时，把上次的播放列表摆回来（⛔ 不自动播，免得一进页面就出声）。</summary>
    private void RestoreAudioPlaylist()
    {
        if (_vm is null || _vm.Scope != ViewerScope.Audio) return;

        // 🔴 已经有实例在播 / 暂停着（从别的页面切回来）⇒ ⛔ 不重建列表：
        //    重建会 Stop + 重新 Open，正在放的歌会断掉、进度归零。
        if (Host.Audio is { CurrentIndex: >= 0 } live
            && _vm.Playback.State != NewToys.Shared.Services.AudioPlayState.Stopped)
        {
            _vm.SetStatus("正在播放：" + (live.Current?.Display ?? ""));
            return;
        }

        var (paths, index, position, mode) = _vm.LoadAudioPlaylist();
        if (paths.Count == 0) return;

        OpenAudioFiles(paths, autoPlay: false, startIndex: index, resumeSeconds: position, mode: mode);
    }

    // ==================== 历史浮层 ====================

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewerViewModel.IsHistoryOpen)) return;

        if (_vm?.IsHistoryOpen == true)
        {
            AlignHistoryPopup();
            HookHistoryDismiss();
        }
        else
        {
            UnhookHistoryDismiss();
        }
    }

    /// <summary>
    /// 历史浮层是页内 <c>Border</c>（不是 <c>Popup</c>），没有自带 light-dismiss ⇒ 自己在顶层挂监听：
    /// 点面板以外任意处 / 按 Esc 收起。
    /// </summary>
    private void HookHistoryDismiss()
    {
        var host = TopLevel.GetTopLevel(this);
        if (host is null || ReferenceEquals(host, _historyDismissHost)) return;

        UnhookHistoryDismiss();
        _historyDismissHost = host;
        host.AddHandler(InputElement.PointerPressedEvent, OnHistoryDismissPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.KeyDownEvent, OnHistoryDismissKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void UnhookHistoryDismiss()
    {
        if (_historyDismissHost is null) return;

        _historyDismissHost.RemoveHandler(InputElement.PointerPressedEvent, OnHistoryDismissPressed);
        _historyDismissHost.RemoveHandler(InputElement.KeyDownEvent, OnHistoryDismissKeyDown);
        _historyDismissHost = null;
    }

    private void OnHistoryDismissPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm?.IsHistoryOpen != true || e.Source is not Visual source) return;
        if (IsHistorySurfaces(source)) return;

        _vm.IsHistoryOpen = false;
    }

    private void OnHistoryDismissKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _vm?.IsHistoryOpen != true) return;

        _vm.IsHistoryOpen = false;
        e.Handled = true;
    }

    /// <summary>面板内部，或工具条上那颗「历史」按钮（它的 Click 自己会切换，别在这里抢着关）。</summary>
    private bool IsHistorySurfaces(Visual source)
    {
        if (ReferenceEquals(source, HistoryPanel)) return true;
        if (source.GetVisualAncestors().Contains(HistoryPanel)) return true;

        var command = _vm?.ToggleHistoryCommand;
        return command is not null
               && source.GetVisualAncestors().OfType<Button>().Any(b => ReferenceEquals(b.Command, command));
    }

    /// <summary>
    /// 历史浮层的左边缘对齐工具条「历史」按钮。
    /// <para>🔴 按钮在 Shell 的工具条里、页面拿不到引用 ⇒ 按**命令对象**在窗口视觉树里反查（⛔ 别按文案匹配，
    /// 文案是本地化的）；查不到（被折进「⋯」）就退回右上角贴边。</para>
    /// </summary>
    private void AlignHistoryPopup()
    {
        var popup = HistoryPanel;
        popup.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        popup.Margin = new Thickness(0, 6, 12, 0);

        if (_vm is null) return;
        if (popup.GetVisualParent() is not { } parent) return;

        var anchor = TopLevel.GetTopLevel(this)?.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, _vm.ToggleHistoryCommand));
        if (anchor is null) return;

        var origin = anchor.TranslatePoint(new Point(0, 0), parent);
        if (origin is null) return;

        var maxLeft = Math.Max(0, parent.Bounds.Width - popup.Width - 8);
        popup.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        popup.Margin = new Thickness(Math.Clamp(origin.Value.X, 0, maxLeft), 6, 0, 0);
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

        // 播放列表文件（m3u / m3u8）先展开成音频列表，再按多首处理
        if (paths.Count == 1 && AudioLibraryService.IsPlaylist(paths[0]))
        {
            var expanded = _vm.AudioLibrary.ReadPlaylist(paths[0]);
            if (expanded.Count == 0)
            {
                _vm.OnOpenFailed("这个播放列表里没有可播的音频（路径失效或不是音频）。");
                return;
            }
            OpenAudioFiles(expanded, autoPlay: true);
            return;
        }

        if (_vm.TryRouteElsewhere(paths)) return;

        if (paths.Count > 1 && paths.All(ViewerHost.IsAudio)) OpenAudioFiles(paths);
        else Open(paths[0]);
    }

    /// <summary>
    /// 打开一个文件（拖放 / 最近打开 / 选择器都汇到这里）。
    /// <para><paramref name="siblingsOverride"/> 非空 = 翻页集由调用方指定（媒体资产按标签投递的那批图），
    /// 此时 ⛔ 不再扫同目录。</para>
    /// </summary>
    private void Open(string path, IReadOnlyList<string>? siblingsOverride = null)
    {
        if (_vm is null) return;

        SaveProgress();   // 换文件前先存上一个的进度

        // 换文件前先止住音乐：音频页打开了别的类型时，⛔ 不让上一首在后台继续响
        Host.Audio?.Stop();

        if (ViewerHost.IsArchive(path)) { OpenArchive(path); return; }

        var kind = ViewerHost.KindOf(path);

        if (kind == ViewerKind.Reader) { OpenReaderFile(path); return; }
        if (kind == ViewerKind.Audio) { OpenAudioWithFolderPrompt(path); return; }

        IReadOnlyList<string> siblings = Array.Empty<string>();
        var index = 0;
        if (kind == ViewerKind.Image) siblings = siblingsOverride is { Count: > 1 } ? siblingsOverride : LoadSiblings(path, out index);

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
            _sectionSize = 0;
            _vm.ClearArchive();   // 只有一张就不占顶部条带
            return;
        }

        // 分节大小跟着总数走：固定 100 张一节时，几十张的目录永远看不到节信息。
        _sectionSize = PickSectionSize(names.Count);

        _suppress = true;
        try
        {
            _vm.SetArchiveEntries(names, index, _sectionSize);
            ArchiveList.SelectedIndex = index;
            ScrollEntryIntoView(index);
            SyncSectionBox(index);
        }
        finally { _suppress = false; }
    }

    /// <summary>把节下拉指到第 <paramref name="index"/> 张所在的节（<c>-1</c> = 清空选中）；⛔ 不触发跳节。</summary>
    private void SyncSectionBox(int index)
    {
        var was = _suppress;
        _suppress = true;
        try
        {
            SectionBox.SelectedIndex = _sectionSize <= 0 || index < 0 ? -1 : index / _sectionSize;
        }
        finally { _suppress = was; }
    }

    /// <summary>节下拉换了一节 ⇒ 直接跳到该节第一张。</summary>
    protected void OnSectionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _sectionSize <= 0) return;

        var section = SectionBox.SelectedIndex;
        if (section < 0) return;

        ShowEntry(section * _sectionSize);
    }

    /// <summary>
    /// 按条目总数挑节大小：≈ 10 节（10 的倍数），最小 10 张、最大 <see cref="SectionSize"/> 张。
    /// <para>总数 &lt; <see cref="SectionMinCount"/> 不分节（返回 0）。</para>
    /// </summary>
    private static int PickSectionSize(int count)
    {
        if (count < SectionMinCount) return 0;

        var size = (int)Math.Ceiling(count / 10d / 10d) * 10;
        return Math.Clamp(size, 10, SectionSize);
    }

    /// <summary>
    /// 把条目条滚到第 <paramref name="index"/> 条。
    /// <para>🔴 容器要等布局才生成（换过 ItemsSource 更是如此）：拿不到容器或滚动量还没实测出来就等下一帧再试，
    /// ⛔ 别用 <c>BringIntoView</c> —— 它只会贴着边缘滚，激活项永远卡在边上。</para>
    /// <para>🔴 定位策略：条目**没铺满**条目条 ⇒ 保持从头顺序排列（<c>Offset = 0</c>）；
    /// **铺满或超出** ⇒ 把当前条滚到**正中**（贴边滚动的 BringIntoView 会让激活项永远卡在边缘，看不出前后关系）。</para>
    /// </summary>
    private void ScrollEntryIntoView(int index)
    {
        if (index < 0) return;
        ScrollEntryIntoViewCore(index, 0);
    }

    private void ScrollEntryIntoViewCore(int index, int attempt)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (index >= ArchiveList.ItemCount) return;

            if (ArchiveList.ContainerFromIndex(index) is Control container && CenterEntryInStrip(container))
                return;

            // 容器 / 滚动量还没实测出来（刚换来源、刚显示条目条）⇒ 下一帧再试
            if (attempt < 3) ScrollEntryIntoViewCore(index, attempt + 1);
        }, DispatcherPriority.Background);
    }

    /// <summary>把某条滚到条目条中央；整条放得下时归零（不居中、按顺序排列）。返回 false = 这一帧还算不出来，调用方可重试。</summary>
    private bool CenterEntryInStrip(Control container)
    {
        var scroller = ArchiveList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroller is null) return true;

        var extent = scroller.Extent.Width;
        var viewport = scroller.Viewport.Width;
        if (extent <= 0 || viewport <= 0) return false;
        if (container.Bounds.Width <= 0) return false;

        if (extent <= viewport + 0.5)
        {
            scroller.Offset = new Vector(0, 0);   // 没铺满：从头排，⛔ 别把它推到中间
            return true;
        }

        var anchor = container.TranslatePoint(new Point(0, 0), scroller);
        if (anchor is not { } p) return false;

        // 🔴 `TranslatePoint` 给的是**已经扣过当前 offset** 的坐标 ⇒ 必须把它加回去才是内容坐标系里的位置，
        //    否则每次居中都会再少滚「当前偏移量」那么多，翻到后面越偏越多（看起来就是"激活项不居中"）。
        var inContent = p.X + scroller.Offset.X;
        var target = inContent + container.Bounds.Width / 2 - viewport / 2;
        scroller.Offset = new Vector(Math.Clamp(target, 0, extent - viewport), scroller.Offset.Y);
        return true;
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
            _ = RefreshSplitAsync(target, target);
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
        RestoreFocus();
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
        RestoreFocus();
    }

    private void ApplyEntryIndex(int index)
    {
        _entryIndex = index;

        _suppress = true;
        try
        {
            ArchiveList.SelectedIndex = index;
            ScrollEntryIntoView(index);
            SyncSectionBox(index);
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
        var lastFocus = _splitFocus;
        _splitActive = false;
        _splitFocus = -1;
        SplitGrid.IsVisible = false;
        Host.IsVisible = true;
        ClearSplitTiles();

        // 回到单图：有选中格就显示那一格，否则显示当前屏起点那张
        if (wasActive && EntryCount > 0) ShowEntry(lastFocus >= 0 ? lastFocus : _entryIndex);
    }

    /// <summary>按屏翻页（网格模式）：一屏 = N 张，到头即止。</summary>
    private void GoToScreen(int delta)
    {
        if (_vm is null || EntryCount == 0) return;

        var n = Math.Clamp(_vm.SplitRows * _vm.SplitColumns, 2, 4);
        var screens = (EntryCount + n - 1) / n;
        var current = _entryIndex / n;
        var target = Math.Clamp(current + delta, 0, screens - 1);
        if (target == current) return;

        _ = RefreshSplitAsync(target * n);
    }

    /// <summary>
    /// 铺一屏：<paramref name="index"/> 所在的那一屏（屏对齐），装载图片走后台解码。
    /// <para>🔴 <paramref name="focus"/> = 要**激活**的格；<c>-1</c> = 取本屏第一张（开分屏 / 翻屏 / 换每屏张数都走这条）。</para>
    /// <para>🔴 每次重铺都递增 <c>_splitToken</c>：连按翻页时旧任务的结果直接丢弃（与压缩包翻页同一手法）。</para>
    /// </summary>
    private async Task RefreshSplitAsync(int index, int focus = -1)
    {
        if (_vm is null || EntryCount == 0) { ClearSplitTiles(); return; }

        var token = ++_splitToken;
        var cols = Math.Clamp(_vm.SplitColumns, 1, 2);
        var rows = Math.Clamp(_vm.SplitRows, 1, 2);
        var n = Math.Clamp(cols * rows, 2, 4);
        var total = EntryCount;
        var start = Math.Clamp(index / n * n, 0, Math.Max(total - 1, 0));
        var count = Math.Min(n, total - start);

        _entryIndex = start;
        // 分屏一律以本屏**第一张**为激活项（⛔ 不是"不选中"）：目录条、节下拉、描边都指它。
        _splitFocus = focus < 0 ? start : Math.Clamp(focus, start, start + count - 1);

        ClearSplitTiles();
        var columnDefs = new ColumnDefinitions();
        for (var c = 0; c < cols; c++) columnDefs.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        SplitGrid.ColumnDefinitions = columnDefs;

        var rowDefs = new RowDefinitions();
        for (var r = 0; r < rows; r++) rowDefs.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        SplitGrid.RowDefinitions = rowDefs;

        var accent = Application.Current?.FindResource("AccentBrush") as IBrush ?? Brushes.DodgerBlue;

        // 每格解码上限（像素）：格宽 × 2，给「点开单图」留一点余量；屏幕上看不出更多细节
        var tileSide = TileDecodeSide(cols);

        // 🔴 解码必须**并行**：逐格 `await` 会让第 2 格等第 1 格解码完才开工 ⇒
        //    左右分屏时右边那张总比左边晚一大拍（分屏格数越多越明显）。
        var loads = new Task[count];

        for (var i = 0; i < count; i++)
        {
            var global = start + i;
            var picture = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(4) };

            var indexLabel = new TextBlock
            {
                Text = (global + 1).ToString(),
                FontSize = 11,
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

            loads[i] = LoadTileAsync(picture, global, token, tileSide);
        }

        await Task.WhenAll(loads);

        if (token != _splitToken) return;

        _vm.SetArchiveRange(start, count);
        _vm.SetInfo($"分屏 · 一屏 {count} 张");
        SyncArchiveSelection(_splitFocus);
        SyncSectionBox(_splitFocus);
        ScrollEntryIntoView(_splitFocus);
        RestoreFocus();
    }

    private void SetSplitFocus(Border cell, int index, IBrush accent)
    {
        if (_vm is null) return;

        _splitFocus = index;
        foreach (var child in SplitGrid.Children.OfType<Border>())
            child.BorderBrush = ReferenceEquals(child, cell) ? accent : Brushes.Transparent;

        _vm.SetArchiveRange(_entryIndex, Math.Min(_vm.SplitRows * _vm.SplitColumns, EntryCount - _entryIndex));
        SyncArchiveSelection(index);
        ScrollEntryIntoView(index);   // 点哪格，条目条就把它居中（与翻页同一手法）
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
    private async Task LoadTileAsync(Image target, int index, int token, int maxSide)
    {
        var (bitmap, error) = await Task.Run(() => ReadBitmap(index, maxSide));

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

    /// <summary>分屏每格的解码边长：格宽（物理像素）× 2，夹在 480 ~ 2048。</summary>
    private int TileDecodeSide(int cols)
    {
        var width = SplitGrid.Bounds.Width > 1 ? SplitGrid.Bounds.Width : Bounds.Width;
        if (width <= 1) width = 1280;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var side = (int)Math.Ceiling(width / Math.Max(cols, 1) * scaling * 2);
        return Math.Clamp(side, 480, 2048);
    }

    /// <summary>
    /// 读第 <paramref name="index"/> 张的解码结果（⛔ 不碰 UI 控件）。
    /// <para><paramref name="maxSide"/> = 解码边长上限；原图更大就降采样（分屏四格全尺寸解码能吃掉几百 MB）。</para>
    /// </summary>
    private (Bitmap? Bmp, string Error) ReadBitmap(int index, int maxSide)
    {
        try
        {
            if (_session is not null)
            {
                if (index < 0 || index >= _session.Entries.Count) return (null, "序号越界");
                using var stream = _session.Open(_session.Entries[index].Key, out var reason);
                return stream is null ? (null, reason) : (DecodeTile(stream, maxSide), "");
            }

            if (index < 0 || index >= _siblings.Count) return (null, "序号越界");
            using var file = File.OpenRead(_siblings[index]);
            return (DecodeTile(file, maxSide), "");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static Bitmap DecodeTile(Stream stream, int maxSide)
    {
        var origin = stream.CanSeek ? stream.Position : 0;

        if (ImageHeader.TryReadSize(stream, out var w, out var h) && (w > maxSide || h > maxSide))
        {
            stream.Position = origin;
            return w >= h ? Bitmap.DecodeToWidth(stream, maxSide) : Bitmap.DecodeToHeight(stream, maxSide);
        }

        return new Bitmap(stream);
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

    /// <summary>条目条选中项跟随当前屏（⛔ 不触发翻页）；<paramref name="index"/> = -1 表示**不选中任何一条**。</summary>
    private void SyncArchiveSelection(int index)
    {
        _suppress = true;
        try
        {
            ArchiveList.SelectedIndex = index;
            ScrollEntryIntoView(index);
            SyncSectionBox(index);
        }
        finally { _suppress = false; }
    }

    // ==================== 音频 ====================

    private void OpenAudioFiles(IReadOnlyList<string> paths, bool autoPlay = true, int startIndex = 0,
        double resumeSeconds = 0, int mode = 0)
    {
        if (_vm is null || paths.Count == 0) return;

        var first = Math.Clamp(startIndex, 0, paths.Count - 1);

        if (!Host.Open(paths[first], 0, out var error, () => _vm.Playback))
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

        // 译文歌词由宿主提供（UI 库不认识 .zh.lrc 这类约定）
        player.TranslationLoader = p => _vm.AudioLibrary.ReadTranslation(p);

        // 歌词来源（同名 .lrc / 内嵌 USLT / 标题-艺术家.lrc）与频谱数据源都由宿主注入
        player.LyricsLoader = p => _vm.AudioLibrary.ReadLyrics(p);
        player.SpectrumSource = _vm.Playback as NewToys.Shared.Services.IAudioSpectrum;

        _vm.Playback.PlaybackEnded -= OnPlaybackEnded;
        _vm.Playback.PlaybackEnded += OnPlaybackEnded;

        var tracks = _vm.AudioLibrary.ReadMany(paths);
        player.SetVolume(_vm.AudioVolume);
        player.SetPlaylist(tracks, first, autoPlay);

        // 续播：模式与位置在列表装好之后再恢复（⛔ 顺手开播，保持暂停态）
        player.ModeValue = mode;
        if (resumeSeconds > 1) player.SeekTo(TimeSpan.FromSeconds(resumeSeconds));

        ResetEntries();
        _vm.OnOpened(paths[first], ViewerKind.Audio, tracks.Count + " 首");
        SaveAudioPlaylist();
    }

    /// <summary>「媒体资产 → ▶ 播放」：整批换列表并开播。</summary>
    private void OnAudioPlayRequested(object? sender, IReadOnlyList<string> paths)
        => OpenAudioFiles(paths, autoPlay: true);

    /// <summary>
    /// 打开单首音乐：先问一句「要不要把同文件夹里剩下的也加进播放列表」。
    /// <para>🔴 是 <c>async void</c>：入口在 UI 事件上、没人 await；列表装配全在 try 内，
    /// 只有「定好要播哪些」之后才真正开播，⛔ 异常路径里二次打开。</para>
    /// </summary>
    private async void OpenAudioWithFolderPrompt(string path)
    {
        if (_vm is null) return;

        var folder = Path.GetDirectoryName(path);
        var others = new List<string>();

        try
        {
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                others = Directory.EnumerateFiles(folder)
                    .Where(p => ViewerHost.IsAudio(p) && !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _vm.SetStatus("⚠ 读文件夹失败：" + ex.Message);
        }

        IReadOnlyList<string> playlist = new[] { path };

        if (others.Count > 0)
        {
            bool ok;
            try
            {
                ok = await DialogHooks.ConfirmAsync(
                    $"把同一文件夹里的另外 {others.Count} 首也加进播放列表？\n\n{folder}");
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                var all = new List<string>(others.Count + 1) { path };
                all.AddRange(others);
                all.Sort(StringComparer.OrdinalIgnoreCase);
                playlist = all;
            }
        }

        var start = 0;
        for (var i = 0; i < playlist.Count; i++)
        {
            if (!string.Equals(playlist[i], path, StringComparison.OrdinalIgnoreCase)) continue;
            start = i;
            break;
        }

        OpenAudioFiles(playlist, autoPlay: true, startIndex: start);
    }

    /// <summary>「媒体资产 → 看这批图」：打开第一张，翻页集就是这批图。</summary>
    private void OnImageSetRequested(object? sender, IReadOnlyList<string> paths) => OpenImageSet(paths);

    /// <summary>
    /// 按投递顺序打开一批图片：首张显示，左右翻页只在这批里转（⛔ 不扫同目录，否则会混进没被选中的图）。
    /// </summary>
    private void OpenImageSet(IReadOnlyList<string> paths)
    {
        if (_vm is null || paths.Count == 0) return;
        if (paths.Count == 1) { Open(paths[0]); return; }

        Open(paths[0], paths);
    }

    /// <summary>把当前列表 + 当前曲目记下来（关掉再开能续上）。</summary>
    private void SaveAudioPlaylist()
    {
        if (_vm is null) return;

        var player = Host.Audio;
        if (player is null || player.Tracks.Count == 0) return;

        _vm.SaveAudioPlaylist(
            player.Tracks.Select(t => t.Path).ToList(),
            player.CurrentIndex,
            player.PositionSeconds,
            player.ModeValue);
    }

    private void OnAudioTrackChanged(AudioTrackItem? track)
    {
        if (track is null) return;
        _vm?.SetInfo(track.Display);
        SaveAudioPlaylist();
    }

    private void OnAudioStatus(string text) => _vm?.SetStatus(text);

    private void OnPlaybackEnded(object? sender, EventArgs e) => Host.Audio?.HandlePlaybackEnded();

    // ==================== 关闭 / 缩放 / 排版 ====================

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        SaveProgress();
        SaveAudioPlaylist();   // 🔴 必须在 Host.Close() 之前：Stop 会把进度归零
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

    /// <summary>旋转 90°（分屏网格里不生效：网格每格按自适应铺开）。</summary>
    private void OnRotate(object? sender, EventArgs e)
    {
        if (_vm is { IsSplit: true }) return;

        Host.Rotate();
        UpdateZoomInfo();
        _vm?.SetStatus("已旋转 90°。");
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
            if (e.Key is Key.Left or Key.Right)
            {
                e.Handled = true;

                var delta = e.Key == Key.Left ? -1 : 1;

                // 分屏：方向键整屏翻（与滚轮一致），不是在一屏里挪一张
                if (_vm is { IsSplit: true, IsImage: true }) GoToScreen(delta);
                else ShowEntry(_entryIndex + delta);

                return;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// 翻页后把键盘焦点收回本页。
    /// <para>🔴 目录里换图会重建渲染器 ⇒ 焦点回到窗口，方向键直接失效（必须再点一下图片才恢复）；
    /// 规则浮层开着时 ⛔ 别抢焦点（用户可能正在输正则）。</para>
    /// </summary>
    private void RestoreFocus()
    {
        if (RulePanel.IsVisible) return;
        if (IsKeyboardFocusWithin) return;

        Focus();
    }
}
