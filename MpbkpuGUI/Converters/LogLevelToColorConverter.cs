using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MpbkpuLib.Abstracts;

namespace MpbkpuGUI.Converters;

public class LogLevelToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = (BaseJobSettings.LogLevels?)value;
        if (level != null) {
            Color? c = null;
            switch (level.Value) {
                case BaseJobSettings.LogLevels.Debug:
                    c = App.GetStyleColor("DebugColor");
                    break;
                case BaseJobSettings.LogLevels.Info:
                    c = App.GetStyleColor("InfoColor");
                    break;
                case BaseJobSettings.LogLevels.Warning:
                    c = App.GetStyleColor("WarningColor");
                    break;
                case BaseJobSettings.LogLevels.Error:
                    c = App.GetStyleColor("DangerColor");
                    break;
            }

            if (c != null)
                return new SolidColorBrush(c.Value);
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}