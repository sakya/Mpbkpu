using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using Avalonia.Threading;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuLib;
using MpbkpuLib.FileManagers;

namespace MpbkpuGUI.Pages;

public partial class BackupOpenPage : BasePage
{
    private readonly BackupJob _backup;
    private BackupFile.OpenResult? _backupFile;

    private readonly BackupOpenPageModel _model = new();
    // Reload the backups when coming back from a page that changed them
    private bool _reloadOnReturn;

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

        if (direction == NavigationDirection.Backward) {
            if (!_reloadOnReturn)
                return;
            _reloadOnReturn = false;
            this.FindControl<Controls.Spinner>("Spinner")!.IsVisible = true;
        }

        _model.SpinnerMessage = Localizer.Localizer.Instance["Step_OpenDatabase"];
        try {
            await Task.Run(OpenDatabase);
        } catch (InvalidPasswordException) when (_backup.HasSavedPassword) {
            _backup.ForgetPassword();
            await App.Settings!.Save(App.SettingFile!);
            await MessageDialog.ShowMessage(App.GetMainWindow(), Localizer.Localizer.Instance["SavedPasswordInvalid"],
                MessageDialog.Icons.Warning);
            await NavigateBack();
            return;
        } catch (Exception ex) {
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
        }

        var spinner = this.FindControl<Controls.Spinner>("Spinner")!;
        spinner.IsVisible = false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["Backup"]}: {_backup.Name}";
        this.FindControl<TextBlock>("Title")!.Text = string.Format(Localizer.Localizer.Instance["HistoryOf"], _backup.Name);
        this.FindControl<TextBlock>("Paths")!.Text = $"{_backup.Source?.FullPath}  →  {_backup.Destination?.FullPath}";
    }

    private async Task OpenDatabase()
    {
        await Task.Delay(500);
        using var fm = _backup.Destination!.GetFileManager(out var backupPath);
        _backupFile = await BackupFile.OpenBackupFile(fm, backupPath, _backup.Password,
            progressAction: (bytes, total) =>
            {
                double percentage = bytes / (double)total * 100.0;
                _model.SpinnerMessage = $"{Localizer.Localizer.Instance["Step_OpenDatabase"]}: {percentage,5:0.0}%";
            });

        // Newest first, grouped by chain (a full backup and the following backups)
        var backups = _backupFile.BackupFile.Backups.OrderByDescending(b => b.Id)
            .Select(b => new BackupOpenPageModel.BackupData()
            {
                Backup = b,
                Items = b.Items.Count(),
                Size = b.Items.Sum(i => i.OriginalSize)
            })
            .ToList();
        var warnings = _backupFile.BackupFile.BackupWarnings.ToList()
            .GroupBy(w => w.BackupId)
            .ToDictionary(g => g.Key, g => g.Select(w => w.Message ?? string.Empty).ToList());
        for (var i = 0; i < backups.Count; i++) {
            if (warnings.TryGetValue(backups[i].Backup!.Id, out var messages))
                backups[i].Warnings = messages;

            if (i > 0 && backups[i - 1].Backup!.Type != MpbkpuLib.DataModel.Backup.BackupTypes.Full)
                continue;
            var fullIndex = backups.FindIndex(i, b => b.Backup!.Type == MpbkpuLib.DataModel.Backup.BackupTypes.Full);
            var chain = backups.GetRange(i, (fullIndex < 0 ? backups.Count : fullIndex + 1) - i);
            var started = chain[^1].Backup!.StartedAt?.ToLocalTime().ToString("d");
            backups[i].ChainTitle = i == 0 ?
                Localizer.Localizer.Instance["CurrentChain"] :
                string.Format(Localizer.Localizer.Instance["ChainFrom"], started);
            backups[i].ChainSummary = string.Format(Localizer.Localizer.Instance["ChainSummary"], chain.Count,
                MpbkpuLib.Utility.String.BytesToString(chain.Sum(b => b.Size)));
        }

        Dispatcher.UIThread.Post(() =>
        {
            _model.Backups = new ObservableCollection<BackupOpenPageModel.BackupData>(backups);
        });
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is BackupOpenPageModel.BackupData bkp) {
            if (await MessageDialog.ShowConfirmMessage(MainWindow,
                    Localizer.Localizer.Instance["ConfirmDeleteBackup"],
                    MessageDialog.Icons.Warning)) {
                // The delete job updates the database: close it and reload it when coming back
                _backupFile?.Dispose();
                _backupFile = null;
                _reloadOnReturn = true;
                await NavigateTo(new DeleteBackupPage(_backup, bkp.Backup!));
            }
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is BackupOpenPageModel.BackupData bkp)
            await NavigateTo(new BackupBrowsePage(_backupFile!.BackupFile, bkp.Backup!.Id));
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