using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MpbkpuGUI.Controls
{
    public class OkCancelButtons : UserControl
    {
        private string? _okText;
        private bool _okEnabled;
        private string? _cancelText;
        private bool _cancelVisible;
        private bool _smallButtons;

        public static readonly DirectProperty<OkCancelButtons, string?> OkTextProperty =
                AvaloniaProperty.RegisterDirect<OkCancelButtons, string?>(
                    nameof(OkText),
                    o => o.OkText,
                    (o, v) => o.OkText = v);

        public static readonly DirectProperty<OkCancelButtons, bool> OkEnabledProperty =
                AvaloniaProperty.RegisterDirect<OkCancelButtons, bool>(
                    nameof(OkEnabled),
                    o => o.OkEnabled,
                    (o, v) => o.OkEnabled = v);

        public static readonly DirectProperty<OkCancelButtons, string?> CancelTextProperty =
                AvaloniaProperty.RegisterDirect<OkCancelButtons, string?>(
                    nameof(CancelText),
                    o => o.CancelText,
                    (o, v) => o.CancelText = v);

        public static readonly DirectProperty<OkCancelButtons, bool> CancelVisibleProperty =
                AvaloniaProperty.RegisterDirect<OkCancelButtons, bool>(
                    nameof(CancelVisible),
                    o => o.CancelVisible,
                    (o, v) => o.CancelVisible = v);

        public static readonly DirectProperty<OkCancelButtons, bool> SmallButtonsProperty =
            AvaloniaProperty.RegisterDirect<OkCancelButtons, bool>(
                nameof(SmallButtons),
                o => o.SmallButtons,
                (o, v) => o.SmallButtons = v);

        public event EventHandler<RoutedEventArgs>? OkClick;
        public event EventHandler<RoutedEventArgs>? CancelClick;

        public OkCancelButtons()
        {
            InitializeComponent();
        }

        public string? OkText
        {
            get => _okText;
            set => SetAndRaise(OkTextProperty, ref _okText, value);
        }

        public bool OkEnabled
        {
            get => _okEnabled;
            set => SetAndRaise(OkEnabledProperty, ref _okEnabled, value);
        }

        public string? CancelText
        {
            get => _cancelText;
            set => SetAndRaise(CancelTextProperty, ref _cancelText, value);
        }

        public bool CancelVisible
        {
            get => _cancelVisible;
            set
            {
                if (SetAndRaise(CancelVisibleProperty, ref _cancelVisible, value)) {
                    var btn = this.FindControl<Button>("Ok");
                    btn.IsCancel = !value;
                    Grid.SetColumn(btn, value ? 1 : 0);
                    Grid.SetColumnSpan(btn, value ? 1 : 2);
                    if (value)
                        btn.Margin = new Thickness(5,0,0,0);
                    else
                        btn.Margin = new Thickness(0, 0, 0, 0);
                }
            }
        }

        public bool SmallButtons
        {
            get { return _smallButtons; }
            set
            {
                if (SetAndRaise(SmallButtonsProperty, ref _smallButtons, value)) {
                    var btn = this.FindControl<Button>("Ok");
                    if (value)
                        btn.Classes.Add("Small");
                    else
                        btn.Classes.Remove("Small");
                    btn = this.FindControl<Button>("Cancel");
                    if (value)
                        btn.Classes.Add("Small");
                    else
                        btn.Classes.Remove("Small");
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            OkEnabled = true;
            OkText = Localizer.Localizer.Instance["Ok"];
            CancelText = Localizer.Localizer.Instance["Cancel"];
            CancelVisible = true;
            DataContext = this;

            this.FindControl<Button>("Ok").Click += (s, args) => {
                OkClick?.Invoke(this, new RoutedEventArgs());
            };
            this.FindControl<Button>("Cancel").Click += (s, args) => {
                CancelClick?.Invoke(this, new RoutedEventArgs());
            };
        }
    }
}