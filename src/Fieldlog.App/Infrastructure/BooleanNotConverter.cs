using System.Globalization;
using System.Windows.Data;

namespace Fieldlog.App.Infrastructure;

/// <summary>
/// Inverts a boolean binding value; used to disable the endpoint box while connected.
/// </summary>
public sealed class BooleanNotConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag ? !flag : true;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag ? !flag : false;
}
