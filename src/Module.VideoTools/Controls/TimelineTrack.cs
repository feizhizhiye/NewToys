using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.Controls;

/// <summary>
/// 时间轴轨道：「预览位置」那条滑条的替代品 —— 一条**能看见剪辑区间**的轨道。
/// <para>点一下 / 按住拖动 = 移动预览位置（等价拖滑条）。区间块按真实比例铺满整条轨道，
/// 多段剪切时逐段画色块，一眼看清剪掉的是哪几段。开始 / 结束的微调仍由下面两条滑条负责。</para>
/// <para>🔴 颜色一律由外部用 XAML <c>DynamicResource</c> 传进来，
/// 控件内⛔ 不写死色值；主题切换时 Brush 对象被替换 → <see cref="AffectsRender{T}"/> 自动重绘。</para>
/// <para>🔴 拖动改写 <see cref="Position"/> 必须用 <c>SetCurrentValue</c>：直接 <c>SetValue</c> 会把
/// VM 那边的 TwoWay 绑定**整条断掉**（拖一次之后滑条再也不跟数据走）。</para>
/// </summary>
public sealed class TimelineTrack : Control
{
    /// <summary>左右内缩：和下面两条 Slider 的轨道端点对齐（Fluent thumb 半径 ≈ 10）。</summary>
    private const double RailPad = 10;

    private const double RailHeight = 8;
    private const double LabelSize = 10;

    /// <summary>可选刻度间隔（秒）：宽了就细分，窄了就粗分。</summary>
    private static readonly double[] TickSteps =
        { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400 };

    // ==================== 属性 ====================

    /// <summary>总时长（秒）。≤0 = 还没探测出时长 → 只画空轨道，不画刻度 / 播放头。</summary>
    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<TimelineTrack, double>(nameof(Duration));

