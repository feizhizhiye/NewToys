using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace NewToys.Shell.Converters;

/// <summary>把 Orientation 转成 Track.IsDirectionReversed： Orientation.Vertical → true，Orientation.Horizontal → false。（WHY: SHL-016）</summary>
public sealed class VerticalDirectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Orientation.Vertical;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
