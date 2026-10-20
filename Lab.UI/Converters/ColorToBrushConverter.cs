using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Lab.UI.Converters;

/// <summary>Converts a <see cref="Color"/> to a frozen <see cref="SolidColorBrush"/> for backgrounds.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? brush.Color : Colors.Transparent;
}
