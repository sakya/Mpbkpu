using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;
using BackupJob = MpbkpuGUI.Models.BackupJob;

namespace MpbkpuGUI.Pages;

public class JobRunningPage : BasePage
{
    private readonly JobRunningPageModel _model = new JobRunningPageModel();
    private readonly Job _job;
    private readonly CancellationTokenSource _cts = new();
    private BaseJob? _backupJob;
    private Thread? _worker;
    private readonly List<string> _log = new List<string>();

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
        _worker?.Join();
        base.Dispose();
    }

    public override void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);

        _worker = new Thread(DoWork);
        _worker.Start();
    }

    private async void DoWork()
    {
        if (_job is BackupJob bkp) {
            var settings = new BackupJobSettings()
            {
                BackupType = bkp.Type ?? Backup.BackupTypes.Full,
                EncryptPassword = bkp.Password,
                Source = bkp.Source!.Path,
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

                Destination = bkp.Destination!.Path,
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
            var backupRes = await _backupJob.Run(_cts.Token);
            _job.LastRun = DateTime.Now;
        } catch (Exception ex) {
            BackupJobOnLogEmitted(null, BaseJobSettings.LogLevels.Error, ex.Message);
        }
    }

    private void BackupJobOnLogEmitted(object? sender, BaseJobSettings.LogLevels level, string message)
    {
        while (_log.Count > 200)
            _log.RemoveAt(0);

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
        _log.Add(message);
        _model.Log = $"{string.Join('\n', _log)}\n";
        Dispatcher.UIThread.Post(() =>
        {
            var log = this.FindControl<TextBox>("Log");
            log.CaretIndex = log.Text.Length;
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
        var percentage = (double)args.Bytes / args.TotalBytes * 100.0;
        _model.Progress = percentage;
        _model.ProgressMessageEta = MpbkpuLib.Utility.String.TimeSpanToString(args.Eta);
        _model.ProgressMessageFiles = $"{args.File}/{args.TotalFiles}";
        _model.ProgressMessageBytes = $"{MpbkpuLib.Utility.String.BytesToString(args.Bytes)}/{MpbkpuLib.Utility.String.BytesToString(args.TotalBytes)}";
    }
}