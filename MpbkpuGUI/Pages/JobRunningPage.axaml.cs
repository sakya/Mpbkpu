using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dropbox.Api.Team;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;
using BackupJob = MpbkpuGUI.Models.BackupJob;

namespace MpbkpuGUI.Pages;

public partial class JobRunningPage : BasePage
{
    private readonly JobRunningPageModel _model = new JobRunningPageModel();
    private readonly Job _job;
    private readonly CancellationTokenSource _cts = new();
    private Task<BaseJobResult>? _task;
    private BaseJob? _backupJob;

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

    public override async void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);

        await Task.Run(() =>
        {
            if (_job is BackupJob bkp) {
                var settings = new BackupJobSettings()
                {
                    BackupType = bkp.Type ?? Backup.BackupTypes.Full,
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
                backupJob.FileProgress += BackupJobOnFileProgress;

                _backupJob = backupJob;
            } else {

            }
        });

        var spinner = this.FindControl<Controls.Spinner>("Spinner");
        spinner.IsVisible = false;

        _task = _backupJob.Run(_cts.Token);
    }

    private void BackupJobOnStep(object sender, MpbkpuLib.Jobs.BackupJob.Steps step)
    {
        _model.Step = step.ToString();
    }

    private Task<bool> BackupJobOnConfirmBackup(object sender, MpbkpuLib.Jobs.BackupJob.ConfirmArgs e)
    {
        return Task.FromResult(true);
    }

    private void BackupJobOnProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        var percentage = (double)args.Bytes / args.TotalBytes * 100.0;
        _model.Progress = percentage;
    }

    private void BackupJobOnFileProgress(object sender, string filepath, long bytes, long totalbytes)
    {

    }

}