using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MpbkpuGUI.Converters;

/// <summary>
/// Converts an enum value to the localized string with key "{parameter}{value}" (e.g. "Result_Completed")
/// </summary>
public class EnumToLocalizedStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        return Localizer.Localizer.Instance[$"{parameter}{value}"];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
