using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.SingleWindow;
using MpbkpuGUI.Pages;

namespace MpbkpuGUI.Views
{
    public partial class MainWindow : MainWindowBase
    {
        private Controls.TitleBar? _titleBar;

        public MainWindow()
        {
            if (OperatingSystem.IsWindows())
                Win32Properties.AddWindowStylesCallback(this, CustomTitleBarStyles);
            InitializeComponent();

            if (OperatingSystem.IsWindows()) {
                this.ExtendClientAreaToDecorationsHint = true;
                this.ExtendClientAreaTitleBarHeightHint = -1;
                this.WindowDecorations = WindowDecorations.BorderOnly;
            }
            WindowTitle = Title;
            Transition = new TransitionSettings(TransitionSettings.EnterTransitions.SlideLeft, TimeSpan.FromMilliseconds(220),
                new Avalonia.Animation.Easings.CubicEaseOut());

            Container = this.FindControl<Grid>("Container")!;
            _titleBar = this.FindControl<Controls.TitleBar>("TitleBar")!;
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            await Task.Delay(500);
            await NavigateTo(new JobListPage());
            SetWaitAnimation(false);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Window styles for the custom title bar on Windows
        /// </summary>
        private static (uint style, uint exStyle) CustomTitleBarStyles(uint style, uint exStyle)
        {
            // DWM needs WS_CAPTION for the open/minimize/maximize/restore animations;
            // WindowDecorations.BorderOnly still prevents the native caption from being painted
            const uint captionStyle = 0x00C00000;
            // WS_SYSMENU owns the native caption buttons, replaced by the custom title bar
            const uint systemMenuStyle = 0x00080000;
            return ((style | captionStyle) & ~systemMenuStyle, exStyle);
        }

        protected override void PageChanged()
        {
            // Show the back button in the title bar on every page except the first one
            _titleBar!.CanGoBack = CanNavigateBack;
        }

        private void SetWaitAnimation(bool visible)
        {
            var spinner = this.FindControl<Controls.Spinner>("Spinner")!;
            spinner.IsVisible = visible;
        } // SetWaitAnimation

    }
}
