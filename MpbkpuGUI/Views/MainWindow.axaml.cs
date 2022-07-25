using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Mpbkpu.Views
{
    public partial class MainWindow : BaseView
    {
        private Task? _backupTask = null;
        private CancellationTokenSource? _cts = null;
        private List<string> _log = new List<string>();
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            base.InitializeComponent();

            if (OperatingSystem.IsWindows()) {
                this.FindControl<TextBox>("_source").Text = @"c:\Users\paolo.iommarini\Development\";
                this.FindControl<TextBox>("_destination").Text = @"c:\Users\paolo.iommarini\Backup\";
            } else {
                this.FindControl<TextBox>("_source").Text = "/home/sakya/Development/";
                this.FindControl<TextBox>("_destination").Text = "/home/sakya/backup/";
            }
        }

        private async void OnStartClick(object sender, RoutedEventArgs args)
        {
            var progress = this.FindControl<ProgressBar>("_progress");
            var fileProgress = this.FindControl<ProgressBar>("_fileProgress");
            progress.Value = 0;
            fileProgress.Value = 0;
            
            var settings = new BackupJobSettings()
            {
                Name = "Test",
                BackupType = BackupJobSettings.BackupTypes.Incremental,
                LogLevel = BackupJobSettings.LogLevels.Debug,
                Source = this.FindControl<TextBox>("_source").Text,
                Destination = this.FindControl<TextBox>("_destination").Text,
                EncryptPassword = "ciccioPasticcio",
                LogToConsole = true,
            };
            var bkp = new BackupJob(settings);
            bkp.FileProgress += async (sender, bytes, totalBytes) =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    fileProgress.Value = (double)bytes / (double)totalBytes * 100.0;
                }, DispatcherPriority.Background);
            };
            bkp.Progress += async (sender, fileIdx, totalFiles, bytes, totalBytes) =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    progress.Value = (double)bytes / (double)totalBytes * 100.0;
                }, DispatcherPriority.Background);
            };
            
            bkp.LogEmitted += async (s, level, message) =>
            {
                while (_log.Count >= 100) {
                    _log.RemoveAt(0);
                }
                _log.Add(message);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var tb = this.FindControl<TextBlock>("_log");
                    tb.Text = string.Join('\n', _log);
                }, DispatcherPriority.Background);
            };
            bkp.ConfirmBackup += (s, a) => {
                return Task.FromResult(true);
            };
            this.FindControl<Button>("_startBtn").IsEnabled = false;
            this.FindControl<Button>("_stopBtn").IsEnabled = true;

            _cts = new CancellationTokenSource();
            try {
                _backupTask = Task.Run(() => bkp.Run(_cts.Token));
                await _backupTask;
            } catch (Exception ex) {

            } finally {
                if (_cts != null) {
                    _cts.Dispose();
                    _cts = null;
                }
            }
            this.FindControl<Button>("_startBtn").IsEnabled = true;
            this.FindControl<Button>("_stopBtn").IsEnabled = false;            
        }


        private async void OnStopClick(object sender, RoutedEventArgs args)
        {
            if (_cts != null && _backupTask != null) {
                _cts.Cancel();
                await _backupTask;
                _cts.Dispose();
                _cts = null;

                this.FindControl<Button>("_startBtn").IsEnabled = true;
                this.FindControl<Button>("_stopBtn").IsEnabled = false;
            }
        }
    }
}