    /// <summary>当前预览位置（秒）；拖动会写回这里。</summary>
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<TimelineTrack, double>(nameof(Position), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>剪辑区间起点（秒）。</summary>
    public static readonly StyledProperty<double> StartProperty =
        AvaloniaProperty.Register<TimelineTrack, double>(nameof(Start));

    /// <summary>剪辑区间终点（秒；0 = 到片尾）。</summary>
    public static readonly StyledProperty<double> EndProperty =
        AvaloniaProperty.Register<TimelineTrack, double>(nameof(End));

    /// <summary>多段剪切的解析结果（≥2 段时优先于 Start/End 画）。</summary>
    public static readonly StyledProperty<IReadOnlyList<CutSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<TimelineTrack, IReadOnlyList<CutSegment>?>(nameof(Segments));

    /// <summary>是否已勾选「剪辑」——未勾选时区间块淡显（看得见但知道还没生效）。</summary>
    public static readonly StyledProperty<bool> IsRangeEnabledProperty =
        AvaloniaProperty.Register<TimelineTrack, bool>(nameof(IsRangeEnabled));

    public static readonly StyledProperty<IBrush?> RailBrushProperty =
        AvaloniaProperty.Register<TimelineTrack, IBrush?>(nameof(RailBrush));

    public static readonly StyledProperty<IBrush?> RangeBrushProperty =
        AvaloniaProperty.Register<TimelineTrack, IBrush?>(nameof(RangeBrush));

    public static readonly StyledProperty<IBrush?> SegmentBrushProperty =
        AvaloniaProperty.Register<TimelineTrack, IBrush?>(nameof(SegmentBrush));

    public static readonly StyledProperty<IBrush?> PositionBrushProperty =
        AvaloniaProperty.Register<TimelineTrack, IBrush?>(nameof(PositionBrush));

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<TimelineTrack, IBrush?>(nameof(TickBrush));

    public double Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double Start { get => GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public double End { get => GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public IReadOnlyList<CutSegment>? Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public bool IsRangeEnabled { get => GetValue(IsRangeEnabledProperty); set => SetValue(IsRangeEnabledProperty, value); }
    public IBrush? RailBrush { get => GetValue(RailBrushProperty); set => SetValue(RailBrushProperty, value); }
    public IBrush? RangeBrush { get => GetValue(RangeBrushProperty); set => SetValue(RangeBrushProperty, value); }
    public IBrush? SegmentBrush { get => GetValue(SegmentBrushProperty); set => SetValue(SegmentBrushProperty, value); }
    public IBrush? PositionBrush { get => GetValue(PositionBrushProperty); set => SetValue(PositionBrushProperty, value); }
    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    static TimelineTrack()
    {
        AffectsRender<TimelineTrack>(
            DurationProperty, PositionProperty, StartProperty, EndProperty, SegmentsProperty,
            IsRangeEnabledProperty, RailBrushProperty, RangeBrushProperty, SegmentBrushProperty,
            PositionBrushProperty, TickBrushProperty);
    }

    // ==================== 交互 ====================

    private bool _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Duration <= 0) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        e.Pointer.Capture(this);
        Seek(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        Seek(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>把指针 X 换算成时间点写回 <see cref="Position"/>（clamp 到 [0, Duration]）。</summary>
    private void Seek(double x)
    {
        var railW = Bounds.Width - RailPad * 2;
        if (railW <= 0 || Duration <= 0) return;

        var t = (x - RailPad) / railW * Duration;
        if (t < 0) t = 0;
        if (t > Duration) t = Duration;

        SetCurrentValue(PositionProperty, t);   // ⛔ 别用 SetValue：会掐断 TwoWay 绑定
    }

    // ==================== 绘制 ====================

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        // 自定义控件要能收到指针事件就得先有一次实际绘制（Transparent 也算）
        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));

        var railW = w - RailPad * 2;
        if (railW <= 0) return;

        // 轨道略微下移：上方留给刻度文字，下方留给刻度短线
        var railTop = (h - RailHeight) / 2 + 4;
        if (RailBrush is { } rail)
            context.DrawRectangle(rail, null, new Rect(RailPad, railTop, railW, RailHeight),
                RailHeight / 2, RailHeight / 2);

        var duration = Duration;
        if (duration <= 0) return;

        if (IsRangeEnabled) DrawRanges(context, railTop, railW, duration);
        else
        {
            using (context.PushOpacity(0.35))
                DrawRanges(context, railTop, railW, duration);
        }

        DrawTicks(context, railTop, railW, duration);
        DrawPlayhead(context, railTop, railW, duration);
    }

    /// <summary>区间块：多段（≥2）逐段画，否则画单段 Start→End。判定与 ffmpeg 命令一致。</summary>
    private void DrawRanges(DrawingContext ctx, double railTop, double railW, double duration)
    {
        double X(double t) => RailPad + Math.Clamp(t / duration, 0, 1) * railW;

        var segs = Segments;
        if (segs is { Count: >= 2 })
        {
            var brush = SegmentBrush ?? RangeBrush;
            if (brush is null) return;
            foreach (var s in segs)
            {
                var x1 = X(s.Start);
                var x2 = X(s.End);
                if (x2 - x1 < 2) x2 = x1 + 2;
                ctx.DrawRectangle(brush, null, new Rect(x1, railTop, x2 - x1, RailHeight),
                    RailHeight / 2, RailHeight / 2);
            }
            return;
        }

        var end = End > Start ? End : duration;   // 结束=0 在老逻辑里等于"到片尾"
        if (end <= Start) return;
        if (RangeBrush is not { } rangeBrush) return;

        var rx1 = X(Start);
        var rx2 = X(end);
        if (rx2 - rx1 < 2) return;
        ctx.DrawRectangle(rangeBrush, null, new Rect(rx1, railTop, rx2 - rx1, RailHeight),
            RailHeight / 2, RailHeight / 2);
    }

    private void DrawTicks(DrawingContext ctx, double railTop, double railW, double duration)
    {
        if (TickBrush is not { } brush) return;

        // 标签数按可用宽度定：留 76px 一个，最少 2 个
        var maxLabels = Math.Max(2, (int)(railW / 76));
        var step = TickSteps[^1];
        foreach (var s in TickSteps)
        {
            if (duration / s <= maxLabels) { step = s; break; }
        }

        var pen = new Pen(brush, 1);
        var railBottom = railTop + RailHeight;
        var typeface = Typeface.Default;
        var labelY = railTop - LabelSize - 4;

        for (var t = 0.0; t <= duration + 1e-6; t += step)
        {
            var x = Math.Min(RailPad + t / duration * railW, RailPad + railW);
            ctx.DrawLine(pen, new Point(x, railBottom), new Point(x, railBottom + 3));

            var text = new FormattedText(FormatTick(t), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, LabelSize, brush);
            // ⚠️ 极窄时 Width - text.Width 会是负数，Math.Clamp(min>max) 直接抛 ⇒ 先夹到 0
            var maxX = Math.Max(0, Bounds.Width - text.Width);
            var tx = Math.Clamp(x - text.Width / 2, 0, maxX);
            ctx.DrawText(text, new Point(tx, labelY));
        }
    }

    private void DrawPlayhead(DrawingContext ctx, double railTop, double railW, double duration)
    {
        if (PositionBrush is not { } brush) return;

        var x = RailPad + Math.Clamp(Position / duration, 0, 1) * railW;
        ctx.DrawLine(new Pen(brush, 2),
            new Point(x, railTop - 6),
            new Point(x, railTop + RailHeight + 4));
        ctx.DrawEllipse(brush, null, new Point(x, railTop), 4, 4);
    }

    /// <summary>刻度文字：不足 1 小时用 <c>mm:ss</c>，超过用 <c>h:mm:ss</c>。</summary>
    private static string FormatTick(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }
}
