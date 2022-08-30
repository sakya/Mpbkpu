using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MpbkpuGUI.Converters;

public class BytesToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = (long?)value ?? 0;
        return MpbkpuLib.Utility.String.BytesToString(bytes);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}