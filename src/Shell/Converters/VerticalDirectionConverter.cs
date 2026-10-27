using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace NewToys.Shell.Converters;

/// <summary>
/// 把 <see cref="Orientation"/> 转成 <c>Track.IsDirectionReversed</c>：
/// <see cref="Orientation.Vertical"/> → <c>true</c>，<see cref="Orientation.Horizontal"/> → <c>false</c>。
/// <para>
/// 🔴 <c>Track</c> 的垂直方向默认是「值增大 ⇒ 滑块向上」，与滚动条「值增大 ⇒ 内容下滚 / 滑块下移」相反，
/// 必须反转；水平方向默认已是从左往右，不能反转。
/// </para>
/// </summary>
public sealed class VerticalDirectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Orientation.Vertical;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
