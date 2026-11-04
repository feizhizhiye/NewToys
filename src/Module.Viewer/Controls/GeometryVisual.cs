using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace NewToys.Viewer.Controls;

/// <summary>
/// 动态几何图案（自绘）：多层旋转多边形 + 顶点连线 + 中心呼吸圆 + 向外扩散的脉冲环。
/// <para>
/// 🔴 配色只取 <see cref="TemplatedControl.Foreground"/>（图案色）与 <see cref="TemplatedControl.Background"/>（底），
/// 由 axaml 用主题键传入；代码里⛔ 不写死色值。
/// </para>
/// <para>🔴 动效由 <see cref="DispatcherTimer"/> 驱动（30 fps）：控件不可见 / 离开视觉树即停表，⛔ 后台空转。</para>
/// </summary>
public sealed class GeometryVisual : TemplatedControl
{
    private const double FrameMs = 33;

    private readonly DispatcherTimer _timer;
    private double _time;
    private IPen? _penMain;
    private IPen? _penSoft;
    private IPen? _penFaint;
    private IBrush? _fillNode;
    private IBrush? _fillCore;
    private Color _tint;

    public GeometryVisual()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameMs) };
        _timer.Tick += (_, _) =>
        {
            _time += FrameMs / 1000.0;
            InvalidateVisual();
        };

        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>开动（已动则无副作用）。</summary>
    public void Start()
    {
        if (_timer.IsEnabled) return;
        _timer.Start();
    }

    /// <summary>停动并重绘一次空白帧（⛔ 留残影）。</summary>
    public void Stop()
    {
        _timer.Stop();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (Background is { } background) context.FillRectangle(background, bounds);

        // 未开动 / 无前景色时只留底色：静止的图案会让人以为「卡住了」
        if (!_timer.IsEnabled) return;
        if (Foreground is not ISolidColorBrush solid) return;
        if (bounds.Width < 80 || bounds.Height < 80) return;

        EnsureBrushes(solid.Color);

        var center = bounds.Center;
        var radius = Math.Min(bounds.Width, bounds.Height) * 0.44;
        var t = _time;

        DrawGuides(context, center, radius);
        DrawRings(context, center, radius, t);
        DrawPulse(context, center, radius, t);
        DrawCore(context, center, radius, t);
    }

    /// <summary>静态同心参考圆（很淡：给旋转的多边形一个「舞台」）。</summary>
    private void DrawGuides(DrawingContext context, Point center, double radius)
    {
        for (var i = 1; i <= 3; i++)
            context.DrawEllipse(null, _penFaint, center, radius * i / 3.0, radius * i / 3.0);
    }

    /// <summary>三层多边形：边数 / 转速 / 半径比 / 线条权重各不相同，顶点之间连成网。</summary>
    private void DrawRings(DrawingContext context, Point center, double radius, double t)
    {
        // EnsureBrushes 已在 Render 里调过，这里取局部非空引用（字段为可空只为跨帧缓存）
        var faint = _penFaint!;
        // 边数, 角速度, 半径比, 起始相位
        var layers = new (int Sides, double Speed, double Scale, double Phase)[]
        {
            (6, 0.55, 1.00, 0.0),
            (5, -0.80, 0.70, 1.1),
            (3, 1.20, 0.42, 2.3),
        };

        var rings = new Point[layers.Length][];
        for (var i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            rings[i] = Ring(center, radius * layer.Scale, layer.Sides, t * layer.Speed + layer.Phase);
        }

        // 网状连线：外环每个顶点连到内环最近的顶点（低透明度，避免糊成一团）
        for (var i = 1; i < rings.Length; i++)
        {
            var outer = rings[i - 1];
            var inner = rings[i];
            for (var j = 0; j < outer.Length; j++)
            {
                var target = inner[j % inner.Length];
                context.DrawLine(faint, outer[j], target);
            }
        }

        for (var i = 0; i < rings.Length; i++)
        {
            var ring = rings[i];
            var pen = i == 0 ? _penMain! : _penSoft!;
            for (var j = 0; j < ring.Length; j++)
            {
                var next = ring[(j + 1) % ring.Length];
                context.DrawLine(pen, ring[j], next);
                context.DrawEllipse(_fillNode, null, ring[j], i == 0 ? 3 : 2, i == 0 ? 3 : 2);
            }
        }
    }

    /// <summary>向外扩散的脉冲环：半径线性放大、透明度线性衰减。</summary>
    private void DrawPulse(DrawingContext context, Point center, double radius, double t)
    {
        for (var k = 0; k < 3; k++)
        {
            var progress = (t * 0.28 + k / 3.0) % 1.0;
            var r = radius * (0.16 + progress * 0.84);
            using (context.PushOpacity((1 - progress) * 0.45))
                context.DrawEllipse(null, _penMain, center, r, r);
        }
    }

    /// <summary>中心呼吸球 + 一圈虚线感内环。</summary>
    private void DrawCore(DrawingContext context, Point center, double radius, double t)
    {
        var breath = 1 + 0.22 * Math.Sin(t * 2.4);
        var core = radius * 0.085 * breath;

        using (context.PushOpacity(0.75))
            context.DrawEllipse(_fillCore, null, center, core, core);
        context.DrawEllipse(null, _penMain, center, core * 1.9, core * 1.9);
    }

    private static Point[] Ring(Point center, double radius, int sides, double angle)
    {
        var points = new Point[sides];
        var step = 2 * Math.PI / sides;
        for (var i = 0; i < sides; i++)
        {
            var a = angle + i * step;
            points[i] = new Point(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
        }

        return points;
    }

    /// <summary>按前景色重建画笔（主题切换 / Foreground 重设时刷新，⛔ 每帧 new）。</summary>
    private void EnsureBrushes(Color tint)
    {
        if (_tint == tint && _penMain is not null) return;

        _tint = tint;
        _penMain = new Pen(new SolidColorBrush(tint, 0.85), 1.4);
        _penSoft = new Pen(new SolidColorBrush(tint, 0.42), 1);
        _penFaint = new Pen(new SolidColorBrush(tint, 0.14), 1);
        _fillNode = new SolidColorBrush(tint, 0.95);
        _fillCore = new SolidColorBrush(tint, 0.55);
    }
}
