using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Controls.Models;
using MpbkpuGUI.Interfaces;
using MpbkpuLib.FileManagers;
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

    /// <summary>
    /// Raised when the user picks another file system type
    /// </summary>
    public event EventHandler? TypeChanged;

    public bool IsLocalFileSystem => _model.IsLocalFileSystem;

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: { } } cmb) {
            _model.Type = (Job.Position.FileSystemTypes)cmb.SelectedItem;
            _model.Errors = null;
            _model.TestResult = null;
            TypeChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// Connect with the settings typed in the editor: log in, check the folder and read the free space
    /// </summary>
    private async void OnTestConnectionClick(object? sender, RoutedEventArgs e)
    {
        _model.TestResult = null;
        if (!Validate())
            return;

        var position = Value!;
        _model.IsTesting = true;
        try {
            var test = Task.Run(async () =>
            {
                using var fm = position.GetFileManager(out var path);
                var exists = await fm.DirectoryExists(path);
                StorageSpace? space = null;
                try {
                    space = await fm.GetSpace(path);
                } catch (Exception) {
                    // The space is optional (some file systems cannot report it)
                }
                return (exists, space);
            });
            // A browser login abandoned by the user would wait forever
            if (await Task.WhenAny(test, Task.Delay(TestTimeout)) != test)
                throw new TimeoutException(Localizer.Localizer.Instance["TestConnectionTimeout"]);

            var (exists, space) = await test;
            var message = Localizer.Localizer.Instance[exists ? "TestConnectionOk" : "TestConnectionNoFolder"];
            if (space != null)
                message += " " + string.Format(Localizer.Localizer.Instance["TestConnectionSpace"],
                    MpbkpuLib.Utility.String.BytesToString(space.Available));
            _model.TestMessage = message;
            _model.TestResult = true;
        } catch (Exception ex) {
            _model.TestMessage = $"{Localizer.Localizer.Instance["TestConnectionFailed"]}{ex.Message}";
            _model.TestResult = false;
        } finally {
            _model.IsTesting = false;
        }
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);

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
            case Job.Position.FileSystemTypes.PCloud:
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