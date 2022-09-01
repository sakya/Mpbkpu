using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Dialogs;

public partial class MessageDialog : BaseDialog
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnButton2Click(object? sender, RoutedEventArgs e)
    {

    }

    private void OnButton1Click(object? sender, RoutedEventArgs e)
    {

    }
}