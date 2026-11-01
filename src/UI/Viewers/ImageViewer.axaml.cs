using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace NewToys.UI.Viewers;

/// <summary>
/// 图片渲染器：缩放 / 平移 / 1:1 与适应窗口。
/// <para>
/// 🔴 缩放直接改 <see cref="Image.Width"/> / <c>Height</c>（不是 <c>RenderTransform</c>）——
/// 只有占位尺寸变了，<see cref="ScrollViewer.Extent"/> 才会跟上，放大后能拖到边缘。
/// </para>
/// <para>🔴 不解码降采样（Avalonia <see cref="Bitmap"/> 没有公开的「限定像素尺寸」重载）——
/// 超大图首帧会卡一下，V1 接受；要优化就另起后台线程解码成小图再换源。</para>
/// </summary>
public partial class ImageViewer : UserControl
{
    private Bitmap? _bitmap;
    private int _pixelWidth;
    private int _pixelHeight;

    private bool _dragging;
    private Point _dragOrigin;
    private Vector _dragStartOffset;

    /// <summary>当前缩放（1.0 = 原始像素）。</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>缩放变化（宿主用来刷新状态栏百分比）。</summary>
    public event EventHandler? ZoomChanged;

    public ImageViewer()
    {
        InitializeComponent();
        // 双击切 1:1 / 适应窗口
        Pic.DoubleTapped += (_, _) => { if (Math.Abs(Zoom - 1) < 0.001) FitToWindow(); else SetZoom(1); };
        // Ctrl + 滚轮缩放
        Scroller.PointerWheelChanged += OnWheel;
        // 按住左键拖拽平移
        Scroller.PointerPressed += OnPress;
        Scroller.PointerMoved += OnMove;
        Scroller.PointerReleased += OnRelease;
    }

    /// <summary>载入图片；失败时返回 false 并给出原因。</summary>
    public bool Load(string path, out string error)
    {
        error = "";
        try
        {
            var bmp = new Bitmap(path);
            _pixelWidth = bmp.PixelSize.Width;
            _pixelHeight = bmp.PixelSize.Height;

            _bitmap?.Dispose();
            _bitmap = bmp;
            Pic.Source = bmp;
            SetZoom(1);
            // 首次显示按「适应窗口」，等布局有 Viewport 再算
            Dispatcher.UIThread.Post(FitToWindow, DispatcherPriority.Loaded);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>适应窗口（只缩小，不放大）。</summary>
    public void FitToWindow()
    {
        if (_bitmap is null) return;
        var vw = Scroller.Viewport.Width;
        var vh = Scroller.Viewport.Height;
        if (vw <= 1 || vh <= 1) return;

        var scale = Math.Min(vw / _pixelWidth, vh / _pixelHeight);
        SetZoom(Math.Min(scale, 1.0));
    }

    /// <summary>1:1 原始像素。</summary>
    public void ResetZoom() => SetZoom(1);

    /// <summary>按倍数缩放（<paramref name="factor"/> &gt; 1 放大）。</summary>
    public void ZoomBy(double factor) => SetZoom(Zoom * factor);

    private void SetZoom(double zoom)
    {
        if (_bitmap is null) return;
        Zoom = Math.Clamp(zoom, 0.05, 16);
        Pic.Width = Math.Max(1, _pixelWidth * Zoom);
        Pic.Height = Math.Max(1, _pixelHeight * Zoom);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        ZoomBy(e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
    }

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Scroller).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _dragOrigin = e.GetPosition(Scroller);
        _dragStartOffset = Scroller.Offset;
        e.Pointer.Capture(Scroller);
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Scroller);
        Scroller.Offset = new Vector(
            _dragStartOffset.X - (p.X - _dragOrigin.X),
            _dragStartOffset.Y - (p.Y - _dragOrigin.Y));
    }

    private void OnRelease(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }
}
