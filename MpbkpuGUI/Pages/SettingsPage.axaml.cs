using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuGUI.Utility;
using MpbkpuLib.Utility;

namespace MpbkpuGUI.Pages;

/// <summary>
/// Settings of the app (not of the jobs), applied and saved as soon as they change, and the updates
/// </summary>
public partial class SettingsPage : BasePage
{
    private readonly SettingsPageModel _model = new() { Installable = UpdateChecker.CanInstall };
    private CancellationTokenSource? _downloadCancel;
    // Ignore the events raised while the controls are filled
    private readonly bool _loaded;

    public SettingsPage()
    {
        DataContext = _model;
        AvaloniaXamlLoader.Load(this);
        PageTitle = Localizer.Localizer.Instance["Settings"];

        var settings = App.Settings!;
        var theme = this.FindControl<ComboBox>("CmbTheme")!;
        theme.ItemsSource = Enum.GetValues<AppSettings.ThemeModes>();
        theme.SelectedItem = settings.Theme;
        var accent = this.FindControl<ComboBox>("CmbAccent")!;
        accent.ItemsSource = Enum.GetValues<AppSettings.AccentModes>();
        accent.SelectedItem = settings.Accent;
        this.FindControl<ToggleSwitch>("CheckAtStartup")!.IsChecked = settings.CheckForUpdates;
        // A release found at startup
        if (UpdateChecker.Available != null)
            ShowRelease(UpdateChecker.Available);
        _loaded = true;
    }

    private async void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || (sender as ComboBox)?.SelectedItem is not AppSettings.ThemeModes theme)
            return;
        App.Settings!.Theme = theme;
        App.ApplyTheme(theme);
        await App.Settings.Save(App.SettingFile!);
    }

    private async void OnAccentChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || (sender as ComboBox)?.SelectedItem is not AppSettings.AccentModes accent)
            return;
        App.Settings!.Accent = accent;
        AccentPalette.Apply(accent);
        await App.Settings.Save(App.SettingFile!);
    }

    private async void OnCheckAtStartupChanged(object? sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;
        App.Settings!.CheckForUpdates = (sender as ToggleSwitch)?.IsChecked == true;
        await App.Settings.Save(App.SettingFile!);
    }

    private void ShowRelease(UpdateChecker.Release? release)
    {
        _model.UpdateFound = release != null;
        if (release == null)
            _model.UpdateStatus = Localizer.Localizer.Instance["UpdateCurrent"];
        else
            _model.UpdateStatus = string.Format(Localizer.Localizer.Instance[_model.CanInstall ? "UpdateAvailableVersion" : "UpdateAvailableManual"],
                release.Version.ToString(3));
    }

    private async void OnCheckUpdatesClick(object? sender, RoutedEventArgs e)
    {
        _model.IsBusy = true;
        _model.UpdateStatus = Localizer.Localizer.Instance["UpdateChecking"];
        try {
            ShowRelease(await UpdateChecker.Check());
        } catch (Exception ex) {
            _model.UpdateFound = false;
            _model.UpdateStatus = $"{Localizer.Localizer.Instance["UpdateCheckFailed"]}{ex.Message}";
        } finally {
            _model.IsBusy = false;
        }
    }

    private void OnReleasePageClick(object? sender, RoutedEventArgs e)
    {
        if (UpdateChecker.Available != null)
            Browser.Open(UpdateChecker.Available.PageUrl);
    }

    /// <summary>
    /// Download the setup program, start it and close the program (the setup starts it again)
    /// </summary>
    private async void OnInstallUpdateClick(object? sender, RoutedEventArgs e)
    {
        var release = UpdateChecker.Available;
        if (release == null)
            return;
        if (!await MessageDialog.ShowConfirmMessage(App.GetMainWindow(),
                string.Format(Localizer.Localizer.Instance["ConfirmInstallUpdate"], release.Version.ToString(3))))
            return;

        _model.IsBusy = true;
        _model.IsDownloading = true;
        _model.DownloadProgress = 0;
        _model.UpdateStatus = string.Format(Localizer.Localizer.Instance["UpdateDownloading"], release.InstallerName);
        _downloadCancel = new CancellationTokenSource();
        try {
            var path = await UpdateChecker.DownloadInstaller(release, new Progress<double>(p => _model.DownloadProgress = p),
                _downloadCancel.Token);
            UpdateChecker.StartInstaller(path);
            App.GetMainWindow()!.Close();
        } catch (OperationCanceledException) {
            ShowRelease(release);
        } catch (Exception ex) {
            _model.UpdateStatus = $"{Localizer.Localizer.Instance["UpdateInstallFailed"]}{ex.Message}";
        } finally {
            _model.IsBusy = false;
            _model.IsDownloading = false;
            _downloadCancel?.Dispose();
            _downloadCancel = null;
        }
    }

    public override Task<bool> OnNavigatingFrom(NavigationDirection direction)
    {
        // Leaving the page stops a download, so the program never closes by surprise
        _downloadCancel?.Cancel();
        return base.OnNavigatingFrom(direction);
    }

    private static readonly FilePickerFileType JobsFileType = new(Localizer.Localizer.Instance["JobsFile"])
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"]
    };

    private async void OnExportJobsClick(object? sender, RoutedEventArgs e)
    {
        var jobs = App.Settings!.Jobs;
        if (jobs.Count == 0) {
            await MessageDialog.ShowMessage(App.GetMainWindow(), Localizer.Localizer.Instance["NoJobsToExport"], MessageDialog.Icons.Info);
            return;
        }
        var file = await App.GetMainWindow()!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = Localizer.Localizer.Instance["ExportJobs"],
            SuggestedFileName = $"mpbkpu-jobs-{DateTime.Now:yyyyMMdd}.json",
            DefaultExtension = "json",
            FileTypeChoices = [JobsFileType]
        });
        if (file == null)
            return;

        try {
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream)) {
                await writer.WriteAsync(JobTransfer.Export(jobs));
            }
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                string.Format(Localizer.Localizer.Instance["JobsExported"], jobs.Count, file.Name), MessageDialog.Icons.Info);
        } catch (Exception ex) {
            await MessageDialog.ShowMessage(App.GetMainWindow(), $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}",
                MessageDialog.Icons.Error);
        }
    }

    private async void OnImportJobsClick(object? sender, RoutedEventArgs e)
    {
        var files = await App.GetMainWindow()!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = Localizer.Localizer.Instance["ImportJobs"],
            FileTypeFilter = [JobsFileType]
        });
        if (files.Count == 0)
            return;

        try {
            string content;
            await using (var stream = await files[0].OpenReadAsync())
            using (var reader = new StreamReader(stream)) {
                content = await reader.ReadToEndAsync();
            }
            var jobs = JobTransfer.Import(content);
            App.Settings!.Jobs.AddRange(jobs);
            await App.Settings.Save(App.SettingFile!);
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                string.Format(Localizer.Localizer.Instance["JobsImported"], jobs.Count), MessageDialog.Icons.Info);
        } catch (FormatException) {
            await MessageDialog.ShowMessage(App.GetMainWindow(), Localizer.Localizer.Instance["NotAJobsFile"], MessageDialog.Icons.Error);
        } catch (Exception ex) {
            await MessageDialog.ShowMessage(App.GetMainWindow(), $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}",
                MessageDialog.Icons.Error);
        }
    }

    private async void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}
