using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib;

namespace Mpbkpu.Views
{
    public partial class MainWindow : BaseView
    {
        private Task? _backupTask = null;
        private CancellationTokenSource? _cts = null;
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            base.InitializeComponent();
        }

        private void OnStartClick(object sender, RoutedEventArgs args)
        {
            var settings = new BackupJobSettings()
            {
                Name = "Test",
                BackupType = BackupJobSettings.BackupTypes.Incremental,
                LogLevel = BackupJobSettings.LogLevels.Debug,
                Source = "/home/sakya/Development/",
                Destination = "/home/sakya/backup/",
                LogToConsole = true,
            };
            var bkp = new BackupJob(settings);
            bkp.ConfirmBackup += (s, a) => {
                return Task.FromResult(true);
            };
            _cts = new CancellationTokenSource();
            this.FindControl<Button>("_startBtn").IsEnabled = false;
            this.FindControl<Button>("_stopBtn").IsEnabled = true;
            _backupTask = Task.Run( () => bkp.Run(_cts.Token));
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
