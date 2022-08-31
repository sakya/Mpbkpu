using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Converters;

public class BackupResultToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Backup.Results? result = (Backup.Results?)value;
        if (value != null) {
            Color? color = result switch
            {
                Backup.Results.Error => App.GetStyleColor("ErrorColor"),
                Backup.Results.Cancelled => App.GetStyleColor("WarningColor"),
                Backup.Results.Completed => App.GetStyleColor("SuccessColor"),
                _ => null
            };

            if (color != null)
                return new SolidColorBrush(color.Value);
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}