using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MpbkpuLib.DataModel;
using Material.Icons;

namespace MpbkpuGUI.Converters;

public class ItemTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        File.FileTyes? result = (File.FileTyes?)value;
        if (result != null) {
            switch (result) {
                case File.FileTyes.Directory:
                    return MaterialIconKind.Folder;
                case File.FileTyes.File:
                    return MaterialIconKind.File;
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}