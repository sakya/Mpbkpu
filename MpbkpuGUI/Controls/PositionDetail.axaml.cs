using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Controls.Models;
using MpbkpuGUI.Interfaces;

namespace MpbkpuGUI.Controls;

public class PositionDetail : UserControl, IValidate
{
    private readonly PositionDetailModel _model = new();

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

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: { } } cmb) {
            _model.Type = (Job.Position.FileSystemTypes)cmb.SelectedItem;
            _model.Errors = null;
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        var dir = await dlg.ShowAsync(((App)Application.Current!).MainWindow!);
        if (!string.IsNullOrEmpty(dir)) {
            _model.Path = dir;
        }
    }

    public bool Validate()
    {
        _model.Errors = null;
        var errors = new List<Controls.ErrorContainer.ErrorData>();
        if (string.IsNullOrEmpty(_model.Path))
            errors.Add(new ErrorContainer.ErrorData("Path", Localizer.Localizer.Instance["RequiredError"]));

        switch (_model.Type) {
            case Job.Position.FileSystemTypes.Ftp:
            case Job.Position.FileSystemTypes.Sftp:
                if (string.IsNullOrEmpty(_model.User))
                    errors.Add(new ErrorContainer.ErrorData("User", Localizer.Localizer.Instance["RequiredError"]));
                break;
            case Job.Position.FileSystemTypes.Mega:
                if (string.IsNullOrEmpty(_model.User))
                    errors.Add(new ErrorContainer.ErrorData("User", Localizer.Localizer.Instance["RequiredError"]));
                if (string.IsNullOrEmpty(_model.Password))
                    errors.Add(new ErrorContainer.ErrorData("Password", Localizer.Localizer.Instance["RequiredError"]));
                break;
            case Job.Position.FileSystemTypes.Nextcloud:
                if (string.IsNullOrEmpty(_model.User))
                    errors.Add(new ErrorContainer.ErrorData("User", Localizer.Localizer.Instance["RequiredError"]));
                if (_model.OauthServerUrl == null)
                    errors.Add(new ErrorContainer.ErrorData("OauthServerUrl", Localizer.Localizer.Instance["RequiredError"]));
                if (string.IsNullOrEmpty(_model.Password)) {
                    if (_model.OauthRedirectUrl == null)
                        errors.Add(new ErrorContainer.ErrorData("OauthRedirectUrl", Localizer.Localizer.Instance["RequiredError"]));
                    if (string.IsNullOrEmpty(_model.OauthAppId))
                        errors.Add(new ErrorContainer.ErrorData("OauthAppId", Localizer.Localizer.Instance["RequiredError"]));
                    if (string.IsNullOrEmpty(_model.OauthAppSecret))
                        errors.Add(new ErrorContainer.ErrorData("OauthAppSecret", Localizer.Localizer.Instance["RequiredError"]));
                }
                break;
            case Job.Position.FileSystemTypes.DropBox:
                if (string.IsNullOrEmpty(_model.User))
                    errors.Add(new ErrorContainer.ErrorData("User", Localizer.Localizer.Instance["RequiredError"]));
                if (_model.OauthRedirectUrl == null)
                    errors.Add(new ErrorContainer.ErrorData("OauthRedirectUrl", Localizer.Localizer.Instance["RequiredError"]));
                if (string.IsNullOrEmpty(_model.OauthAppId))
                    errors.Add(new ErrorContainer.ErrorData("OauthAppId", Localizer.Localizer.Instance["RequiredError"]));
                if (string.IsNullOrEmpty(_model.OauthAppSecret))
                    errors.Add(new ErrorContainer.ErrorData("OauthAppSecret", Localizer.Localizer.Instance["RequiredError"]));
                break;
            case Job.Position.FileSystemTypes.MediaFire:
                if (string.IsNullOrEmpty(_model.User))
                    errors.Add(new ErrorContainer.ErrorData("User", Localizer.Localizer.Instance["RequiredError"]));
                if (string.IsNullOrEmpty(_model.Password))
                    errors.Add(new ErrorContainer.ErrorData("Password", Localizer.Localizer.Instance["RequiredError"]));
                break;
            case Job.Position.FileSystemTypes.OneDrive:
                if (string.IsNullOrEmpty(_model.User))
                    errors.Add(new ErrorContainer.ErrorData("User", Localizer.Localizer.Instance["RequiredError"]));
                break;
        }

        _model.Errors = errors;
        return errors.Count == 0;
    }
}