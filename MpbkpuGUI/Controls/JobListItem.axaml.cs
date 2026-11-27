using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuGUI.Utility;
using Material.Icons;
using Material.Icons.Avalonia;

namespace MpbkpuGUI.Controls;

public partial class JobListItem : UserControl
{
    public static readonly DirectProperty<JobListItem, Job?> ItemProperty =
        AvaloniaProperty.RegisterDirect<JobListItem, Job?>(
            nameof(Item),
            o => o.Item,
            (o, v) => o.Item = v);

    private Job? _item;

    #region events
    public delegate void JobEventHandler(object sender, Job job);
    public event JobEventHandler? JobSettings;
    public event JobEventHandler? JobDelete;
    public event JobEventHandler? JobRun;
    public event JobEventHandler? JobOpen;
    #endregion

    public JobListItem()
    {
        InitializeComponent();
    }

    public Job? Item
    {
        get => _item;
        set
        {
            if (SetAndRaise(ItemProperty, ref _item, value)) {
                DataContext = _item;

                var icon = this.FindControl<MaterialIcon>("Icon")!;
                var typeText = this.FindControl<TextBlock>("TypeText")!;
                if (_item is BackupJob bkp) {
                    icon.Kind = MaterialIconKind.BackupOutline;
                    typeText.Text = Localizer.Localizer.Instance[$"BackupType_{bkp.Type ?? MpbkpuLib.DataModel.Backup.BackupTypes.Full}"];
                } else {
                    // Synchronizations use a green tile to tell them apart from backups
                    icon.Kind = MaterialIconKind.Sync;
                    icon.Bind(MaterialIcon.ForegroundProperty, this.GetResourceObservable("SyncBrush"));
                    this.FindControl<Border>("IconTile")!.Bind(Border.BackgroundProperty, this.GetResourceObservable("SyncSoftBrush"));
                    typeText.Text = Localizer.Localizer.Instance["Sync"];
                    this.FindControl<TextBlock>("RunText")!.Text = Localizer.Localizer.Instance["Synchronize"];
                }

                ShowDestinations();
                ShowSchedule();
            }
        }
    }

    /// <summary>
    /// The schedule chip and the log of the last automatic run
    /// </summary>
    private void ShowSchedule()
    {
        var culture = new CultureInfo(App.Settings!.Language);
        var chip = this.FindControl<Border>("ScheduleChip")!;
        var schedule = _item?.Schedule;
        chip.IsVisible = schedule is { Enabled: true } && JobScheduler.Current != null;
        if (chip.IsVisible) {
            this.FindControl<TextBlock>("ScheduleText")!.Text = schedule!.Describe(culture);
            var next = schedule.NextRun(DateTime.Now);
            ToolTip.SetTip(chip, next == null ? null : string.Format(Localizer.Localizer.Instance["NextRun"], next.Value.ToString("f", culture)));
        }

        this.FindControl<Button>("LogButton")!.IsVisible =
            _item is { LastRunAutomatic: true } && File.Exists(ScheduledRun.GetLogFile(_item.Id));
    }

    private async void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        if (_item != null)
            await ScheduledRun.OpenLog(this, _item.Id);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Copy to the clipboard the CLI command that runs the job, without secrets
    /// </summary>
    private async void OnCopyCommandClick(object? sender, RoutedEventArgs e)
    {
        if (_item == null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        // The installer puts the CLI next to the GUI; otherwise rely on the PATH
        var executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "mpbkpu.exe" : "mpbkpu");
        if (!File.Exists(executable))
            executable = "mpbkpu";
        var res = _item.GetCommandLine(executable);
        await clipboard.SetTextAsync(res.Command);

        var localizer = Localizer.Localizer.Instance;
        var message = localizer["CommandCopied"];
        if (_item is BackupJob { Encrypted: true })
            message += $"\n{localizer["CommandEncryptionPassword"]}";
        if (res.HasPlaceholders)
            message += $"\n{string.Format(localizer["CommandPlaceholders"], MpbkpuLib.Utility.CommandLine.PasswordPlaceholder)}";
        await MessageDialog.ShowMessage(App.GetMainWindow(), message, MessageDialog.Icons.Info);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        JobSettings?.Invoke(this, _item!);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        JobDelete?.Invoke(this, _item!);
    }

    private void OnRunClick(object? sender, RoutedEventArgs e)
    {
        JobRun?.Invoke(this, _item!);
    }

    /// <summary>
    /// Show the paths of the job and the space of every destination
    /// </summary>
    private void ShowDestinations()
    {
        var destinations = _item?.Destinations ?? [];
        this.FindControl<TextBlock>("Paths")!.Text = _item?.GetPathsText();

        var list = this.FindControl<StackPanel>("SpaceList")!;
        list.Children.Clear();
        foreach (var destination in destinations)
            list.Children.Add(new DestinationSpaceView(destination, destinations.Count > 1));
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        JobOpen?.Invoke(this, _item!);
    }
}