using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Threading.Tasks;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Views
{
    public class MessageWindow : BaseView
    {
        private static MessageWindow? _mOpenedMessageWindow;

        public enum Buttons
        {
            YesNo,
            OkCancel,
            Ok
        }

        public enum Icons
        {
            None,
            Warning,
            Error,
            Question,
            Info
        }

        public MessageWindow()
        {
        }

        public MessageWindow(string title, string message, Buttons buttons = Buttons.YesNo, Icons icon = Icons.None)
        {
            InitializeComponent();

            this.Title = title;
            var txt = this.FindControl<TextBlock>("m_Message");
            txt.Text = message;

            var btn1 = this.FindControl<Button>("m_Button1");
            var btn2 = this.FindControl<Button>("m_Button2");
            btn1.IsCancel = false;
            btn1.IsDefault = false;
            btn2.IsCancel = false;
            btn2.IsDefault = false;

            switch (buttons) {
                case Buttons.Ok:
                    btn2.IsVisible = false;
                    btn1.Content = Localizer.Localizer.Instance["Ok"];
                    btn1.IsCancel = true;
                    btn1.IsDefault = true;
                    break;
                case Buttons.OkCancel:
                    btn1.Content = Localizer.Localizer.Instance["Ok"];
                    btn1.IsDefault = true;
                    btn2.Content = Localizer.Localizer.Instance["Cancel"];
                    btn2.IsCancel = true;
                    break;
                case Buttons.YesNo:
                    btn1.Content = Localizer.Localizer.Instance["Yes"];
                    btn1.IsDefault = true;
                    btn2.Content = Localizer.Localizer.Instance["No"];
                    btn2.IsCancel = true;
                    break;
            }

            var i = this.FindControl<Projektanker.Icons.Avalonia.Icon>("m_Icon");
            switch (icon) {
                case Icons.None:
                    i.IsVisible = false;
                    break;
                case Icons.Warning:
                    i.Value = "fas fa-exclamation-triangle";
                    var wc = App.GetStyleColor("WarningColor");
                    if (wc != null)
                        i.Foreground = new SolidColorBrush(wc.Value);
                    break;
                case Icons.Error:
                    i.Value = "fas fa-exclamation-triangle";
                    var ec = App.GetStyleColor("DangerColor");
                    if (ec != null)
                        i.Foreground = new SolidColorBrush(ec.Value);
                    break;
                case Icons.Info:
                    i.Value = "fas fa-info-circle";
                    var ic = App.GetStyleColor("InfoColor");
                    if (ic != null)
                        i.Foreground = new SolidColorBrush(ic.Value);
                    break;
                case Icons.Question:
                    i.Value = "fas fa-question-circle";
                    var qc = App.GetStyleColor("InfoColor");
                    if (qc != null)
                        i.Foreground = new SolidColorBrush(qc.Value);
                    break;
            }
        }

        protected sealed override void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            base.InitializeComponent();
        }

        private void OnButton1Click(object sender, RoutedEventArgs e)
        {
            this.Close(true);
        }

        private void OnButton2Click(object sender, RoutedEventArgs e)
        {
            this.Close(false);
        }

        #region static operations
        public static void CloseOpenedWindow()
        {
            if (_mOpenedMessageWindow != null) {
                _mOpenedMessageWindow.Close(false);
                _mOpenedMessageWindow = null;
            }
        } // CloseOpenedWindow

        public static async Task<bool> ShowMessage(Window? owner, string title, string message, Icons icon = Icons.None)
        {
            CloseOpenedWindow();
            _mOpenedMessageWindow = new MessageWindow(title, message, Buttons.Ok, icon);
            await _mOpenedMessageWindow.ShowDialog<bool>(owner);

            return true;
        } // ShowMessage

        public static async Task<bool> ShowConfirmMessage(Window? owner, string title, string message, Icons icon = Icons.Question)
        {
            CloseOpenedWindow();
            _mOpenedMessageWindow = new MessageWindow(title, message, Buttons.YesNo, icon);
            return await _mOpenedMessageWindow.ShowDialog<bool>(owner);
        } // ShowConfirmMessage
        #endregion
    }
}
