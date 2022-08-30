using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Converters;

public class IsBackupJobConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is BackupJob;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}