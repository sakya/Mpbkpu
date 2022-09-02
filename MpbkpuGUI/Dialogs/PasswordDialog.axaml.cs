using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Dialogs;

public partial class PasswordDialog : BaseDialog
{
    public PasswordDialog()
    {
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
        var pwd = this.FindControl<TextBox>("Password");
        if (!string.IsNullOrEmpty(pwd.Text))
            Close(pwd.Text);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}