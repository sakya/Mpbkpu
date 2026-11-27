using Avalonia;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuGUI.Models;

namespace MpbkpuGUI
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Set current directory to the one containing the executable
            var exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()!.Location);
            Directory.SetCurrentDirectory(exeDir!);

            // Started by the scheduler of the operating system: run the job without a window
            var run = Array.IndexOf(args, Utility.ScheduledRun.Argument);
            if (run >= 0 && run + 1 < args.Length) {
                Environment.ExitCode = RunScheduled(args[run + 1]).GetAwaiter().GetResult();
                return;
            }

            BuildAvaloniaApp()
                .Start(AppMain, args);
        }

        private static async Task<int> RunScheduled(string jobId)
        {
            // settings.json belongs to the GUI, which may be open: never written here
            var settings = await LoadSettings();
            return settings == null ? 2 : await Utility.ScheduledRun.Run(settings, jobId);
        }

        /// <summary>
        /// Set the local paths and read the settings (null when there are none)
        /// </summary>
        private static async Task<AppSettings?> LoadSettings()
        {
            App.LocalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MpbkpuGUI");
            if (!Directory.Exists(App.LocalPath))
                Directory.CreateDirectory(App.LocalPath);
            App.SettingFile = Path.Combine(App.LocalPath, "settings.json");
            return File.Exists(App.SettingFile) ? await AppSettings.Load(App.SettingFile) : null;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseSkia();
        }

        private static void AppMain(Application app, string[] args)
        {
            var stop = new CancellationTokenSource();
            var mApp = ((App)app);

            async void Start()
            {
                mApp.MainWindow = await InitializeApp(args);
                mApp.MainWindow.Closed += (_, __) => stop.Cancel();
                mApp.MainWindow.Show();
                mApp.MainWindow.Activate();
            }
            Start();
            mApp.Run(stop.Token);
        }

        private static async Task<Views.MainWindow> InitializeApp(string[] args)
        {
            App.Settings = await LoadSettings();
            if (App.Settings == null) {
                App.Settings = new AppSettings();
                await App.Settings.Save(App.SettingFile!);
            } else if (App.Settings.HasLegacySecrets) {
                // Move the plain-text secrets of older versions to the secret store
                await App.Settings.Save(App.SettingFile!);
            }

            Localizer.Localizer.Instance.LoadLanguage(App.Settings.Language);
            App.ApplyTheme(App.Settings.Theme);
            Utility.AccentPalette.Apply(App.Settings.Accent);
            // MEGA accounts with two-factor authentication: the code is asked once per account and run of the app
            MpbkpuLib.FileManagers.Mega.TwoFactorCodeProvider = user => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                (await new Dialogs.PasswordDialog(false, string.Format(Localizer.Localizer.Instance["MegaTwoFactorCode"], user), 6)
                    .Show<Dialogs.PasswordDialog.Result?>())?.Password);

            return new Views.MainWindow();
        }
    }
}
