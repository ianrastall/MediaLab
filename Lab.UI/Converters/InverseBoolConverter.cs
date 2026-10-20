using System.Globalization;
using System.Windows.Data;

namespace Lab.UI.Converters;

/// <summary>Returns !value for bool bindings (e.g. IsEnabled when IsBusy).</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;
}
