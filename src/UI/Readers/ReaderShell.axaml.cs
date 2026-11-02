using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace NewToys.UI.Readers;

/// <summary>
/// 文本阅读器壳：目录抽屉 + 章内滚动 + 书签触发 + 全书搜索 + 排版下发。
/// <para>
/// 🔴 输入是「整本书字符串 + 章节切片」，本控件⛔ 不读文件、⛔ 不写配置 ——
/// 编码嗅探 / 章节规则 / 书签持久化全在宿主（<c>Module.Viewer</c>）。
/// </para>
/// <para>
/// 🔴 「章」是唯一的进度单位：切章只换正文，章内用滚动比例记位置；
/// 这样一本几 MB 的 txt 也不用一次性塞进视觉树。
/// </para>
/// </summary>
public partial class ReaderShell : UserControl
{
    private static readonly string[] ThemeNames = { "跟随主题", "米黄护眼", "淡绿", "夜间" };
    private static readonly (string? Background, string? Foreground)[] Themes =
    {
        (null, null),
        ("#F5EFE0", "#3A3226"),
        ("#E7F0E3", "#24301F"),
        ("#1E1E20", "#C9C9C9"),
    };

    private string _book = "";
    private IReadOnlyList<ReaderChapter> _chapters = Array.Empty<ReaderChapter>();
    private int _index;
    private double _ratio;
    private bool _suppress;
    private DateTimeOffset _lastPositionAt = DateTimeOffset.MinValue;

    private int _highlightStart = -1;
    private int _highlightLength;
    private int _highlightOrdinal;
    private int _highlightTotal;

    private ReaderTypography _typography = ReaderTypography.Default;

    public ReaderShell()
    {
        InitializeComponent();
        Focusable = true;

        foreach (var name in ThemeNames) ThemeBox.Items.Add(new ComboBoxItem { Content = name });
        ThemeBox.SelectedIndex = 0;

        Scroller.PropertyChanged += OnScrollerPropertyChanged;
        AttachedToVisualTree += (_, _) => Focus();
    }

    /// <summary>阅读位置变化（章 + 章内比例）；宿主据此存进度。</summary>
    public event Action<ReaderPosition>? PositionChanged;

    /// <summary>排版参数变化（宿主据此落配置）。</summary>
    public event Action<ReaderTypography>? TypographyChanged;

    /// <summary>用户点了「加书签」——宿主负责生成并持久化。</summary>
    public event Action? AddBookmarkRequested;

    /// <summary>用户要删除某条书签（参数 = Id）。</summary>
    public event Action<string>? RemoveBookmarkRequested;

    /// <summary>用户点了某条书签要跳过去。</summary>
    public event Action<ReaderBookmark>? BookmarkActivated;

    /// <summary>一句话状态（切章到头 / 搜索无结果等）。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>当前阅读位置。</summary>
    public ReaderPosition Position => new(_index, _ratio);

    /// <summary>当前排版参数。</summary>
    public ReaderTypography CurrentTypography => _typography;

    /// <summary>章节数（状态栏显示用）。</summary>
    public int ChapterCount => _chapters.Count;

    // ==================== 装载 ====================

    /// <summary>装载整本书并从 <paramref name="position"/> 续读；失败返回 false 并给出原因。</summary>
    public bool Load(string text, IReadOnlyList<ReaderChapter> chapters, ReaderPosition position, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(text) || chapters.Count == 0)
        {
            error = "没有可显示的正文（章节识别结果为空）。";
            return false;
        }

