using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Controls;

public partial class PositionDetail : UserControl
{
    enum FileSystemTypes
    {
        FileSystem,
        DropBox,
        Ftp,
        MediaFire,
        Mega,
        Nextcloud,
        OneDrive,
        Sftp,
        Smb
    }

    private Job.Position _position;

    public static readonly DirectProperty<PositionDetail, Job.Position?> ValueProperty =
        AvaloniaProperty.RegisterDirect<PositionDetail, Job.Position?>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v);

    public PositionDetail()
    {
        _position = new Job.Position();
        InitializeComponent();
    }

    public Job.Position? Value
    {
        get => _position;
        set
        {
            if (SetAndRaise(ValueProperty, ref _position, value)) {
                DataContext = _position;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        var cmbType = this.FindControl<ComboBox>("CmbType");
        cmbType.Items = new List<FileSystemTypes>()
        {
            FileSystemTypes.FileSystem,
            FileSystemTypes.DropBox,
            FileSystemTypes.Ftp,
            FileSystemTypes.MediaFire,
            FileSystemTypes.Mega,
            FileSystemTypes.Nextcloud,
            FileSystemTypes.OneDrive,
            FileSystemTypes.Sftp,
            FileSystemTypes.Smb
        };
    }
}