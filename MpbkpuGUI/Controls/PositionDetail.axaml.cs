using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
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
            Path = $"{GetScheme(_model.Type)}{_model.Path}",
            User = _model.User,

            OauthServerUrl = _model.OauthServerUrl,
            OauthAppId = _model.OauthAppId,
            OauthAppName = _model.OauthAppName,
            OauthAppSecret = _model.OauthAppSecret,
            OauthRedirectUrl = _model.OauthRedirectUrl
        };
        set
        {
            _model.Path = value?.Path;
            _model.User = value?.User;
            _model.Password = value?.Password;
            _model.OauthAppId = value?.OauthAppId;
            _model.OauthAppName = value?.OauthAppName;
            _model.OauthAppSecret = value?.OauthAppSecret;
            _model.OauthRedirectUrl = value?.OauthRedirectUrl;
            _model.OauthServerUrl = value?.OauthServerUrl;

            _model.Type = GetFileSystemType();
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
        cmbType.SelectedItem = GetFileSystemType();
    }

    private PositionDetailModel.FileSystemTypes GetFileSystemType()
    {
        if (string.IsNullOrEmpty(_model.Path))
            return PositionDetailModel.FileSystemTypes.FileSystem;

        try {
            using var fm = FileManagerFactory.GetFileManager(_model.Path, out _);
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

    private static string? GetScheme(PositionDetailModel.FileSystemTypes type)
    {
        IFileManager? fm = null;
        switch (type) {
            case PositionDetailModel.FileSystemTypes.Ftp:
                fm = new Ftp();
                break;
            case PositionDetailModel.FileSystemTypes.Mega:
                fm = new Mega("account", "account");
                break;
            case PositionDetailModel.FileSystemTypes.Nextcloud:
                fm = new Nextcloud("account");
                break;
            case PositionDetailModel.FileSystemTypes.Sftp:
                fm = new Sftp();
                break;
            case PositionDetailModel.FileSystemTypes.Smb:
                fm = new Smb(string.Empty, string.Empty, string.Empty, string.Empty);
                break;
            case PositionDetailModel.FileSystemTypes.DropBox:
                fm = new MpbkpuLib.FileManagers.Dropbox("account");
                break;
            case PositionDetailModel.FileSystemTypes.MediaFire:
                fm = new MediaFire("account", "account");
                break;
            case PositionDetailModel.FileSystemTypes.OneDrive:
                fm = new OneDrive("account");
                break;
        }

        return fm?.GetScheme();
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: { } } cmb) {
            _model.Type = (PositionDetailModel.FileSystemTypes)cmb.SelectedItem;
        }
    }
}