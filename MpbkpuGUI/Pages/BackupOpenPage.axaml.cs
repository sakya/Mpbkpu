using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuGUI.Views;
using MpbkpuLib;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;

namespace MpbkpuGUI.Pages;

public class BackupOpenPage : BasePage
{
    private readonly BackupJob _backup;
    private BackupFile.OpenResult? _backupFile;

    private readonly BackupOpenPageModel _model = new();

    public BackupOpenPage()
    {
        _backup = new BackupJob();
        DataContext = _model;
        InitializeComponent();
    }

    public BackupOpenPage(BackupJob backup)
    {
        _backup = backup;
        DataContext = _model;
        InitializeComponent();
    }

    public override void Dispose()
    {
        _backupFile?.Dispose();
        base.Dispose();
    }

    public override async void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);

        _model.SpinnerMessage = Localizer.Localizer.Instance["Step_OpenDatabase"];
        try {
            await Task.Run(OpenDatabase);
        } catch (Exception ex) {
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
        }

        var spinner = this.FindControl<Controls.Spinner>("Spinner");
        spinner.IsVisible = false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["Backup"]}: {_backup.Name}";
    }

    private async Task OpenDatabase()
    {
        await Task.Delay(500);
        var oauthSettings = new OAuth2Settings()
        {
            AppName = _backup.Destination!.OauthAppName,
            AppId = _backup.Destination.OauthAppId,
            AppSecret = _backup.Destination.OauthAppSecret,
            ServerUrl = _backup.Destination.OauthServerUrl,
            RedirectUrl = _backup.Destination.OauthRedirectUrl
        };
        using var fm = FileManagerFactory.GetFileManager(_backup.Destination.FullPath, out var backupPath, oauth2Settings: oauthSettings);
        _backupFile = await BackupFile.OpenBackupFile(fm, backupPath, _backup.Password,
            progressAction: (bytes, total) =>
            {
                double percentage = bytes / (double)total * 100.0;
                _model.SpinnerMessage = $"{Localizer.Localizer.Instance["Step_OpenDatabase"]}: {percentage,5:0.0}%";
            });

        _model.Backups = new ObservableCollection<BackupOpenPageModel.BackupData>();
        var backups = _backupFile.BackupFile.Backups.OrderBy(b => b.Id)
            .Select(b => new
            {
                Backup = b,
                Files = b.Items.Count(),
                Bytes = b.Items.Sum(i => i.OriginalSize)
            });
        foreach (var bkp in backups) {
            Dispatcher.UIThread.Post(() =>
            {
                _model.Backups.Add(new BackupOpenPageModel.BackupData()
                {
                    Backup = bkp.Backup,
                    Items = bkp.Files,
                    Size = bkp.Bytes
                });
            });
        }
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is BackupOpenPageModel.BackupData bkp) {
            if (await MessageDialog.ShowConfirmMessage(MainWindow,
                    Localizer.Localizer.Instance["ConfirmDeleteBackup"],
                    MessageDialog.Icons.Warning)) {
                await NavigateTo(new DeleteBackupPage(bkp.Backup!));
            }
        }
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {

    }

    private async void OnVerifyClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is BackupOpenPageModel.BackupData bkp)
            await NavigateTo(new BackupVerifyPage(bkp.Backup!, _backup.Password));
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}