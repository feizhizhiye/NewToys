using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace NewToys.Viewer.Controls;

/// <summary>信息浮层里的一行（键 + 值）。</summary>
public sealed class ImageInfoRow
{
    public string Label { get; init; } = "";

    public string Value { get; init; } = "";
}

/// <summary>图片渲染器：缩放 / 平移 / 1:1 与适应窗口。（WHY: VW-005）</summary>
public partial class ImageViewer : UserControl
{
    /// <summary>解码尺寸下限（像素）：小屏 / 折叠态也别解得太糊。</summary>
    private const int MinDecodeSide = 1200;

    /// <summary>解码尺寸上限（像素）：再大也只是吃内存，屏幕上看不出差别。</summary>
    private const int MaxDecodeSide = 4096;

    private Bitmap? _bitmap;
    private int _pixelWidth;
    private int _pixelHeight;
    private int _originalWidth;
    private int _originalHeight;
    private int _rotation;   // 0 / 90 / 180 / 270
    private int _loadToken;  // 丢弃过期的「适应窗口」回调

    private string _sourcePath = "";
    private string _archivePath = "";
    private long _sourceSize = -1;
    private bool _fromArchive;

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
            using var stream = File.OpenRead(path);
            if (!Load(stream, out error)) return false;

            _fromArchive = false;
            _sourcePath = path;
            _archivePath = "";
            _sourceSize = -1;
            try { _sourceSize = new FileInfo(path).Length; } catch { /* 大小读不到就不显示 */ }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>从流载入图片（压缩包内的图片走这条；调用方负责关闭流）。</summary>
    public bool Load(Stream stream, out string error)
    {
        error = "";
        try
        {
            var bmp = Decode(stream, DecodeSideLimit(), out var originalWidth, out var originalHeight);

            _pixelWidth = bmp.PixelSize.Width;
            _pixelHeight = bmp.PixelSize.Height;
            _originalWidth = originalWidth;
            _originalHeight = originalHeight;

            _sourcePath = "";
            _sourceSize = -1;
            _fromArchive = false;
            _archivePath = "";

            ApplyNewBitmap(bmp);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>显示新位图：换源 → 按视口适应 → 下一轮再兜底算一次并恢复滚动条。（WHY: VW-006）</summary>
    private void ApplyNewBitmap(Bitmap bmp)
    {
        var token = ++_loadToken;

        Scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

        // 🔴 先换源再释放旧图：反了的话「已 Dispose 但仍挂在 Image.Source 上」会有一帧取不到像素
        var previous = _bitmap;
        _bitmap = bmp;
        _rotation = 0;
        Pic.RenderTransform = null;
        Pic.Source = bmp;
        previous?.Dispose();

        Zoom = 1;
        ApplyLayout();
        FitToWindow();

        Dispatcher.UIThread.Post(() =>
        {
            if (token != _loadToken) return;

            FitToWindow();
            Scroller.Offset = new Vector(0, 0);
            Scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }, DispatcherPriority.Loaded);

        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 解码图片：原图超过 <paramref name="maxSide"/> 时**降采样解码**（内存按面积平方级下降）。
    /// <para>🔴 必须先探测原始尺寸：<c>DecodeToWidth/Height</c> 在目标大于原图时**会放大**，
    /// 不看尺寸就调用会让小图反而多吃几倍内存。</para>
    /// </summary>
    private static Bitmap Decode(Stream stream, int maxSide, out int width, out int height)
    {
        var origin = stream.CanSeek ? stream.Position : 0;

        if (ImageHeader.TryReadSize(stream, out width, out height) && (width > maxSide || height > maxSide))
        {
            stream.Position = origin;
            return width >= height
                ? Bitmap.DecodeToWidth(stream, maxSide)
                : Bitmap.DecodeToHeight(stream, maxSide);
        }

        var bmp = new Bitmap(stream);
        width = bmp.PixelSize.Width;
        height = bmp.PixelSize.Height;
        return bmp;
    }

    /// <summary>解码边长上限：视口物理像素宽 × 2（给放大留余量），夹在 <c>1200 ~ 4096</c>。</summary>
    private int DecodeSideLimit()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

        var viewport = Scroller.Viewport.Width;
        if (viewport <= 1) viewport = Bounds.Width;
        if (viewport <= 1) viewport = 1280;

        var px = (int)Math.Ceiling(viewport * scaling * 2);
        return Math.Clamp(px, MinDecodeSide, MaxDecodeSide);
    }

    /// <summary>
    /// 标注来源为压缩包内条目（<paramref name="display"/> 形如 <c>包路径!条目名</c>）。
    /// <para>宿主在 <see cref="Load(Stream, out string)"/> 之后调用；⛔ 控件自己不解析 <c>!</c>。</para>
    /// </summary>
    public void SetArchiveSource(string display, string archivePath, long size)
    {
        _fromArchive = true;
        _sourcePath = display ?? "";
        _archivePath = archivePath ?? "";
        _sourceSize = size;
    }

    /// <summary>当前图片的来源（本地全路径，或压缩包内 <c>包路径!条目名</c>）。</summary>
    public string SourcePath => _sourcePath;

    /// <summary>信息浮层是否打开（宿主据此让开滚轮 / 方向键，免得翻页把卡片里的数字换成别的图）。</summary>
    public bool IsInfoVisible => InfoPanel.IsVisible;

    /// <summary>释放当前位图（宿主换内容 / 关闭时调用）。</summary>
    public void Close()
    {
        _loadToken++;   // 让排队中的「适应窗口」回调失效
        Pic.Source = null;
        Pic.RenderTransform = null;
        PicBox.Width = double.NaN;
        PicBox.Height = double.NaN;
        _bitmap?.Dispose();
        _bitmap = null;
        _pixelWidth = 0;
        _pixelHeight = 0;
        _originalWidth = 0;
        _originalHeight = 0;
        _rotation = 0;
        _sourcePath = "";
        _archivePath = "";
        _sourceSize = -1;
        _fromArchive = false;
        Scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        InfoPanel.IsVisible = false;
    }

    /// <summary>适应窗口（只缩小，不放大）。</summary>
    public void FitToWindow()
    {
        if (_bitmap is null) return;
        var vw = Scroller.Viewport.Width;
        var vh = Scroller.Viewport.Height;
        if (vw <= 1 || vh <= 1) return;

        var swap = _rotation is 90 or 270;
        var w = swap ? _pixelHeight : _pixelWidth;
        var h = swap ? _pixelWidth : _pixelHeight;

        var scale = Math.Min(vw / w, vh / h);
        SetZoom(Math.Min(scale, 1.0));
    }

    /// <summary>1:1 原始像素。</summary>
    public void ResetZoom() => SetZoom(1);

    /// <summary>当前旋转角度（0 / 90 / 180 / 270）。</summary>
    public int Rotation => _rotation;

    /// <summary>顺时针旋转 90°（四次一圈）。</summary>
    public void Rotate()
    {
        if (_bitmap is null) return;

        _rotation = (_rotation + 90) % 360;
        // 原本是「缩小到适应窗口」的状态就重新适应（转 90° 后外框比例反了）
        var refit = Zoom < 0.999;
        ApplyLayout();
        if (refit) FitToWindow();
    }

    /// <summary>按当前缩放 + 旋转角重排占位尺寸（唯一改 <see cref="Image.Width"/> 的地方）。</summary>
    private void ApplyLayout()
    {
        if (_bitmap is null) return;

        var swap = _rotation is 90 or 270;
        var shownW = Math.Max(1, _pixelWidth * Zoom);
        var shownH = Math.Max(1, _pixelHeight * Zoom);

        // 内层：未旋转的画布尺寸（图按它被拉伸绘制）
        Pic.Width = shownW;
        Pic.Height = shownH;
        Pic.RenderTransform = _rotation == 0 ? null : new RotateTransform(_rotation);

        // 外层：旋转后的外框（90°/270° 宽高互换）⇒ ScrollViewer 的 Extent 跟得上
        PicBox.Width = swap ? shownH : shownW;
        PicBox.Height = swap ? shownW : shownH;
    }

    /// <summary>按倍数缩放（<paramref name="factor"/> &gt; 1 放大）。</summary>
    public void ZoomBy(double factor) => SetZoom(Zoom * factor);

    private void SetZoom(double zoom)
    {
        if (_bitmap is null) return;
        Zoom = Math.Clamp(zoom, 0.05, 16);
        ApplyLayout();
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

    // ==================== 右键菜单 / 图片信息 ====================

    private void OnImageInfoClick(object? sender, RoutedEventArgs e) => ShowInfo();

    private void OnFitClick(object? sender, RoutedEventArgs e) => FitToWindow();

    private void OnResetZoomClick(object? sender, RoutedEventArgs e) => ResetZoom();

    private void OnRotateClick(object? sender, RoutedEventArgs e) => Rotate();

    private void OnCloseInfoClick(object? sender, RoutedEventArgs e) => HideInfo();

    /// <summary>弹出信息浮层（没有图片时忽略）。</summary>
    private void ShowInfo()
    {
        if (_bitmap is null) return;

        InfoList.ItemsSource = BuildInfoRows();
        InfoHint.Text = "";
        InfoPanel.IsVisible = true;
        InfoPanel.Focus();
    }

    private void HideInfo()
    {
        InfoPanel.IsVisible = false;
        Scroller.Focus();
    }

    /// <summary>点卡片外的遮罩即关闭；点卡片本身不关。</summary>
    private void OnInfoBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (InfoCard.IsPointerOver) return;
        HideInfo();
    }

    private void OnInfoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        HideInfo();
    }

    private async void OnCopyPathClick(object? sender, RoutedEventArgs e)
    {
        if (!InfoPanel.IsVisible) ShowInfo();

        if (_sourcePath.Length == 0) { InfoHint.Text = "没有可复制的路径。"; return; }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) { InfoHint.Text = "剪贴板不可用。"; return; }

            await clipboard.SetTextAsync(_sourcePath);
            InfoHint.Text = "已复制路径。";
        }
        catch (Exception ex) { InfoHint.Text = "⚠ 复制失败：" + ex.Message; }
    }

    private IReadOnlyList<ImageInfoRow> BuildInfoRows()
    {
        var rows = new List<ImageInfoRow>();
        var name = _fromArchive ? EntryName() : Path.GetFileName(_sourcePath);

        if (name.Length > 0) rows.Add(InfoRow("名称", name));

        rows.Add(InfoRow("尺寸", $"{_originalWidth} × {_originalHeight} 像素"));

        // 大图按显示精度降采样解码（省内存）；这里说明清楚，免得用户以为「1:1」不准
        if (_pixelWidth != _originalWidth || _pixelHeight != _originalHeight)
            rows.Add(InfoRow("解码尺寸", $"{_pixelWidth} × {_pixelHeight} 像素（按屏幕精度降采样）"));

        if (_rotation != 0) rows.Add(InfoRow("旋转", _rotation + "°"));

        var shownWidth = Math.Max(1, Math.Round(_pixelWidth * Zoom));
        var shownHeight = Math.Max(1, Math.Round(_pixelHeight * Zoom));
        rows.Add(InfoRow("当前显示",
            (Zoom * 100).ToString("0.#") + "%（" + shownWidth + " × " + shownHeight + " 像素）"));

        if (_sourceSize >= 0) rows.Add(InfoRow("文件大小", FormatSize(_sourceSize)));

        if (_fromArchive)
        {
            rows.Add(InfoRow("位置", _archivePath));
            rows.Add(InfoRow("包内路径", EntryName()));
        }
        else if (_sourcePath.Length > 0)
        {
            try
            {
                rows.Add(InfoRow("修改时间", File.GetLastWriteTime(_sourcePath).ToString("yyyy-MM-dd HH:mm:ss")));
            }
            catch { /* 只读设备 / 文件已删 → 不显示 */ }

            var dir = Path.GetDirectoryName(_sourcePath);
            if (!string.IsNullOrEmpty(dir)) rows.Add(InfoRow("位置", dir));
        }

        var ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        if (ext.Length > 0) rows.Add(InfoRow("类型", ext + " 图片"));

        return rows;
    }

    /// <summary>压缩包内条目名（<c>包路径!条目</c> 去掉包路径前缀）。</summary>
    private string EntryName()
    {
        var prefix = _archivePath + "!";
        return _sourcePath.StartsWith(prefix, StringComparison.Ordinal)
            ? _sourcePath[prefix.Length..]
            : _sourcePath;
    }

    private static ImageInfoRow InfoRow(string label, string value) => new() { Label = label, Value = value };

    private static string FormatSize(long bytes)
    {
        const double Kb = 1024d;
        if (bytes < Kb) return bytes + " B";
        if (bytes < Kb * 1024) return (bytes / Kb).ToString("0.0") + " KB";
        if (bytes < Kb * 1024 * 1024) return (bytes / (Kb * 1024)).ToString("0.00") + " MB";
        return (bytes / (Kb * 1024 * 1024)).ToString("0.00") + " GB";
    }
}
