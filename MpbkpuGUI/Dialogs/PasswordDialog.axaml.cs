using Avalonia;
using Avalonia.Controls;
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