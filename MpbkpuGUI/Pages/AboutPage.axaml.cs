using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuGUI.Utility;
using MpbkpuLib.Utility;

namespace MpbkpuGUI.Pages;

public partial class AboutPage : BasePage
{
    private readonly AboutPageModel _model;
    private CancellationTokenSource? _downloadCancel;

    public AboutPage()
    {
        _model = new AboutPageModel()
        {
            Title = $"Mpbkpu v.{Assembly.GetEntryAssembly()!.GetName().Version}",
            Copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright,
            Description = "A multi platform backup utility with encryption and compression feature.\nSupports local and remote file system.",
            Installable = UpdateChecker.CanInstall
        };
        DataContext = _model;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ToggleSwitch>("CheckAtStartup")!.IsChecked = App.Settings!.CheckForUpdates;
        // A release found at startup
        if (UpdateChecker.Available != null)
            ShowRelease(UpdateChecker.Available);
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

    private async void OnCheckAtStartupChanged(object? sender, RoutedEventArgs e)
    {
        var value = (sender as ToggleSwitch)?.IsChecked == true;
        if (App.Settings!.CheckForUpdates == value)
            return;
        App.Settings.CheckForUpdates = value;
        await App.Settings.Save(App.SettingFile!);
    }

    public override Task<bool> OnNavigatingFrom(NavigationDirection direction)
    {
        // Leaving the page stops a download, so the program never closes by surprise
        _downloadCancel?.Cancel();
        return base.OnNavigatingFrom(direction);
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}
