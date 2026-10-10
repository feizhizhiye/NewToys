using Avalonia.Data.Converters;
using System.Globalization;

namespace NewToys.Shell.Converters;

public class BoolInversionConverter : IValueConverter
{
    public static readonly BoolInversionConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}