using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Controls.Models;
using MpbkpuLib.FileManagers;

namespace MpbkpuGUI.Controls;

public class PositionDetail : UserControl
{
    private readonly PositionDetailModel _model = new PositionDetailModel();
    private Job.Position? _position;

    public static readonly DirectProperty<PositionDetail, Job.Position?> ValueProperty =
        AvaloniaProperty.RegisterDirect<PositionDetail, Job.Position?>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v);

    public PositionDetail()
    {
        DataContext = _model;
        InitializeComponent();
    }

    public Job.Position? Value
    {
        get => _position;
        set
        {
            if (SetAndRaise(ValueProperty, ref _position, value)) {
                _model.Position = _position;
                _model.Type = GetFileSystemType(out var scheme);
                if (string.IsNullOrEmpty(_model.Position!.Path))
                    _model.Position.Path = scheme;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        var cmbType = this.FindControl<ComboBox>("CmbType");
        cmbType.Items = new List<PositionDetailModel.FileSystemTypes>()
        {
            PositionDetailModel.FileSystemTypes.FileSystem,
            PositionDetailModel.FileSystemTypes.DropBox,
            PositionDetailModel.FileSystemTypes.Ftp,
            PositionDetailModel.FileSystemTypes.MediaFire,
            PositionDetailModel.FileSystemTypes.Mega,
            PositionDetailModel.FileSystemTypes.Nextcloud,
            PositionDetailModel.FileSystemTypes.OneDrive,
            PositionDetailModel.FileSystemTypes.Sftp,
            PositionDetailModel.FileSystemTypes.Smb
        };
        cmbType.SelectedItem = GetFileSystemType(out _);
    }

    private PositionDetailModel.FileSystemTypes GetFileSystemType(out string? scheme)
    {
        scheme = null;
        if (_position == null)
            return PositionDetailModel.FileSystemTypes.FileSystem;

        try {
            using var fm = FileManagerFactory.GetFileManager(_position.Path, out _);
            scheme = fm.GetScheme();
            switch (fm) {
                case MpbkpuLib.FileManagers.Dropbox:
                    return PositionDetailModel.FileSystemTypes.DropBox;
                case Ftp:
                    return PositionDetailModel.FileSystemTypes.Ftp;
                case Mega:
                    return PositionDetailModel.FileSystemTypes.Mega;
                case Nextcloud:
                    return PositionDetailModel.FileSystemTypes.Nextcloud;
                case Sftp:
                    return PositionDetailModel.FileSystemTypes.Sftp;
                case Smb:
                    return PositionDetailModel.FileSystemTypes.Smb;
                case MediaFire:
                    return PositionDetailModel.FileSystemTypes.MediaFire;
                case OneDrive:
                    return PositionDetailModel.FileSystemTypes.OneDrive;
            }
        } catch {
            // ignored
        }

        return PositionDetailModel.FileSystemTypes.FileSystem;
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: { } } cmb) {
            _model.Type = (PositionDetailModel.FileSystemTypes)cmb.SelectedItem;
        }
    }
}