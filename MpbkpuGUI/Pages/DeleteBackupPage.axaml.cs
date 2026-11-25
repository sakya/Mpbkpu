using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using Avalonia.Threading;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;
using BackupJob = MpbkpuGUI.Models.BackupJob;

namespace MpbkpuGUI.Pages;

/// <summary>
/// Deletes a backup (and the following backups of its chain) showing progress and log
/// </summary>
public partial class DeleteBackupPage : BasePage
{
    private readonly BackupJob _job;
    private readonly Backup _backup;
    private readonly BackupVerifyPageModel _model = new();
    private readonly CancellationTokenSource _cts = new();

    public DeleteBackupPage()
    {
        _job = new BackupJob();
        _backup = new Backup();
        DataContext = _model;
        InitializeComponent();
    }

    public DeleteBackupPage(BackupJob job, Backup backup)
    {
        _job = job;
        _backup = backup;
        DataContext = _model;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["DeletingBackup"]}: {_backup.Guid}";
        NavigateBackWithKeyboard = false;
        this.FindControl<TextBlock>("Subtitle")!.Text =
            $"{_backup.StartedAt?.ToLocalTime():g} · {Localizer.Localizer.Instance[$"BackupType_{_backup.Type}"]} · {_backup.Guid}";
    }

    public override void Dispose()
    {
        _cts.Dispose();
        base.Dispose();
    }

    public override async void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);
        if (direction == NavigationDirection.Forward)
            await Run();
    }

    public override async Task<bool> OnNavigatingFrom(NavigationDirection direction)
    {
        if (!_model.Running)
            return true;

        if (await MessageDialog.ShowConfirmMessage(App.GetMainWindow(),
                Localizer.Localizer.Instance["ConfirmAbortDelete"], MessageDialog.Icons.Warning)) {
            _cts.Cancel();
        }
        return false;
    }

    private async Task Run()
    {
        _model.Running = true;
        _model.ButtonText = Localizer.Localizer.Instance["Cancel"];
        _model.Log = new ObservableCollection<JobRunningPageModel.LogMessage>();

        var settings = new DeleteJobSettings()
        {
            BackupDestinations = [_job.Destination!.ToBackupDestination()],
            BackupGuid = _backup.Guid,
            EncryptPassword = _job.Password
        };

        using var deleteJob = new DeleteJob(settings);
        deleteJob.Step += (_, step) => Dispatcher.UIThread.Post(() =>
            _model.Step = Localizer.Localizer.Instance[$"Step_{step}"]);
        deleteJob.Progress += (_, args) => Dispatcher.UIThread.Post(() =>
        {
            _model.Progress = Math.Floor(args.Percentage);
            _model.ProgressMessageEta = MpbkpuLib.Utility.String.EtaToString(args.Eta);
            _model.ProgressMessageFiles = $"{args.File}/{args.TotalFiles}";
            _model.ProgressMessageBytes = $"{MpbkpuLib.Utility.String.BytesToString(args.Bytes)}/{MpbkpuLib.Utility.String.BytesToString(args.TotalBytes)}";
        });
        deleteJob.LogEmitted += (_, level, message) => AddLog(level, message);
        deleteJob.ConfirmDelete += OnConfirmDelete;

        this.FindControl<Controls.Spinner>("Spinner")!.IsVisible = false;
        try {
            await Task.Run(() => deleteJob.Run(_cts.Token));
            _model.Progress = 100;
            _model.Step = Localizer.Localizer.Instance["Step_Completed"];
        } catch (Exception ex) {
            AddLog(BaseJobSettings.LogLevels.Error, ex.Message);
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
        } finally {
            _model.Running = false;
            _model.ButtonText = Localizer.Localizer.Instance["Close"];
        }
    }

    /// <summary>
    /// Deleting a backup deletes also the following backups of its chain: ask before doing it
    /// </summary>
    private async Task<bool> OnConfirmDelete(object sender, DeleteJob.ConfirmArgs e)
    {
        if (e.Backups.Count <= 1)
            return true;

        return await Dispatcher.UIThread.InvokeAsync(() => MessageDialog.ShowConfirmMessage(App.GetMainWindow(),
            string.Format(Localizer.Localizer.Instance["ConfirmDeleteChain"], e.Backups.Count - 1),
            MessageDialog.Icons.Warning));
    }

    private void AddLog(BaseJobSettings.LogLevels level, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _model.Log!.Add(new JobRunningPageModel.LogMessage()
            {
                Level = level,
                Message = message
            });
            this.FindControl<ScrollViewer>("Log")!.ScrollToEnd();
        });
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}
