using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using MpbkpuGUI.Models;

namespace MpbkpuGUI
{
    public class App : Application
    {
        public Window? MainWindow { get; set; }
        public static string? LocalPath;
        public static string? SettingFile;
        public static AppSettings? Settings;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new Views.MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        public void Run(CancellationToken ct)
        {
            try {
                Dispatcher.UIThread.MainLoop(ct);
            } catch (Exception ex) {
                // Log the exception
                using (var sw = new StreamWriter(Path.Combine(LocalPath!, "crash.log"))) {
                    Exception? lex = ex;
                    while (lex != null) {
                        sw.WriteLine($"[{DateTime.Now.ToString("yyyyMMdd_HHmmss")}] {lex.Message}");
                        if (!string.IsNullOrEmpty(lex.StackTrace))
                            sw.WriteLine($"[{DateTime.Now.ToString("yyyyMMdd_HHmmss")}] {lex.StackTrace}");

                        lex = lex.InnerException;
                    }
                    sw.Flush();
                }
            }
        } // Run

        public static Window? GetMainWindow()
        {
            if (Current is App app && app.MainWindow != null) {
                return app.MainWindow;
            }
            return null;
        }

        public static void UpdateLayout()
        {
            if (Current is App app && app.MainWindow != null) {
                app.MainWindow.LayoutManager.ExecuteLayoutPass();
            }
        }

        public static Color? GetStyleColor(string name)
        {
            if (Current is App app && app.MainWindow != null) {
                var resource = app.MainWindow.FindResource(name);
                if (resource is Color col)
                    return col;
            }
            return null;
        }
    }
}
