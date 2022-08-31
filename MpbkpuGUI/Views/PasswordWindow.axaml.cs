using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Views;

public class PasswordWindow : BaseView
{
    public PasswordWindow()
    {
        InitializeComponent();
    }

    protected sealed override void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        base.InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FindControl<TextBox>("Password").Focus();
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