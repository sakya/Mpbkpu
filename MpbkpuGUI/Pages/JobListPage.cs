using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Pages;

public class JobListPage : BasePage
{
    public JobListPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = "Page 1";
    }

    public override void OnNavigatedTo(NavigationDirection direction)
    {
        var list = this.FindControl<ListBox>("JobList");
        list.Items = App.Settings!.Jobs.OrderBy(i => i.Name);

        base.OnNavigatedTo(direction);
    }

    private async void OnJobSettings(object sender, Job job)
    {
        await MainWindow.NavigateTo(new JobSettingsPage());
    }
}