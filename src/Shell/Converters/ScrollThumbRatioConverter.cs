using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NewToys.Shell.Converters;

/// <summary>把 Avalonia.Controls.Primitives.ScrollBar.ViewportSize 按 Ratio 缩小后再交给 Track： 滑块（thumb）按 viewport / (range + viewport) 算长度，缩小 viewport ⇒ 滑块更短， 而 Track 的定位公式用的是同一个 thumbLength ⇒ 拖到两端仍然贴边（视觉缩短不会…。（WHY: SHL-015）</summary>
public sealed class ScrollThumbRatioConverter : IValueConverter
{
    /// <summary>缩放系数（&lt;1 = 更短）。默认 0.55。</summary>
    public double Ratio { get; set; } = 0.55;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && !double.IsNaN(d) && !double.IsInfinity(d) ? d * Ratio : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
