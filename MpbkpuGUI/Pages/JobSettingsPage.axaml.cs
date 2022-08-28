using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Pages;

public class JobSettingsPage : BasePage
{
    private Job? _job;
    public JobSettingsPage()
    {
        InitializeComponent();
    }

    public JobSettingsPage(Job job)
    {
        _job = job;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = Localizer.Localizer.Instance["JobSettings"];
        NavigateBackWithKeyboard = false;
    }

    private async void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        await MainWindow!.NavigateBack();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {

    }
}