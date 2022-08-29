using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib;
using System;

namespace MpbkpuGUI.Abstracts
{
    public abstract class BaseView : Window
    {
        protected virtual void InitializeComponent()
        {
#if DEBUG
            this.AttachDevTools();
#endif
            if (OperatingSystem.IsWindows()) {
                this.ExtendClientAreaToDecorationsHint = true;
                this.ExtendClientAreaTitleBarHeightHint = -1;
                this.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            }
        }

        protected override void OnOpened(EventArgs e)
        {
            // Fix for https://github.com/AvaloniaUI/Avalonia/issues/6433
            if (!OperatingSystem.IsWindows())
                CenterWindow();
        }

        private async void CenterWindow()
        {
            if (this.WindowStartupLocation == WindowStartupLocation.Manual)
                return;

            await Task.Delay(1);
            double scale = PlatformImpl?.DesktopScaling ?? 1.0;
            IWindowBaseImpl? powner = Owner?.PlatformImpl;
            if (powner != null) {
                scale = powner.DesktopScaling;
            }
            PixelRect rect = new PixelRect(PixelPoint.Origin,
                PixelSize.FromSize(ClientSize, scale));
            if (WindowStartupLocation == WindowStartupLocation.CenterScreen) {
                Screen? screen = Screens.ScreenFromPoint(powner?.Position ?? Position);
                if (screen == null)
                    return;
                Position = screen.WorkingArea.CenterRect(rect).Position;
            } else {
                if (powner == null || WindowStartupLocation != WindowStartupLocation.CenterOwner)
                    return;
                Position = new PixelRect(powner.Position,
                    PixelSize.FromSize(powner.ClientSize, scale)).CenterRect(rect).Position;
            }
        } // CenterWindow
    }
}