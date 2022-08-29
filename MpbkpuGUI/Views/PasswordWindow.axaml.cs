using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Views;

public partial class PasswordWindow : BaseView
{
    public PasswordWindow() : base()
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
        this.Close(pwd.Text);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        this.Close(null);
    }
}