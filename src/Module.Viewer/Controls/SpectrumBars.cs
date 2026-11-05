using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using NewToys.Shared.Services;

namespace NewToys.Viewer.Controls;

/// <summary>分频柱（自绘）：等高竖条，柱高 = 频段能量。（WHY: VW-007）</summary>
public sealed class SpectrumBars : TemplatedControl
{
    private readonly float[] _bands = new float[32];

    /// <summary>频谱数据源（宿主注入；为空则不画）。</summary>
    public IAudioSpectrum? Source { get; set; }

    /// <summary>相邻柱之间的空隙。</summary>
    public double Gap { get; set; } = 2;

    /// <summary>拉一帧并重绘；无数据（未播放）时自动隐藏。</summary>
    public void Pump()
    {
        if (Source is null)
        {
            IsVisible = false;
            return;
        }

        var count = Source.ReadSpectrum(_bands);
        IsVisible = count > 0;
        if (count <= 0) return;

        InvalidateVisual();
    }

    /// <summary>停表时收起（⛔ 留在界面上当装饰）。</summary>
    public void Hide() => IsVisible = false;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var brush = Foreground;
        if (brush is null) return;

        var count = Source?.BandCount ?? 0;
        if (count <= 0) count = _bands.Length;

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var slot = width / count;
        var barWidth = Math.Max(1, slot - Gap);

        for (var i = 0; i < count && i < _bands.Length; i++)
        {
            var value = Math.Clamp(_bands[i], 0f, 1f);
            var barHeight = Math.Max(1, value * height);
            var x = i * slot + (slot - barWidth) / 2;
            context.FillRectangle(brush, new Rect(x, height - barHeight, barWidth, barHeight));
        }
    }
}
