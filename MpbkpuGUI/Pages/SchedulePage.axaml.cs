using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using Avalonia.Threading;
using MpbkpuGUI.Models;
using MpbkpuGUI.Utility;
using Backup = MpbkpuLib.DataModel.Backup;

namespace MpbkpuGUI.Pages;

/// <summary>
/// The scheduled jobs, the next run of each and the result of its last run (automatic or by hand)
/// </summary>
public partial class SchedulePage : BasePage
{
    private readonly IDisposable? _resultsWatcher;

    public SchedulePage()
    {
        AvaloniaXamlLoader.Load(this);
        PageTitle = Localizer.Localizer.Instance["Scheduled"];
        // Automatic runs that end while the page is shown
        _resultsWatcher = ScheduledRun.WatchResults(() => Dispatcher.UIThread.Post(async () =>
        {
            if (IsEffectivelyVisible && (await ScheduledRun.MergeAndSave()).Count > 0)
                SetList();
        }));
    }

    public override void Dispose()
    {
        _resultsWatcher?.Dispose();
        base.Dispose();
    }

    public override async void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);
        await ScheduledRun.MergeAndSave();
        // Also when coming back from the settings of a job
        SetList();
    }

    private void SetList()
    {
        var localizer = Localizer.Localizer.Instance;
        var culture = new CultureInfo(App.Settings!.Language);
        var now = DateTime.Now;
        var rows = App.Settings.Jobs
            .Where(j => j.Schedule is { Enabled: true })
            .Select(j =>
            {
                var next = j.Schedule!.NextRun(now);
                var result = j.LastRunAutomatic ? ScheduledRun.ReadResult(j.Id) : null;
                var failed = j.LastResult is not (null or Backup.Results.Completed or Backup.Results.CompletedWithWarnings);
                return new ScheduledJobRow()
                {
                    Job = j,
                    TypeText = localizer[j is BackupJob ? "Backup" : "Sync"],
                    ScheduleText = j.Schedule.Describe(culture),
                    NextRunText = next == null ? null : string.Format(localizer["NextRun"], next.Value.ToString("f", culture)),
                    LastRunText = j.LastRun == null ? localizer["NeverRun"] :
                        $"{localizer["LastRunLabel"]}: {j.LastRun.Value.ToString("g", culture)} " +
                        $"({localizer[j.LastRunAutomatic ? "RunAutomatic" : "RunByHand"]})",
                    ErrorText = failed && result != null ?
                        result.PasswordNotSaved ? localizer["SchedulePasswordMissing"] : result.ErrorMessage : null,
                    HasLog = j.LastRunAutomatic && File.Exists(ScheduledRun.GetLogFile(j.Id))
                };
            })
            .OrderBy(r => r.Job.Schedule!.NextRun(now) ?? DateTime.MaxValue)
            .ToList();

        this.FindControl<ItemsControl>("List")!.ItemsSource = rows;
        this.FindControl<Control>("EmptyState")!.IsVisible = rows.Count == 0;
    }

    private async void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ScheduledJobRow row)
            await ScheduledRun.OpenLog(this, row.Job.Id);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ScheduledJobRow row)
            await NavigateTo(new JobSettingsPage(row.Job));
    }

    private async void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}
