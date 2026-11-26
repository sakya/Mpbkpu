using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Controls.Models;
using MpbkpuGUI.Interfaces;
using Avalonia.Platform.Storage;

namespace MpbkpuGUI.Controls;

public partial class PositionDetail : UserControl, IValidate
{
    private readonly PositionDetailModel _model = new();
    /// <summary>
    /// The edited position: keeps what the editor does not show (id, cached space)
    /// </summary>
    private Job.Position? _value;

    public PositionDetail()
    {
        DataContext = _model;
        InitializeComponent();
    }

    public Job.Position? Value
    {
        get => new()
        {
            Id = _value?.Id ?? Guid.NewGuid().ToString("N"),
            Space = _value?.Space,
            SpaceRead = _value?.SpaceRead,
            Type = _model.Type,
            Path = _model.Path,
            User = _model.User,
            Password = _model.Password,

            OauthServerUrl = _model.OauthServerUrl,
            OauthAppId = _model.OauthAppId,
            OauthAppName = _model.OauthAppName,
            OauthAppSecret = _model.OauthAppSecret,
            OauthRedirectUrl = _model.OauthRedirectUrl
        };
        set
        {
            _value = value;
            _model.Type = value?.Type ?? Job.Position.FileSystemTypes.FileSystem;
            _model.Path = value?.Path;
            _model.User = value?.User;
            _model.Password = value?.Password;
            _model.OauthAppId = value?.OauthAppId;
            _model.OauthAppName = value?.OauthAppName;
            _model.OauthAppSecret = value?.OauthAppSecret;
            _model.OauthRedirectUrl = value?.OauthRedirectUrl;
            _model.OauthServerUrl = value?.OauthServerUrl;
            // The binding is not refreshed when the type does not change (a new position is a local folder like the
            // default of the model): select it explicitly
            this.FindControl<ComboBox>("CmbType")!.SelectedItem = _model.Type;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        var cmbType = this.FindControl<ComboBox>("CmbType")!;
        // Sorted by the name shown in the current language
        cmbType.ItemsSource = Enum.GetValues<Job.Position.FileSystemTypes>()
            .OrderBy(t => Localizer.Localizer.Instance[$"Fs_{t}"], StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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
        var folders = await ((App)Application.Current!).MainWindow!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());
        var dir = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
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