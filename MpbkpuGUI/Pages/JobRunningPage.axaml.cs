using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using Avalonia.Threading;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;
using BackupJob = MpbkpuGUI.Models.BackupJob;

namespace MpbkpuGUI.Pages;

public partial class JobRunningPage : BasePage
{
    private readonly JobRunningPageModel _model = new();
    private readonly Job _job;
    private readonly CancellationTokenSource _cts = new();
    private BaseJob? _backupJob;
    private StorageSpace? _lastSpace;
    private Thread? _worker;

    public JobRunningPage()
    {
        _job = new BackupJob();
        InitializeComponent();
    }

    public JobRunningPage(Job job)
    {
        if (job.Source == null)
            throw new Exception("Source cannot be null");
        if (job.Destination == null)
            throw new Exception("Destination cannot be null");

        _job = job;
        DataContext = _model;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["JobRunning"]}: {_job.Name}";
        NavigateBackWithKeyboard = false;
    }

    public override void Dispose()
    {
        _cts.Dispose();
        base.Dispose();
    }

    public override void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);

        this.FindControl<TextBlock>("JobName")!.Text = _job.Name;
        _model.Source = _job.Source?.FullPath;
        _model.Destination = _job.Destination?.FullPath;
        _model.Running = true;
        _model.ButtonText = Localizer.Localizer.Instance["Cancel"];
        App.UpdateLayout();

        _worker = new Thread(DoWork);
        _worker.Start();
    }

    public override async Task<bool> OnNavigatingFrom(NavigationDirection direction)
    {
        if (_model.Running) {
            if (await MessageDialog.ShowConfirmMessage(App.GetMainWindow(),
                    Localizer.Localizer.Instance["ConfirmAbortBackup"], MessageDialog.Icons.Warning)) {
                await CancelBackup();
            }

            return false;
        }

        return true;
    }

    private async void DoWork()
    {
        if (_job is BackupJob bkp) {
            var settings = new BackupJobSettings()
            {
                BackupType = bkp.Type ?? Backup.BackupTypes.Full,
                EncryptPassword = bkp.Password,
                Source = bkp.Source!.FullPath,
                SourceUser = bkp.Source.User,
                SourcePassword = bkp.Source.Password,
                SourceOauthSettings = bkp.Source.GetOAuth2Settings(),

                Destination = bkp.Destination!.FullPath,
                DestinationUser = bkp.Destination.User,
                DestinationPassword = bkp.Destination.Password,
                DestinationOauthSettings = bkp.Destination.GetOAuth2Settings(),

                SkipHashCheck = bkp.SkipHashCheck,
                RetryOnError = bkp.RetryOnError,
                CompressionLevel = bkp.CompressionLevel,
                FullBackupEveryDays = bkp.FullBackupEveryDays,
                KeepChains = bkp.KeepChains
            };

            var backupJob = new MpbkpuLib.Jobs.BackupJob(settings);
            backupJob.Step += BackupJobOnStep;
            backupJob.ConfirmBackup += BackupJobOnConfirmBackup;
            backupJob.Progress += BackupJobOnProgress;
            backupJob.SpaceChanged += BackupJobOnSpaceChanged;

            _backupJob = backupJob;
        } else if (_job is SyncJob sync) {
            var settings = new SyncDirectoryJobSettings()
            {
                Source = sync.Source!.FullPath,
                SourceUser = sync.Source.User,
                SourcePassword = sync.Source.Password,
                SourceOauthSettings = sync.Source.GetOAuth2Settings(),

                Destination = sync.Destination!.FullPath,
                DestinationUser = sync.Destination.User,
                DestinationPassword = sync.Destination.Password,
                DestinationOauthSettings = sync.Destination.GetOAuth2Settings(),

                SkipHashCheck = sync.SkipHashCheck,
                RetryOnError = sync.RetryOnError,
            };

            var backupJob = new SyncDirectoryJob(settings);
            backupJob.Step += BackupJobOnStep;
            backupJob.ConfirmBackup += BackupJobOnConfirmBackup;
            backupJob.Progress += BackupJobOnProgress;
            backupJob.SpaceChanged += BackupJobOnSpaceChanged;

            _backupJob = backupJob;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var spinner = this.FindControl<Controls.Spinner>("Spinner")!;
            spinner.IsVisible = false;
        }, DispatcherPriority.Background);

        try {
            if (_backupJob == null)
                throw new Exception("BackupJob not initialized");

            _backupJob.LogEmitted += BackupJobOnLogEmitted;
            _model.Running = true;
            _model.ButtonText = Localizer.Localizer.Instance["Cancel"];
            var backupRes = await _backupJob.Run(_cts.Token);
            Backup.Results result;
            if (backupRes.Result is { } backupResult)
                result = backupResult;
            else if (_cts.IsCancellationRequested)
                result = Backup.Results.Cancelled;
            else
                result = backupRes.Success ? Backup.Results.Completed : Backup.Results.Error;
            await SaveLastRun(result);
            _model.Step = Localizer.Localizer.Instance["Step_Completed"];

            if (!backupRes.Success) {
                await MessageDialog.ShowMessage(App.GetMainWindow(),
                    $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{backupRes.ErrorMessage}", MessageDialog.Icons.Error);
            }
        } catch (Exception ex) {
            BackupJobOnLogEmitted(null, BaseJobSettings.LogLevels.Error, ex.Message);
            var message = $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}";
            if (ex is MpbkpuLib.InvalidPasswordException && _job is BackupJob { HasSavedPassword: true } savedJob) {
                // Saved with the next SaveLastRun
                savedJob.ForgetPassword();
                message = Localizer.Localizer.Instance["SavedPasswordInvalid"];
            }
            await SaveLastRun(Backup.Results.Error);
            Dispatcher.UIThread.Post(() =>
            {
                _ = MessageDialog.ShowMessage(App.GetMainWindow(), message, MessageDialog.Icons.Error);
            });
        } finally {
            _model.Running = false;
            _model.ButtonText = Localizer.Localizer.Instance["Close"];
        }
    }

    /// <summary>
    /// Save when the job ran and its result (shown in the job list)
    /// </summary>
    private void BackupJobOnSpaceChanged(object sender, StorageSpace space)
    {
        _lastSpace = space;
        Dispatcher.UIThread.Post(() =>
        {
            this.FindControl<Control>("SpacePanel")!.IsVisible = true;
            Controls.SpaceDisplay.Show(space, this.FindControl<ProgressBar>("SpaceBar")!, this.FindControl<TextBlock>("SpaceText")!);
        });
    }

    private async Task SaveLastRun(Backup.Results result)
    {
        _job.LastRun = DateTime.Now;
        _job.LastResult = result;
        if (_lastSpace != null) {
            // Shown in the job list
            _job.DestinationSpace = _lastSpace;
            _job.DestinationSpaceRead = DateTime.Now;
        }
        await App.Settings!.Save(App.SettingFile!);
    }

    private async Task CancelBackup()
    {
        _cts.Cancel();
        while (_model.Running)
            await Task.Delay(100);
        _worker?.Join();
    }

    private void BackupJobOnLogEmitted(object? sender, BaseJobSettings.LogLevels level, string message)
    {
        _model.Log ??= new ObservableCollection<JobRunningPageModel.LogMessage>();

        switch (level) {
            case BaseJobSettings.LogLevels.Debug:
                message = $"[DBG]{message}";
                break;
            case BaseJobSettings.LogLevels.Info:
                message = $"[INF]{message}";
                break;
            case BaseJobSettings.LogLevels.Warning:
                message = $"[WRN]{message}";
                break;
            case BaseJobSettings.LogLevels.Error:
                message = $"[ERR]{message}";
                break;
        }

        Dispatcher.UIThread.Post(() =>
        {
            while (_model.Log.Count > 200)
                _model.Log.RemoveAt(0);

            _model.Log.Add(new JobRunningPageModel.LogMessage()
            {
                Level = level,
                Message = message
            });
            App.UpdateLayout();
            var log = this.FindControl<ScrollViewer>("Log")!;
            log.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private Task<bool> BackupJobOnConfirmBackup(object sender, SyncDirectoryJob.ConfirmArgs e)
    {
        return Task.FromResult(true);
    }

    private void BackupJobOnStep(object sender, SyncDirectoryJob.Steps step)
    {
        _model.Step = Localizer.Localizer.Instance[$"Step_{step.ToString()}"];
    }

    private void BackupJobOnStep(object sender, MpbkpuLib.Jobs.BackupJob.Steps step)
    {
        _model.Step = Localizer.Localizer.Instance[$"Step_{step.ToString()}"];
    }

    private Task<bool> BackupJobOnConfirmBackup(object sender, MpbkpuLib.Jobs.BackupJob.ConfirmArgs e)
    {
        return Task.FromResult(true);
    }

    private void BackupJobOnProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        var percentage = Math.Floor(args.Percentage);
        _model.Progress = percentage;
        _model.ProgressMessageEta = MpbkpuLib.Utility.String.EtaToString(args.Eta);
        _model.ProgressMessageFiles = $"{args.File}/{args.TotalFiles}";
        _model.ProgressMessageBytes = $"{MpbkpuLib.Utility.String.BytesToString(args.Bytes)}/{MpbkpuLib.Utility.String.BytesToString(args.TotalBytes)}";
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}