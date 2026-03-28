using System.Globalization;
using Microsoft.Maui.Controls;

namespace ClaudeUsageTracker.Maui.Converters;

public class CountToBoolConverter : IValueConverter
{
    // Default: returns true when count == 0 (for empty state visibility)
    // With ConverterParameter=inverse: returns true when count > 0 (for content visibility)
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var inverse = parameter as string == "inverse";
        return inverse ? count > 0 : count == 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