        _book = text;
        _chapters = chapters;
        TocList.ItemsSource = chapters;
        ShowChapter(position.ChapterIndex, position.Ratio);
        return true;
    }

    /// <summary>跳转到指定章（<paramref name="ratio"/> = 章内比例）。</summary>
    public void ShowChapter(int index, double ratio)
    {
        if (_chapters.Count == 0) return;

        _index = Math.Clamp(index, 0, _chapters.Count - 1);
        _ratio = Math.Clamp(ratio, 0, 1);
        _highlightStart = -1;
        _highlightLength = 0;

        RenderBody();
        ChapterTitle.Text = _chapters[_index].Title;
        SyncTocSelection();

        var target = _ratio;
        Dispatcher.UIThread.Post(() => ApplyScroll(target), DispatcherPriority.Loaded);
        RaisePosition(true);
    }

    /// <summary>下一章（末尾时提示，不越界）。</summary>
    public void NextChapter()
    {
        if (_index >= _chapters.Count - 1)
        {
            StatusChanged?.Invoke("已经是最后一章。");
            return;
        }
        ShowChapter(_index + 1, 0);
    }

    /// <summary>上一章。</summary>
    public void PrevChapter()
    {
        if (_index <= 0)
        {
            StatusChanged?.Invoke("已经是第一章。");
            return;
        }
        ShowChapter(_index - 1, 0);
    }

    // ==================== 排版 ====================

    /// <summary>下发排版参数（宿主从配置读出来后调用）。</summary>
    public void ApplyTypography(ReaderTypography typography)
    {
        _typography = typography ?? ReaderTypography.Default;

        _suppress = true;
        try
        {
            FontSlider.Value = Math.Clamp(_typography.FontSize, 12, 34);
            LineSlider.Value = Math.Clamp(_typography.LineSpacing, 1.2, 2.6);
            PadSlider.Value = Math.Clamp(_typography.Padding, 0, 160);
            IndentBox.IsChecked = _typography.Indent;
            SpacingBox.IsChecked = _typography.ParagraphSpacing > 0;
        }
        finally { _suppress = false; }

        ApplyVisuals();
        ApplyTheme(_typography.Theme);

        if (_chapters.Count > 0)
        {
            var keep = _ratio;
            RenderBody();
            Dispatcher.UIThread.Post(() => ApplyScroll(keep), DispatcherPriority.Loaded);
        }
    }

    /// <summary>当前配色档（0 = 跟随主题）。</summary>
    public void ApplyTheme(int index)
    {
        var i = Math.Clamp(index, 0, Themes.Length - 1);
        _suppress = true;
        try { ThemeBox.SelectedIndex = i; }
        finally { _suppress = false; }

        var (bg, fg) = Themes[i];
        ReaderBackground.Background = bg is null ? null : new SolidColorBrush(Color.Parse(bg));
        Body.Foreground = fg is null ? FindBrush("PrimaryTextBrush") : new SolidColorBrush(Color.Parse(fg));
    }

    private void ApplyVisuals()
    {
        Body.FontSize = _typography.FontSize;
        Body.LineHeight = Math.Round(_typography.FontSize * _typography.LineSpacing);
        Pad.Padding = new Thickness(_typography.Padding, 24);

        if (!string.IsNullOrWhiteSpace(_typography.FontFamily))
        {
            try { Body.FontFamily = new FontFamily(_typography.FontFamily); }
            catch { /* 字体名无效 ⇒ 系统默认 */ }
        }

        if (!string.IsNullOrWhiteSpace(_typography.Background))
            ReaderBackground.Background = new SolidColorBrush(Color.Parse(_typography.Background));
        if (!string.IsNullOrWhiteSpace(_typography.Foreground))
            Body.Foreground = new SolidColorBrush(Color.Parse(_typography.Foreground));
    }

    // ==================== 正文渲染 ====================

    private void RenderBody()
    {
        var chapter = _chapters[_index];
        var bodyStart = Math.Clamp(chapter.BodyStart, 0, _book.Length);
        var bodyEnd = Math.Clamp(chapter.Start + chapter.Length, bodyStart, _book.Length);
        var raw = _book.Substring(bodyStart, bodyEnd - bodyStart);

        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var separator = _typography.ParagraphSpacing > 0 ? "\n\n" : "\n";
        var indent = _typography.Indent ? "　　" : "";

        Body.Inlines?.Clear();
        var first = true;
        var cursor = bodyStart;

        foreach (var line in lines)
        {
            var lineStart = cursor;
            cursor += line.Length + 1;   // +1 = 被切掉的换行

            var paragraph = line.Trim();
            if (paragraph.Length == 0) continue;

            if (!first) Body.Inlines?.Add(new Run { Text = separator });
            first = false;

            AppendParagraph(indent + paragraph, lineStart, indent.Length);
        }
    }

    /// <summary>追加一个段落；命中高亮区间时拆成三段（命中段加粗 + 强调色）。</summary>
    private void AppendParagraph(string text, int absoluteStart, int indentLength)
    {
        if (_highlightLength <= 0 || Body.Inlines is null)
        {
            Body.Inlines?.Add(new Run { Text = text });
            return;
        }

        var rel = _highlightStart - absoluteStart + indentLength;
        if (rel < 0 || rel >= text.Length)
        {
            Body.Inlines.Add(new Run { Text = text });
            return;
        }

        var length = Math.Min(_highlightLength, text.Length - rel);
        if (rel > 0) Body.Inlines.Add(new Run { Text = text[..rel] });

        var hit = new Run { Text = text.Substring(rel, length), FontWeight = FontWeight.Bold };
        var accent = FindBrush("AccentBrush");
        if (accent is not null) hit.Foreground = accent;
        Body.Inlines.Add(hit);

        if (rel + length < text.Length) Body.Inlines.Add(new Run { Text = text[(rel + length)..] });
    }

    private IBrush? FindBrush(string key)
    {
        try { return this.FindResource(key) as IBrush; }
        catch { return null; }
    }

    // ==================== 滚动 / 进度 ====================

    private void ApplyScroll(double ratio)
    {
        var max = Scroller.Extent.Height - Scroller.Viewport.Height;
        if (max <= 0) return;

        _suppress = true;
        try { Scroller.Offset = new Vector(Scroller.Offset.X, Math.Clamp(ratio, 0, 1) * max); }
        finally { _suppress = false; }
    }

    private void OnScrollerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty || _suppress) return;

        var max = Scroller.Extent.Height - Scroller.Viewport.Height;
        if (max <= 0) return;

        _ratio = Math.Clamp(Scroller.Offset.Y / max, 0, 1);
        RaisePosition(false);
    }

    private void RaisePosition(bool force)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastPositionAt < TimeSpan.FromMilliseconds(250)) return;
        _lastPositionAt = now;
        PositionChanged?.Invoke(new ReaderPosition(_index, _ratio));
    }

    // ==================== 目录 ====================

    private void SyncTocSelection()
    {
        _suppress = true;
        try
        {
            TocList.SelectedIndex = _index;
            TocList.ScrollIntoView(_chapters[_index]);
        }
        finally { _suppress = false; }

        TocCount.Text = $"第 {_index + 1} / {_chapters.Count} 章";
    }

    private void OnTocFilterChanged(object? sender, TextChangedEventArgs e)
    {
        var key = TocFilter.Text?.Trim() ?? "";
        var items = key.Length == 0
            ? _chapters
            : _chapters.Where(c => c.Title.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();

        _suppress = true;
        try
        {
            TocList.ItemsSource = items;
            TocList.SelectedIndex = -1;
        }
        finally { _suppress = false; }

        TocCount.Text = key.Length == 0
            ? $"第 {_index + 1} / {_chapters.Count} 章"
            : $"筛出 {items.Count} / 共 {_chapters.Count} 章";
    }

    private void OnTocSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (TocList.SelectedItem is not ReaderChapter chapter) return;
        var target = IndexOfChapter(chapter);
        if (target >= 0) ShowChapter(target, 0);
    }

    private int IndexOfChapter(ReaderChapter chapter)
    {
        for (var i = 0; i < _chapters.Count; i++)
            if (ReferenceEquals(_chapters[i], chapter)) return i;
        return -1;
    }

    // ==================== 书签 ====================

    /// <summary>刷新书签列表（宿主在增删后调用）。</summary>
    public void SetBookmarks(IEnumerable<ReaderBookmark> bookmarks)
    {
        BookmarkList.ItemsSource = bookmarks.OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <summary>按当前阅读位置生成一条书签（宿主负责加 Id 与持久化，见 <see cref="CreateBookmarkCore"/>）。</summary>
    public ReaderBookmark CreateBookmarkCore()
    {
        var chapter = _chapters[_index];
        var bodyStart = Math.Clamp(chapter.BodyStart, 0, _book.Length);
        var bodyEnd = Math.Clamp(chapter.Start + chapter.Length, bodyStart, _book.Length);
        var bodyLength = Math.Max(1, bodyEnd - bodyStart);
        var at = Math.Clamp(bodyStart + (int)(bodyLength * _ratio), bodyStart, Math.Max(bodyStart, bodyEnd - 1));
        var take = Math.Min(60, Math.Max(0, bodyEnd - at));

        var preview = take > 0
            ? _book.Substring(at, take).Replace("\r", "").Replace("\n", " ").Trim()
            : "";

        return new ReaderBookmark
        {
            Id = Guid.NewGuid().ToString("N"),
            ChapterTitle = chapter.Title,
            ChapterIndex = _index,
            Ratio = _ratio,
            Preview = preview,
            CreatedAt = DateTimeOffset.Now,
        };
    }

    private void OnAddBookmark(object? sender, RoutedEventArgs e)
    {
        if (_chapters.Count == 0) return;
        AddBookmarkRequested?.Invoke();
    }

    private void OnRemoveBookmark(object? sender, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is not ReaderBookmark bookmark) return;
        RemoveBookmarkRequested?.Invoke(bookmark.Id);
    }

    private void OnBookmarkActivated(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (BookmarkList.SelectedItem is not ReaderBookmark bookmark) return;
        _suppress = true;
        try { BookmarkList.SelectedIndex = -1; }
        finally { _suppress = false; }
        BookmarkActivated?.Invoke(bookmark);
    }

    // ==================== 搜索 ====================

    /// <summary>全书搜索（区分大小写关闭，最多 500 条）。</summary>
    public void Search(string keyword)
    {
        var key = (keyword ?? "").Trim();
        if (key.Length == 0)
        {
            SearchInfo.Text = "输入关键词后再搜。";
            return;
        }

        var hits = new List<ReaderSearchHit>();
        var from = 0;
        while (hits.Count < 500)
        {
            var at = _book.IndexOf(key, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) break;

            var chapterIndex = FindChapterIndex(at);
            hits.Add(new ReaderSearchHit
            {
                ChapterIndex = chapterIndex,
                ChapterTitle = chapterIndex >= 0 ? _chapters[chapterIndex].Title : "（卷首）",
                Position = at,
                Snippet = BuildSnippet(at, key.Length),
            });

            from = at + Math.Max(1, key.Length);
        }

        SearchList.ItemsSource = hits;
        SearchInfo.Text = hits.Count == 0 ? $"没有找到「{key}」。" : $"共 {hits.Count} 处（点一条跳过去）";

        if (hits.Count > 0)
        {
            SearchList.SelectedIndex = 0;
            GoToHit(hits[0]);
        }
        else
        {
            _highlightStart = -1;
            _highlightLength = 0;
            RenderBody();
        }
    }

    private string BuildSnippet(int at, int length)
    {
        var start = Math.Max(0, at - 18);
        var end = Math.Min(_book.Length, at + length + 26);
        var text = _book.Substring(start, end - start).Replace("\r", "").Replace("\n", " ").Trim();
        return "…" + text + "…";
    }

    private int FindChapterIndex(int position)
    {
        var low = 0;
        var high = _chapters.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var chapter = _chapters[mid];
            if (position < chapter.Start) high = mid - 1;
            else if (position >= chapter.Start + chapter.Length) low = mid + 1;
            else return mid;
        }
        return -1;
    }

    private void GoToHit(ReaderSearchHit hit)
    {
        if (hit.ChapterIndex < 0) return;

        _index = hit.ChapterIndex;
        _ratio = 0;
        _highlightStart = hit.Position;
        _highlightLength = Math.Max(1, (SearchBox.Text ?? "").Trim().Length);

        var total = 0;
        var ordinal = 0;
        if (SearchList.ItemsSource is IEnumerable<ReaderSearchHit> all)
        {
            foreach (var item in all)
            {
                if (item.ChapterIndex != _index) continue;
                total++;
                if (item.Position == hit.Position) ordinal = total;
            }
        }
        _highlightTotal = total;
        _highlightOrdinal = ordinal;

        RenderBody();
        ChapterTitle.Text = _chapters[_index].Title;
        SyncTocSelection();

        // 命中在章内的相对位置 → 近似滚动（无法直接量到像素）
        var chapter = _chapters[_index];
        var length = Math.Max(1, chapter.Length);
        var relative = Math.Clamp((hit.Position - chapter.Start) / (double)length, 0, 1);
        Dispatcher.UIThread.Post(() => ApplyScroll(relative), DispatcherPriority.Loaded);
        RaisePosition(true);

        SearchInfo.Text = total > 0
            ? $"第 {ordinal} / {total} 处（本章）"
            : "已定位";
    }

    private void OnDoSearch(object? sender, RoutedEventArgs e) => Search(SearchBox.Text ?? "");

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Search(SearchBox.Text ?? "");
    }

    private void OnSearchHitSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (SearchList.SelectedItem is ReaderSearchHit hit) GoToHit(hit);
    }

    // ==================== 浮层开合 ====================

    private void OnToggleToc(object? sender, RoutedEventArgs e) => Toggle(TocPanel);
    private void OnToggleBookmarks(object? sender, RoutedEventArgs e) => Toggle(BookmarkPanel);
    private void OnToggleSearch(object? sender, RoutedEventArgs e) => Toggle(SearchPanel);
    private void OnToggleType(object? sender, RoutedEventArgs e) => Toggle(TypePanel);

    private static void Toggle(Control panel) => panel.IsVisible = !panel.IsVisible;

    private void ClosePanels()
    {
        TocPanel.IsVisible = false;
        BookmarkPanel.IsVisible = false;
        SearchPanel.IsVisible = false;
        TypePanel.IsVisible = false;
    }

    private void OnPrevChapter(object? sender, RoutedEventArgs e) => PrevChapter();
    private void OnNextChapter(object? sender, RoutedEventArgs e) => NextChapter();

    // ==================== 排版控件回写 ====================

    private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppress) return;
        CommitTypography();
    }

    private void OnCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        CommitTypography();
    }

    private void CommitTypography()
    {
        _typography = new ReaderTypography
        {
            FontSize = Math.Round(FontSlider.Value),
            LineSpacing = Math.Round(LineSlider.Value, 1),
            Padding = Math.Round(PadSlider.Value),
            ParagraphSpacing = SpacingBox.IsChecked == true ? 1 : 0,
            Indent = IndentBox.IsChecked != false,
            FontFamily = _typography.FontFamily,
            Foreground = _typography.Foreground,
            Background = _typography.Background,
            Theme = ThemeIndex,
        };

        ApplyVisuals();

        if (_chapters.Count > 0)
        {
            var keep = _ratio;
            RenderBody();
            Dispatcher.UIThread.Post(() => ApplyScroll(keep), DispatcherPriority.Loaded);
        }

        TypographyChanged?.Invoke(_typography);
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ThemeBox.SelectedIndex < 0) return;
        var (bg, fg) = Themes[Math.Clamp(ThemeBox.SelectedIndex, 0, Themes.Length - 1)];
        _typography = new ReaderTypography
        {
            FontSize = _typography.FontSize,
            LineSpacing = _typography.LineSpacing,
            Padding = _typography.Padding,
            ParagraphSpacing = _typography.ParagraphSpacing,
            Indent = _typography.Indent,
            FontFamily = _typography.FontFamily,
            Foreground = fg,
            Background = bg,
            Theme = ThemeBox.SelectedIndex,
        };
        ApplyTheme(ThemeBox.SelectedIndex);
        TypographyChanged?.Invoke(_typography);
    }

    /// <summary>当前配色档序号（宿主持久化用）。</summary>
    public int ThemeIndex => Math.Max(0, ThemeBox.SelectedIndex);

    // ==================== 键盘 ====================

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 输入框里的按键先给输入框
        if (e.Source is TextBox) { base.OnKeyDown(e); return; }

        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                ScrollByPage(1);
                return;
            case Key.PageDown:
                e.Handled = true;
                ScrollByPage(1);
                return;
            case Key.PageUp:
                e.Handled = true;
                ScrollByPage(-1);
                return;
            case Key.Left:
                e.Handled = true;
                PrevChapter();
                return;
            case Key.Right:
                e.Handled = true;
                NextChapter();
                return;
            case Key.Escape:
                ClosePanels();
                return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.F:
                    e.Handled = true;
                    SearchPanel.IsVisible = true;
                    SearchBox.Focus();
                    return;
                case Key.B:
                    e.Handled = true;
                    Toggle(BookmarkPanel);
                    return;
                case Key.T:
                    e.Handled = true;
                    Toggle(TocPanel);
                    return;
            }
        }

        base.OnKeyDown(e);
    }

    private void ScrollByPage(int direction)
    {
        var step = Math.Max(80, Scroller.Viewport.Height * 0.9);
        var max = Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height);
        Scroller.Offset = new Vector(Scroller.Offset.X,
            Math.Clamp(Scroller.Offset.Y + direction * step, 0, max));
    }
}
