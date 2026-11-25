using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Dialogs.Models;
using Material.Icons;

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

    private static MessageDialog? _openedMessageDialog;
    private readonly MessageDialogModel _model;
    public MessageDialog()
    {
        _model = new MessageDialogModel();
        DataContext = _model;
    }

    public MessageDialog(string message, Buttons buttons = Buttons.YesNo, Icons icon = Icons.None)
    {
        _model = new MessageDialogModel();
        DataContext = _model;
        VerticalAlignment = VerticalAlignment.Bottom;
        InitializeComponent();

        KeyDown += OnKeyDown;

        _model.Message = message;

        _model.Button1Data.IsCancel = false;
        _model.Button1Data.IsDefault = false;
        _model.Button2Data.IsCancel = false;
        _model.Button2Data.IsDefault = false;

        switch (buttons) {
            case Buttons.Ok:
                _model.Button2Data.IsVisible = false;
                _model.Button1Data.Column = 0;
                _model.Button1Data.ColumnSpan = 2;
                _model.Button1Data.Text = Localizer.Localizer.Instance["Ok"];
                _model.Button1Data.IsCancel = true;
                _model.Button1Data.IsDefault = true;
                break;
            case Buttons.OkCancel:
                _model.Button1Data.Text = Localizer.Localizer.Instance["Ok"];
                _model.Button1Data.IsDefault = true;
                _model.Button2Data.Text = Localizer.Localizer.Instance["Cancel"];
                _model.Button2Data.IsCancel = true;
                break;
            case Buttons.YesNo:
                _model.Button1Data.Text = Localizer.Localizer.Instance["Yes"];
                _model.Button1Data.IsDefault = true;
                _model.Button2Data.Text = Localizer.Localizer.Instance["No"];
                _model.Button2Data.IsCancel = true;
                break;
        }

        switch (icon) {
            case Icons.None:
                _model.IconVisible = false;
                break;
            case Icons.Warning:
                _model.Icon = MaterialIconKind.Alert;
                var wc = App.GetStyleColor("WarningColor");
                if (wc != null)
                    _model.IconBrush = new SolidColorBrush(wc.Value);
                break;
            case Icons.Error:
                _model.Icon = MaterialIconKind.Alert;
                var ec = App.GetStyleColor("DangerColor");
                if (ec != null)
                    _model.IconBrush = new SolidColorBrush(ec.Value);
                break;
            case Icons.Info:
                _model.Icon = MaterialIconKind.Information;
                var ic = App.GetStyleColor("InfoColor");
                if (ic != null)
                    _model.IconBrush = new SolidColorBrush(ic.Value);
                break;
            case Icons.Question:
                _model.Icon = MaterialIconKind.HelpCircle;
                var qc = App.GetStyleColor("InfoColor");
                if (qc != null)
                    _model.IconBrush = new SolidColorBrush(qc.Value);
                break;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnKeyDown(object? sender, KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var cancelBtn = _model.Button1Data.IsCancel  || _model.Button2Data.IsCancel;
        var defaultBtn = _model.Button1Data.IsDefault  || _model.Button2Data.IsDefault;

        if (e.Key == Key.Enter && defaultBtn) {
            e.Handled = true;
            Close(true);
        } else if (e.Key == Key.Escape && cancelBtn) {
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

    private static void CloseOpenedWindow()
    {
        if (_openedMessageDialog != null) {
            _openedMessageDialog.Close(false);
            _openedMessageDialog = null;
        }
    } // CloseOpenedWindow

    public static async Task<bool> ShowMessage(Window? owner, string message, Icons icon = Icons.None)
    {
        CloseOpenedWindow();
        _openedMessageDialog = new MessageDialog(message, MessageDialog.Buttons.Ok, icon);
        await _openedMessageDialog.Show<bool>();

        return true;
    } // ShowMessage

    public static async Task<bool> ShowConfirmMessage(Window? owner, string message, Icons icon = Icons.Question)
    {
        CloseOpenedWindow();
        _openedMessageDialog = new MessageDialog(message, Buttons.YesNo, icon);
        return await _openedMessageDialog.Show<bool>();
    } // ShowConfirmMessage

}