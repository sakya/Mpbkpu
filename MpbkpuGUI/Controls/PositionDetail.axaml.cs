using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Controls.Models;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;

namespace MpbkpuGUI.Controls;

public class PositionDetail : UserControl
{
    private readonly PositionDetailModel _model = new();

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
        get => new()
        {
            Type = _model.Type,
            Path = _model.Path,
            User = _model.User,

            OauthServerUrl = _model.OauthServerUrl,
            OauthAppId = _model.OauthAppId,
            OauthAppName = _model.OauthAppName,
            OauthAppSecret = _model.OauthAppSecret,
            OauthRedirectUrl = _model.OauthRedirectUrl
        };
        set
        {
            _model.Type = value?.Type ?? Job.Position.FileSystemTypes.FileSystem;
            _model.Path = value?.Path;
            _model.User = value?.User;
            _model.Password = value?.Password;
            _model.OauthAppId = value?.OauthAppId;
            _model.OauthAppName = value?.OauthAppName;
            _model.OauthAppSecret = value?.OauthAppSecret;
            _model.OauthRedirectUrl = value?.OauthRedirectUrl;
            _model.OauthServerUrl = value?.OauthServerUrl;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        var cmbType = this.FindControl<ComboBox>("CmbType");
        cmbType.Items = new List<Job.Position.FileSystemTypes>()
        {
            Job.Position.FileSystemTypes.FileSystem,
            Job.Position.FileSystemTypes.DropBox,
            Job.Position.FileSystemTypes.Ftp,
            Job.Position.FileSystemTypes.MediaFire,
            Job.Position.FileSystemTypes.Mega,
            Job.Position.FileSystemTypes.Nextcloud,
            Job.Position.FileSystemTypes.OneDrive,
            Job.Position.FileSystemTypes.Sftp,
            Job.Position.FileSystemTypes.Smb
        };
        cmbType.SelectedItem = Job.Position.FileSystemTypes.FileSystem;
    }

    private Job.Position.FileSystemTypes GetFileSystemType()
    {
        if (string.IsNullOrEmpty(_model.Path))
            return Job.Position.FileSystemTypes.FileSystem;

        try {
            using var fm = FileManagerFactory.GetFileManager(_model.Path, out _);
            switch (fm) {
                case MpbkpuLib.FileManagers.Dropbox:
                    return Job.Position.FileSystemTypes.DropBox;
                case Ftp:
                    return Job.Position.FileSystemTypes.Ftp;
                case Mega:
                    return Job.Position.FileSystemTypes.Mega;
                case Nextcloud:
                    return Job.Position.FileSystemTypes.Nextcloud;
                case Sftp:
                    return Job.Position.FileSystemTypes.Sftp;
                case Smb:
                    return Job.Position.FileSystemTypes.Smb;
                case MediaFire:
                    return Job.Position.FileSystemTypes.MediaFire;
                case OneDrive:
                    return Job.Position.FileSystemTypes.OneDrive;
            }
        } catch {
            // ignored
        }

        return Job.Position.FileSystemTypes.FileSystem;
    }

    private static string? GetScheme(Job.Position.FileSystemTypes type)
    {
        IFileManager? fm = null;
        switch (type) {
            case Job.Position.FileSystemTypes.Ftp:
                fm = new Ftp();
                break;
            case Job.Position.FileSystemTypes.Mega:
                fm = new Mega("account", "account");
                break;
            case Job.Position.FileSystemTypes.Nextcloud:
                fm = new Nextcloud("account");
                break;
            case Job.Position.FileSystemTypes.Sftp:
                fm = new Sftp();
                break;
            case Job.Position.FileSystemTypes.Smb:
                fm = new Smb(string.Empty, string.Empty, string.Empty, string.Empty);
                break;
            case Job.Position.FileSystemTypes.DropBox:
                fm = new MpbkpuLib.FileManagers.Dropbox("account");
                break;
            case Job.Position.FileSystemTypes.MediaFire:
                fm = new MediaFire("account", "account");
                break;
            case Job.Position.FileSystemTypes.OneDrive:
                fm = new OneDrive("account");
                break;
        }

        return fm?.GetScheme();
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: { } } cmb) {
            _model.Type = (Job.Position.FileSystemTypes)cmb.SelectedItem;
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        var dir = await dlg.ShowAsync(((App)Application.Current).MainWindow);
        if (!string.IsNullOrEmpty(dir)) {
            _model.Path = dir;
        }
    }
}