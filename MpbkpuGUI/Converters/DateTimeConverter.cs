using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MpbkpuGUI.Converters;

public class DateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        DateTime? dt = (DateTime?)value;
        if (dt != null) {
            if (dt.Value.Kind != DateTimeKind.Local)
                return dt.Value.ToLocalTime().ToString();
            return dt.Value.ToString();
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}