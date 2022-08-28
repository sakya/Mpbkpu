using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dropbox.Api.Team;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Pages;

public partial class JobRunningPage : BasePage
{
    public JobRunningPage()
    {
        InitializeComponent();
    }

    public JobRunningPage(Job job)
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}