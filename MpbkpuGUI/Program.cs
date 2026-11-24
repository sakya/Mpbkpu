using Avalonia;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuGUI.Models;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;

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

            BuildAvaloniaApp()
                .Start(AppMain, args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new X11PlatformOptions { UseGpu = true })
                .With(new Win32PlatformOptions { AllowEglInitialization = true })
                .LogToTrace()
                .UseSkia()
                .WithIcons(container => container.Register<FontAwesomeIconProvider>());
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
            App.LocalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MpbkpuGUI");
            if (!Directory.Exists(App.LocalPath))
                Directory.CreateDirectory(App.LocalPath);
            App.SettingFile = Path.Combine(App.LocalPath, "settings.json");
            if (File.Exists(App.SettingFile))
                App.Settings = await AppSettings.Load(App.SettingFile);

            if (App.Settings == null) {
                App.Settings = new AppSettings();
                await App.Settings.Save(App.SettingFile);
            }

            Localizer.Localizer.Instance.LoadLanguage(App.Settings.Language);

            return new Views.MainWindow();
        }
    }
}
