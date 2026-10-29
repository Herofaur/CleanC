using System.Globalization;
using System.Windows.Data;
using CleanC.Core.Formatting;

namespace CleanC.App.Converters;

public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long bytes ? SizeFormatter.Format(bytes) : "0 B";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
