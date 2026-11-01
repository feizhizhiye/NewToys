using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
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

    public ViewerView()
    {
        InitializeComponent();

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
        }

        _vm = DataContext as ViewerViewModel;
        if (_vm is null) return;

        _vm.OpenRequested += OnOpenRequested;
        _vm.CloseRequested += OnCloseRequested;
        _vm.ZoomInRequested += OnZoomIn;
        _vm.ZoomOutRequested += OnZoomOut;
        _vm.FitRequested += OnFit;
        _vm.TypographyChanged += OnTypographyChanged;

        Host.SetHint("支持：文本 / 源码 / HTML（取纯文本）、常见位图。音视频与 PDF 属 V2 之后的版本。");
    }

    // ---------- 打开 ----------

    private async void OnOpenRequested(object? sender, string? path)
    {
        if (_vm is null) return;

        var target = path;
        if (string.IsNullOrEmpty(target))
        {
            target = await PickFileAsync();
            if (target is null) return;   // 用户取消
        }

        Open(target);
    }

    /// <summary>打开一个文件（拖放 / 最近打开 / 选择器都汇到这里）。</summary>
    private void Open(string path)
    {
        if (_vm is null) return;

        SaveProgress();   // 换文件前先存上一个的进度

        var progress = _vm.GetProgress(path);
        if (!Host.Open(path, progress, out var error))
        {
            _vm.OnOpenFailed(error);
            return;
        }

        _vm.OnOpened(path, Host.ReadProgress, Host.Kind == ViewerKind.Text, Host.Kind == ViewerKind.Image);
        ApplyTypography();

        if (Host.Kind == ViewerKind.Image)
            _vm.SetInfo("缩放 " + (Host.Zoom * 100).ToString("0") + "%");
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        SaveProgress();
        Host.Close();
        _vm?.OnClosed();
    }

    // ---------- 缩放 / 排版 ----------

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

    private void ApplyTypography()
    {
        if (_vm is null) return;
        Host.ApplyTextOptions(_vm.FontSize, _vm.LineSpacing, _vm.PagePadding);
    }

    // ---------- 进度 ----------

    private void SaveProgress()
    {
        if (_vm is null || Host.Kind != ViewerKind.Text) return;
        if (string.IsNullOrEmpty(Host.CurrentPath)) return;
        try { _vm.SaveProgress(Host.CurrentPath, Host.ReadProgress); }
        catch { /* 进度是附属产物，写不进去不影响阅读 */ }
    }

    // ---------- 文件选择 / 拖放 ----------

    private async Task<string?> PickFileAsync()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) return null;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要查看的文件",
                AllowMultiple = false,
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
        catch { return null; }
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

        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        Open(path);
    }

    private void OnRecentSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem is not string path) return;
        RecentList.SelectedItem = null;   // 允许重复点同一条
        Open(path);
    }
}
