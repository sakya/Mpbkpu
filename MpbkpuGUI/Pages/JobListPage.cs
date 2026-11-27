using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Automation;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using Avalonia.Threading;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Utility;
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
        // The check at startup ends after the page is shown (the job list page lives as long as the window)
        UpdateChecker.UpdateAvailable += (_, _) => Dispatcher.UIThread.Post(SetUpdateButton);

        // Results of automatic runs written while the window is open; merged only while the list is shown, so a
        // job being edited in another page is not changed under it (merged when coming back)
        _resultsWatcher = ScheduledRun.WatchResults(() => Dispatcher.UIThread.Post(() =>
        {
            if (IsEffectivelyVisible)
                _ = MergeScheduledResults();
        }));
    }

    private IDisposable? _resultsWatcher;

    public override void Dispose()
    {
        _resultsWatcher?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Show the results of the automatic runs in the jobs and report the failed ones
    /// </summary>
    private async Task MergeScheduledResults()
    {
        var merged = await ScheduledRun.MergeAndSave();
        if (merged.Count == 0)
            return;
        SetJobList();

        var localizer = Localizer.Localizer.Instance;
        var failed = merged
            .Where(m => m.Result.Outcome is not (Backup.Results.Completed or Backup.Results.CompletedWithWarnings))
            .Select(m => $"• {m.Job.Name} ({m.Result.EndedAt.ToString("g", new CultureInfo(App.Settings!.Language))}): " +
                         (m.Result.PasswordNotSaved ? localizer["SchedulePasswordMissing"] : m.Result.ErrorMessage ?? localizer["Result_Error"]))
            .ToList();
        if (failed.Count > 0)
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                $"{localizer["ScheduledRunsFailed"]}\n{string.Join("\n", failed)}", MessageDialog.Icons.Error);
    }

    public override void OnNavigatedTo(NavigationDirection direction)
    {
        _ = MergeScheduledResults();
        SetJobList();
        SetUpdateButton();
        base.OnNavigatedTo(direction);
    }

    private void SetUpdateButton()
    {
        this.FindControl<Button>("UpdateButton")!.IsVisible = UpdateChecker.Available != null;
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        await NavigateTo(new SettingsPage());
    }

    private async void OnJobSettings(object sender, Job job)
    {
        await MainWindow!.NavigateTo(new JobSettingsPage(job));
    }

    private async void OnJobDelete(object sender, Job job)
    {
        if (await MessageDialog.ShowConfirmMessage(App.GetMainWindow(), Localizer.Localizer.Instance["ConfirmDeleteJob"], MessageDialog.Icons.Warning)) {
            try {
                JobScheduler.Current?.Delete(job.Id);
            } catch (Exception ex) {
                // The job stays: its automatic runs would otherwise continue without it
                await MessageDialog.ShowMessage(App.GetMainWindow(), $"{Localizer.Localizer.Instance["ScheduleFailed"]}{ex.Message}",
                    MessageDialog.Icons.Error);
                return;
            }
            ScheduledRun.DeleteFiles(job.Id);
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
        // Hidden where no scheduler is supported
        this.FindControl<Button>("ScheduledButton")!.IsVisible = JobScheduler.Current != null;
        this.FindControl<TextBlock>("ScheduledCount")!.Text = jobs.Count(j => j.Schedule is { Enabled: true }).ToString();
    }

    private async void OnScheduledClick(object? sender, RoutedEventArgs e)
    {
        await NavigateTo(new SchedulePage());
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