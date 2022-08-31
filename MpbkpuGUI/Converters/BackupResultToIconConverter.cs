using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Converters;

public class BackupResultToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Backup.Results? result = (Backup.Results?)value;
        if (result != null) {
            switch (result) {
                case Backup.Results.Error:
                    return "fas fa-exclamation-triangle";
                case Backup.Results.Cancelled:
                    return "fas fa-exclamation-triangle";
                case Backup.Results.Completed:
                    return "fas fa-check-circle";
            };
        }

        return null;

    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}