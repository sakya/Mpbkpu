using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using System;

namespace MpbkpuGUI.Controls
{
    public class TitleBar : UserControl
    {
        public TitleBar()
        {
            InitializeComponent();

            CanMinimize = true;
            CanMaximize = true;
        }

        public bool CanMinimize { get; set; }
        public bool CanMaximize { get; set; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            IsVisible = OperatingSystem.IsWindows();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            if (this.VisualRoot is Window pw) {
                SetTitle(pw.Title);
                var title = pw.GetObservable(Window.TitleProperty);
                title.Subscribe(value =>
                {
                    SetTitle(value);
                });

                var canResize = pw.GetObservable(Window.CanResizeProperty);
                canResize.Subscribe(value =>
                {
                    this.FindControl<Button>("m_MaximizeBtn").IsEnabled = CanMaximize && value;
                });

                var wState = pw.GetObservable(Window.WindowStateProperty);
                wState.Subscribe(s =>
                {
                    var btn = this.FindControl<Button>("m_MaximizeBtn");
                    if (s == WindowState.Maximized) {
                        pw.Padding = new Thickness(5);
                        btn.Content = new Projektanker.Icons.Avalonia.Icon() { Value = "fas fa-window-restore" };
                    } else {
                        pw.Padding = new Thickness(0);
                        btn.Content = new Projektanker.Icons.Avalonia.Icon() { Value = "fas fa-window-maximize" };
                    }
                });
            }

            var btn = this.FindControl<Button>("m_MinimizeBtn");
            btn.Click += (e, a) =>
            {
                (this.VisualRoot as Window).WindowState = WindowState.Minimized;
            };
            btn.IsVisible = CanMinimize;

            btn = this.FindControl<Button>("m_MaximizeBtn");
            btn.Click += (e, a) =>
            {
                var pw = this.VisualRoot as Window;
                if (pw.WindowState == WindowState.Maximized)
                    pw.WindowState = WindowState.Normal;
                else
                    pw.WindowState = WindowState.Maximized;
            };
            btn.IsVisible = CanMinimize;

            btn = this.FindControl<Button>("m_CloseBtn");
            btn.Click += (e, a) =>
            {
                ((Window)this.VisualRoot).Close();
            };
        }

        private void SetTitle(string title)
        {
            var txt = this.FindControl<TextBlock>("m_Title");
            txt.Text = title;
        }
    }
}
