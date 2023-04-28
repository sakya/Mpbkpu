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
using MpbkpuLib.Jobs;
using Newtonsoft.Json;

namespace MpbkpuGUI.Pages;

public class BackupVerifyPage : BasePage
{
    private readonly BackupVerifyPageModel _model = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Backup _backup;
    private readonly string? _password;
    private VerifyJob? _verifyJob;
    private Thread? _worker;

    public BackupVerifyPage()
    {
        _backup = new Backup();
        InitializeComponent();
    }

    public BackupVerifyPage(Backup backup, string? password)
    {
        _backup = backup;
        _password = password;
        DataContext = _model;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["VerifyingBackup"]}: {_backup.Guid}";
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

        _model.Running = true;
        _model.ButtonText = Localizer.Localizer.Instance["Cancel"];

        _worker = new Thread(DoWork);
        _worker.Start();
    }

    public override async Task<bool> OnNavigatingFrom(NavigationDirection direction)
    {
        if (_model.Running) {
            if (await MessageDialog.ShowConfirmMessage(App.GetMainWindow(),
                    Localizer.Localizer.Instance["ConfirmAbortVerify"], MessageDialog.Icons.Warning)) {
                await CancelVerify();
            }

            return false;
        }

        return true;
    }

    private async void DoWork()
    {
        var backupSettings = JsonConvert.DeserializeObject<BackupJobSettings>(_backup.Settings!);
        if (backupSettings == null)
            return;

        var settings = new VerifyJobSettings()
        {
            EncryptPassword = _password,
            BackupGuid = _backup.Guid,
            BackupPath = backupSettings.Destination,
            BackupPathUser = backupSettings.DestinationUser,
            BackupPathPassword = backupSettings.DestinationPassword,
            BackupOauthSettings = backupSettings.DestinationOauthSettings,
        };

        var verifyJob = new VerifyJob(settings);
        verifyJob.Step += VerifyJobOnStep;
        verifyJob.Progress += VerifJobOnProgress;

        _verifyJob = verifyJob;

        Dispatcher.UIThread.Post(() =>
        {
            var spinner = this.FindControl<Controls.Spinner>("Spinner");
            spinner.IsVisible = false;
        }, DispatcherPriority.Background);

        try {
            if (_verifyJob == null)
                throw new Exception("VerifyJob not initialized");

            _verifyJob.LogEmitted += VerifJobOnLogEmitted;
            _model.Running = true;
            _model.ButtonText = Localizer.Localizer.Instance["Cancel"];
            var verifyRes = await _verifyJob.Run(_cts.Token);
            if (verifyRes.Bytes > 0) {
                _model.Step = Localizer.Localizer.Instance["Step_Completed"];
            }

            if (!verifyRes.Success) {
                await MessageDialog.ShowMessage(App.GetMainWindow(),
                    $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{verifyRes.ErrorMessage}", MessageDialog.Icons.Error);
            }
        } catch (Exception ex) {
            if (ex is OperationCanceledException)
                return;
            VerifJobOnLogEmitted(null, BaseJobSettings.LogLevels.Error, ex.Message);
            Dispatcher.UIThread.Post(() =>
            {
                _ = MessageDialog.ShowMessage(App.GetMainWindow(),
                    $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
            });
        } finally {
            _model.Running = false;
            _model.ButtonText = Localizer.Localizer.Instance["Close"];
        }
    }

    private async Task CancelVerify()
    {
        _cts.Cancel();
        while (_model.Running)
            await Task.Delay(100);
        _worker?.Join();
    }

    private void VerifJobOnLogEmitted(object? sender, BaseJobSettings.LogLevels level, string message)
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
            var log = this.FindControl<ScrollViewer>("Log");
            log.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void VerifyJobOnStep(object sender, VerifyJob.Steps step)
    {
        _model.Step = Localizer.Localizer.Instance[$"Step_{step.ToString()}"];
    }

    private void VerifJobOnProgress(object sender, BaseJob.ProgressHandlerArgs args)
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