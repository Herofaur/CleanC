using System.Globalization;
using System.Windows.Data;
using CleanC.Core.Models;

namespace CleanC.App.Converters;

public sealed class CleanupLevelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CleanupLevel.Recommended ? "建议清理" : "需要确认";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
