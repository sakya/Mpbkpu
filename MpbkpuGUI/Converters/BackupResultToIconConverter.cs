using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MpbkpuLib.DataModel;
using Material.Icons;

namespace MpbkpuGUI.Converters;

public class BackupResultToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Backup.Results? result = (Backup.Results?)value;
        if (result != null) {
            switch (result) {
                case Backup.Results.Error:
                    return MaterialIconKind.Alert;
                case Backup.Results.Cancelled:
                    return MaterialIconKind.Alert;
                case Backup.Results.Completed:
                    return MaterialIconKind.CheckCircle;
                case Backup.Results.CompletedWithWarnings:
                    return MaterialIconKind.AlertCircle;
            };
        }

        return null;

    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}