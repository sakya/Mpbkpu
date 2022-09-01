using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Dialogs;

public partial class MessageDialog : BaseDialog
{
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

    public MessageDialog()
    {
    }

    public MessageDialog(string message, Buttons buttons = Buttons.YesNo, Icons icon = Icons.None)
    {
        InitializeComponent();

        KeyDown += OnKeyDown;

        var txt = this.FindControl<TextBlock>("Message");
        txt.Text = message;

        var btn1 = this.FindControl<Button>("Button1");
        var btn2 = this.FindControl<Button>("Button2");
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

        var i = this.FindControl<Projektanker.Icons.Avalonia.Icon>("Icon");
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var btn1 = this.FindControl<Button>("Button1");
        var btn2 = this.FindControl<Button>("Button2");

        var cancelBtn = btn1.IsCancel ? btn1 : btn2.IsCancel ? btn2 : null;
        var defaultBtn = btn1.IsDefault ? btn1 : btn2.IsDefault ? btn2 : null;

        if (e.Key == Key.Enter && defaultBtn != null) {
            e.Handled = true;
            Close(true);
        } else if (e.Key == Key.Escape && cancelBtn != null) {
            e.Handled = true;
            Close(false);
        }
    }

    private void OnButton1Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnButton2Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}