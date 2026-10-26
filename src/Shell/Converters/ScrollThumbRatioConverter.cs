using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NewToys.Shell.Converters;

/// <summary>
/// 把 <see cref="Avalonia.Controls.Primitives.ScrollBar.ViewportSize"/> 按 <see cref="Ratio"/> 缩小后再交给 <c>Track</c>：
/// 滑块（thumb）按 `viewport / (range + viewport)` 算长度，缩小 viewport ⇒ 滑块更短，
/// 而 <c>Track</c> 的定位公式用的是**同一个** thumbLength ⇒ 拖到两端仍然贴边（视觉缩短不会「拖不到底」）。
/// <para>
/// ⚠️ 为什么不用 <c>ScaleTransform</c> 缩 Thumb：那只改渲染不改布局，滑块会「居中缩」——
/// 拖到底时底部空出一截，看起来就是**拖不到底**（2026-10-24 实锤）。
/// </para>
/// </summary>
public sealed class ScrollThumbRatioConverter : IValueConverter
{
    /// <summary>缩放系数（&lt;1 = 更短）。默认 0.55。</summary>
    public double Ratio { get; set; } = 0.55;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && !double.IsNaN(d) && !double.IsInfinity(d) ? d * Ratio : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
