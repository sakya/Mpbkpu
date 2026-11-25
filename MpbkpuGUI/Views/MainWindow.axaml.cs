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
            InitializeComponent();

            if (OperatingSystem.IsWindows()) {
                this.ExtendClientAreaToDecorationsHint = true;
                this.ExtendClientAreaTitleBarHeightHint = -1;
                this.WindowDecorations = WindowDecorations.BorderOnly;
            }
            WindowTitle = Title;
            Transition = new TransitionSettings(TransitionSettings.EnterTransitions.SlideLeft, TimeSpan.FromMilliseconds(220),
                new Avalonia.Animation.Easings.CubicEaseOut());

            Container = this.FindControl<Grid>("Container");
            _titleBar = this.FindControl<Controls.TitleBar>("TitleBar");
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

        protected override void PageChanged()
        {
            // Show the back button in the title bar on every page except the first one
            _titleBar!.CanGoBack = CanNavigateBack;
        }

        private void SetWaitAnimation(bool visible)
        {
            var spinner = this.FindControl<Controls.Spinner>("Spinner");
            spinner.IsVisible = visible;
        } // SetWaitAnimation

    }
}
