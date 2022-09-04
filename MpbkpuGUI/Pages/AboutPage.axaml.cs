using System.Reflection;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
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
            Copyright = ((AssemblyCopyrightAttribute)System.Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyCopyrightAttribute), false)).Copyright
        };
        DataContext = _model;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}