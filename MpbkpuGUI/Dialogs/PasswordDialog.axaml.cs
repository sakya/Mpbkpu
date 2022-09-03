using System.Runtime.InteropServices.ComTypes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Dialogs.Models;

namespace MpbkpuGUI.Dialogs;

public class PasswordDialog : BaseDialog
{
    private readonly PasswordDialogModel _model;
    public PasswordDialog()
    {
        _model = new PasswordDialogModel();
        DataContext = _model;

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        KeyDown += OnKeyDown;
    }

    protected override void Opened()
    {
        this.FindControl<TextBox>("Password").Focus();
        base.Opened();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Enter) {
           OnOkClick(null, new RoutedEventArgs());
        } else if (e.Key == Key.Escape) {
            OnCancelClick(null, new RoutedEventArgs());
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_model.Password))
            Close(_model.Password);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}