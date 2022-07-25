using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib;
using System;

namespace MpbkpuGUI.Views
{
    public abstract class BaseView : Window
    {
        private bool m_CenterDone = false;

        protected virtual void InitializeComponent()
        {
#if DEBUG
            this.AttachDevTools();
#endif

            // Fix for https://github.com/AvaloniaUI/Avalonia/issues/6433
            if (!OperatingSystem.IsWindows()) {
                var iv = this.GetObservable(Window.IsVisibleProperty);
                iv.Subscribe(value =>
                {
                    if (value && !m_CenterDone) {
                        m_CenterDone = true;
                        CenterWindow();
                    }
                });
            }
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