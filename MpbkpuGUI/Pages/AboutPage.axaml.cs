using System.Reflection;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Pages;

public partial class AboutPage : BasePage
{
    public AboutPage()
    {
        DataContext = new AboutPageModel()
        {
            Title = $"Mpbkpu v.{Assembly.GetEntryAssembly()!.GetName().Version}",
            Copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright,
            Description = "A multi platform backup utility with encryption and compression feature.\nSupports local and remote file system."
        };
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}
