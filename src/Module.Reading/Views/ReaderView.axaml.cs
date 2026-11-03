using System;
using Avalonia.Controls;
using NewToys.Reading.Controls;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>
/// 阅读页 code-behind：把 <see cref="ReaderViewModel"/>（抓取 / 进度 / 书签 / 排版的归属地）与
/// <see cref="ReaderShell"/>（渲染）接起来 —— VM ⛔ 碰控件，壳 ⛔ 碰配置，事件在本类互转。
/// </summary>
public partial class ReaderView : UserControl
{
    private ReaderViewModel? _vm;
    private bool _shellWired;

    public ReaderView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => _vm?.CheckPending();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.BookReady -= OnBookReady;
            _vm.ChapterFetched -= OnChapterFetched;
            _vm.ShellTypographyReady -= OnShellTypographyReady;
        }

        _vm = DataContext as ReaderViewModel;
        if (_vm is null) return;

        _vm.BookReady += OnBookReady;
        _vm.ChapterFetched += OnChapterFetched;
        _vm.ShellTypographyReady += OnShellTypographyReady;

        _vm.CheckPending();
    }

    // ==================== 壳事件 → VM（一次接线，VM 换了也照常用 _vm 现值） ====================

    private void WireShellOnce()
    {
        if (_shellWired) return;
        _shellWired = true;

        Shell.PositionChanged += OnShellPositionChanged;
        Shell.ChapterMissing += OnShellChapterMissing;
        Shell.TypographyChanged += OnShellTypographyChanged;
        Shell.AddBookmarkRequested += OnShellAddBookmark;
        Shell.RemoveBookmarkRequested += OnShellRemoveBookmark;
        Shell.BookmarkActivated += OnShellBookmarkActivated;
        Shell.StatusChanged += OnShellStatus;
    }

    private void OnShellPositionChanged(ReaderPosition position)
    {
        if (_vm is null) return;
        _vm.SaveShellProgress(position.ChapterIndex, position.Ratio, Shell.CurrentChapterTitle);
        _vm.SetReaderText(Shell.CurrentChapterText, Shell.CurrentChapterTitle);
    }

    private void OnShellChapterMissing(int index) => _ = _vm?.FetchChapterAsync(index);

    private void OnShellTypographyChanged(ReaderTypography typography) => _vm?.SaveShellTypography(typography);

    private void OnShellAddBookmark()
    {
        if (_vm is null) return;
        _vm.AddBookmark(Shell.CreateBookmarkCore());
        Shell.SetBookmarks(_vm.LoadBookmarks());
    }

    private void OnShellRemoveBookmark(string id)
    {
        if (_vm is null) return;
        _vm.RemoveBookmark(id);
        Shell.SetBookmarks(_vm.LoadBookmarks());
    }

    private void OnShellBookmarkActivated(ReaderBookmark bookmark)
        => Shell.ShowChapter(bookmark.ChapterIndex, bookmark.Ratio);

    private void OnShellStatus(string text)
    {
        if (_vm is not null) _vm.StatusText = text;
    }

    // ==================== VM 事件 → 壳 ====================

    /// <summary>书就绪：装进壳（索引书模式）+ 书签 + 排版 + 听书文本。</summary>
    private void OnBookReady(string indexPath, int chapterIndex, double ratio)
    {
        if (_vm is null) return;

        WireShellOnce();
        _vm.ReloadBookmarks();

        if (!Shell.LoadIndex(indexPath, new ReaderPosition(chapterIndex, ratio), out var error))
        {
            _vm.StatusText = "⚠ " + error;
            return;
        }

        Shell.SetBookmarks(_vm.LoadBookmarks());
        Shell.ApplyTypography(_vm.BuildShellTypography());
        _vm.SetReaderText(Shell.CurrentChapterText, Shell.CurrentChapterTitle);
    }

    /// <summary>缺章补抓落盘完成：重读当前章。</summary>
    private void OnChapterFetched()
    {
        if (_vm is null) return;

        Shell.ReloadChapter();
        _vm.SetReaderText(Shell.CurrentChapterText, Shell.CurrentChapterTitle);
    }

    /// <summary>排版在设置页被改了：下发给壳。</summary>
    private void OnShellTypographyReady(ReaderTypography typography) => Shell.ApplyTypography(typography);

    // ==================== 浮层遮罩 ====================

    private void OnSwitchBackdropTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null) _vm.ShowSwitchPanel = false;
    }

    /// <summary>点整本下载浮层的遮罩 = 收起（下载中不收起，避免用户看不到进度）。</summary>
    private void OnDownloadBackdropTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null && !_vm.IsDownloading) _vm.DownloadIsOpen = false;
    }
}
