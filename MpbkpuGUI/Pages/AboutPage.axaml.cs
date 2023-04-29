using System.Reflection;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Pages;

public class AboutPage : BasePage
{
    private readonly AboutPageModel _model;
    public AboutPage()
    {
        _model = new AboutPageModel()
        {
            Title = $"Mpbkpu v.{Assembly.GetEntryAssembly()!.GetName().Version}",
            Copyright = ((AssemblyCopyrightAttribute)System.Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyCopyrightAttribute), false)).Copyright,
            Description = "A multi platform backup utility with encryption and compression feature.\nSupports local and remote file system."
        };
        DataContext = _model;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}