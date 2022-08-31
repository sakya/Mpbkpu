using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuGUI.Views;

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

        PageTitle = Localizer.Localizer.Instance["JobList"];
    }

    public override void OnNavigatedTo(NavigationDirection direction)
    {
        SetJobList();
        base.OnNavigatedTo(direction);
    }

    private async void OnJobSettings(object sender, Job job)
    {
        await MainWindow!.NavigateTo(new JobSettingsPage(job));
    }

    private async void OnJobDelete(object sender, Job job)
    {
        if (await MessageWindow.ShowConfirmMessage(MainWindow, Localizer.Localizer.Instance["Confirm"],
                Localizer.Localizer.Instance["ConfirmDeleteJob"], MessageWindow.Icons.Warning)) {
            App.Settings!.Jobs.Remove(job);
            await App.Settings.Save(App.SettingFile!);
            SetJobList();
        }
    }

    private async void OnNewBackupClick(object? sender, RoutedEventArgs e)
    {
        await MainWindow!.NavigateTo(new JobSettingsPage(new BackupJob()));
    }

    private async void OnNewSyncClick(object? sender, RoutedEventArgs e)
    {
        await MainWindow!.NavigateTo(new JobSettingsPage(new SyncJob()));
    }

    private async void OnJobRun(object sender, Job job)
    {
        if (await MessageWindow.ShowConfirmMessage(MainWindow, Localizer.Localizer.Instance["Confirm"],
                Localizer.Localizer.Instance["ConfirmRunJob"])) {
            if (job is BackupJob bkp && bkp.Encrypted) {
                var dlg = new PasswordWindow();
                var pwd = await dlg.ShowDialog<string>(MainWindow);
                if (string.IsNullOrEmpty(pwd))
                    return;

                bkp.Password = pwd;
            }
            await MainWindow!.NavigateTo(new JobRunningPage(job));
        }
    }

    private void SetJobList()
    {
        var list = this.FindControl<Controls.ItemsList>("JobList");
        list.Items = null;
        list.Items = App.Settings!.Jobs.OrderBy(i => i.Name);
    }

    private async void OnJobOpen(object sender, Job job)
    {
        if (job is BackupJob bkp) {
            var dlg = new PasswordWindow();
            var pwd = await dlg.ShowDialog<string>(MainWindow);
            if (string.IsNullOrEmpty(pwd))
                return;

            bkp.Password = pwd;
            await MainWindow!.NavigateTo(new BackupOpenPage(bkp));
        }
    }
}