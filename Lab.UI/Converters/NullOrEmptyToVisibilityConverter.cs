using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lab.UI.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound value is null/empty, otherwise
/// <see cref="Visibility.Collapsed"/>. Set ConverterParameter="invert" to flip.
/// </summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        var visible = invert ? !isEmpty : isEmpty;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
