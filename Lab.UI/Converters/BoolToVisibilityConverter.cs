using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lab.UI.Converters;

/// <summary>
/// Converts bool to Visibility. Pass ConverterParameter="invert" to flip so
/// true → Collapsed and false → Visible.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        return (flag ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}
