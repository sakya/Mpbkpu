using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuGUI.Views;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;
using BackupJob = MpbkpuGUI.Models.BackupJob;

namespace MpbkpuGUI.Pages;

public class JobRunningPage : BasePage
{
    private readonly JobRunningPageModel _model = new();
    private readonly Job _job;
    private readonly CancellationTokenSource _cts = new();
    private BaseJob? _backupJob;
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
            if (await MessageWindow.ShowConfirmMessage(App.GetMainWindow(),
                    Localizer.Localizer.Instance["Confirm"],
                    Localizer.Localizer.Instance["ConfirmAbortBackup"], MessageWindow.Icons.Warning)) {
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
                SourceOauthSettings = new OAuth2Settings()
                {
                    AppName = bkp.Source.OauthAppName,
                    AppId = bkp.Source.OauthAppId,
                    AppSecret = bkp.Source.OauthAppSecret,
                    ServerUrl = bkp.Source.OauthServerUrl,
                    RedirectUrl = bkp.Source.OauthRedirectUrl
                },

                Destination = bkp.Destination!.FullPath,
                DestinationUser = bkp.Destination.User,
                DestinationPassword = bkp.Destination.Password,
                DestinationOauthSettings = new OAuth2Settings()
                {
                    AppName = bkp.Destination.OauthAppName,
                    AppId = bkp.Destination.OauthAppId,
                    AppSecret = bkp.Destination.OauthAppSecret,
                    ServerUrl = bkp.Destination.OauthServerUrl,
                    RedirectUrl = bkp.Destination.OauthRedirectUrl
                },

                SkipHashCheck = bkp.SkipHashCheck,
                RetryOnError = bkp.RetryOnError,
                CompressionLevel = bkp.CompressionLevel
            };

            var backupJob = new MpbkpuLib.Jobs.BackupJob(settings);
            backupJob.Step += BackupJobOnStep;
            backupJob.ConfirmBackup += BackupJobOnConfirmBackup;
            backupJob.Progress += BackupJobOnProgress;

            _backupJob = backupJob;
        } else if (_job is SyncJob sync) {
            var settings = new SyncDirectoryJobSettings()
            {
                Source = sync.Source!.Path,
                SourceUser = sync.Source.User,
                SourcePassword = sync.Source.Password,
                SourceOauthSettings = new OAuth2Settings()
                {
                    AppName = sync.Source.OauthAppName,
                    AppId = sync.Source.OauthAppId,
                    AppSecret = sync.Source.OauthAppSecret,
                    ServerUrl = sync.Source.OauthServerUrl,
                    RedirectUrl = sync.Source.OauthRedirectUrl
                },

                Destination = sync.Destination!.Path,
                DestinationUser = sync.Destination.User,
                DestinationPassword = sync.Destination.Password,
                DestinationOauthSettings = new OAuth2Settings()
                {
                    AppName = sync.Destination.OauthAppName,
                    AppId = sync.Destination.OauthAppId,
                    AppSecret = sync.Destination.OauthAppSecret,
                    ServerUrl = sync.Destination.OauthServerUrl,
                    RedirectUrl = sync.Destination.OauthRedirectUrl
                },

                SkipHashCheck = sync.SkipHashCheck,
                RetryOnError = sync.RetryOnError,
            };

            var backupJob = new SyncDirectoryJob(settings);
            backupJob.Step += BackupJobOnStep;
            backupJob.ConfirmBackup += BackupJobOnConfirmBackup;
            backupJob.Progress += BackupJobOnProgress;

            _backupJob = backupJob;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var spinner = this.FindControl<Controls.Spinner>("Spinner");
            spinner.IsVisible = false;
        }, DispatcherPriority.Background);

        try {
            if (_backupJob == null)
                throw new Exception("BackupJob not initialized");

            _backupJob.LogEmitted += BackupJobOnLogEmitted;
            _model.Running = true;
            _model.ButtonText = Localizer.Localizer.Instance["Cancel"];
            var backupRes = await _backupJob.Run(_cts.Token);
            if (backupRes.Bytes > 0) {
                _job.LastRun = DateTime.Now;
                await App.Settings!.Save(App.SettingFile!);
                _model.Step = Localizer.Localizer.Instance["Step_Completed"];
            }

            if (!backupRes.Success) {
                await MessageWindow.ShowMessage(App.GetMainWindow(), Localizer.Localizer.Instance["Error"],
                    $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{backupRes.ErrorMessage}", MessageWindow.Icons.Error);
            }
        } catch (Exception ex) {
            BackupJobOnLogEmitted(null, BaseJobSettings.LogLevels.Error, ex.Message);
            await MessageWindow.ShowMessage(App.GetMainWindow(), Localizer.Localizer.Instance["Error"],
                $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageWindow.Icons.Error);
        } finally {
            _model.Running = false;
            _model.ButtonText = Localizer.Localizer.Instance["Close"];
        }
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
            var log = this.FindControl<ScrollViewer>("Log");
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
        var percentage = Math.Floor(args.Bytes / (double)args.TotalBytes * 100.0);
        _model.Progress = percentage;
        _model.ProgressMessageEta = MpbkpuLib.Utility.String.TimeSpanToString(args.Eta);
        _model.ProgressMessageFiles = $"{args.File}/{args.TotalFiles}";
        _model.ProgressMessageBytes = $"{MpbkpuLib.Utility.String.BytesToString(args.Bytes)}/{MpbkpuLib.Utility.String.BytesToString(args.TotalBytes)}";
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}