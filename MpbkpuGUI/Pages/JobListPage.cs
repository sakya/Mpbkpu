using System;
using System.Threading.Tasks;
using Avalonia.Automation;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Pages;

public partial class JobListPage : BasePage
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
        SetThemeText();
        base.OnNavigatedTo(direction);
    }

    private void SetThemeText()
    {
        var text = $"{Localizer.Localizer.Instance["Theme"]}: {Localizer.Localizer.Instance[$"Theme_{App.Settings!.Theme}"]}";
        this.FindControl<TextBlock>("ThemeText")!.Text = text;
        AutomationProperties.SetName(this.FindControl<Button>("ThemeButton")!, text);
    }

    private async void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        var modes = Enum.GetValues<AppSettings.ThemeModes>();
        App.Settings!.Theme = modes[(Array.IndexOf(modes, App.Settings.Theme) + 1) % modes.Length];
        App.ApplyTheme(App.Settings.Theme);
        SetThemeText();
        await App.Settings.Save(App.SettingFile!);
    }

    private async void OnJobSettings(object sender, Job job)
    {
        await MainWindow!.NavigateTo(new JobSettingsPage(job));
    }

    private async void OnJobDelete(object sender, Job job)
    {
        if (await MessageDialog.ShowConfirmMessage(App.GetMainWindow(), Localizer.Localizer.Instance["ConfirmDeleteJob"], MessageDialog.Icons.Warning)) {
            App.Settings!.Jobs.Remove(job);
            await App.Settings.Save(App.SettingFile!);
            SetJobList();
        }
    }

    private async void OnNewBackupClick(object? sender, RoutedEventArgs e)
    {
        await MainWindow!.NavigateTo(new JobSettingsPage(new BackupJob()
        {
            Type = Backup.BackupTypes.Full,
            Encrypted = true
        }));
    }

    private async void OnNewSyncClick(object? sender, RoutedEventArgs e)
    {
        await MainWindow!.NavigateTo(new JobSettingsPage(new SyncJob()));
    }

    private async void OnJobRun(object sender, Job job)
    {
        if (await MessageDialog.ShowConfirmMessage(App.GetMainWindow(), Localizer.Localizer.Instance["ConfirmRunJob"])) {
            if (job is BackupJob bkp && !await EnsurePassword(bkp))
                return;
            await MainWindow!.NavigateTo(new JobRunningPage(job));
        }
    }

    /// <summary>
    /// Get the encryption password of a backup: the saved one, or asked to the user
    /// </summary>
    /// <returns>False if the user cancelled</returns>
    private static async Task<bool> EnsurePassword(BackupJob bkp)
    {
        if (!bkp.Encrypted || bkp.HasSavedPassword)
            return true;

        var res = await new PasswordDialog(App.Settings!.SecretStoreAvailable).Show<PasswordDialog.Result?>();
        if (res == null)
            return false;

        bkp.Password = res.Password;
        if (res.Remember) {
            bkp.RememberPassword = true;
            await App.Settings.Save(App.SettingFile!);
        }
        return true;
    }

    private void SetJobList()
    {
        var jobs = App.Settings!.Jobs.OrderBy(i => i.Name).ToList();
        var list = this.FindControl<ItemsControl>("JobList")!;
        list.ItemsSource = null;
        list.ItemsSource = jobs;
        this.FindControl<Control>("EmptyState")!.IsVisible = jobs.Count == 0;
        this.FindControl<TextBlock>("JobCount")!.Text = jobs.Count.ToString();
    }

    private async void OnJobOpen(object sender, Job job)
    {
        if (job is BackupJob bkp) {
            if (!await EnsurePassword(bkp))
                return;
            await MainWindow!.NavigateTo(new BackupOpenPage(bkp));
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        App.GetMainWindow()!.Close();
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        await NavigateTo(new AboutPage());
    }
}