using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Converters;

public class ItemTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        File.FileTyes? result = (File.FileTyes?)value;
        if (result != null) {
            switch (result) {
                case File.FileTyes.Directory:
                    return "fas fa-folder";
                case File.FileTyes.File:
                    return "fas fa-file";
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}