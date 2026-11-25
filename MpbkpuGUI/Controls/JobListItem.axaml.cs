using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
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
                    icon.Foreground = new SolidColorBrush(Color.Parse("#6EE7B7"));
                    this.FindControl<Border>("IconTile")!.Background = new SolidColorBrush(Color.Parse("#1C2A26"));
                    typeText.Text = Localizer.Localizer.Instance["Sync"];
                    this.FindControl<TextBlock>("RunText")!.Text = Localizer.Localizer.Instance["Synchronize"];
                }

                ShowSpace();
                // Local destinations are cheap to read: always show the current space
                if (_item?.Destination?.IsLocal == true)
                    _ = RefreshSpace(false);
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
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

    private async void OnRefreshSpaceClick(object? sender, RoutedEventArgs e)
    {
        await RefreshSpace(true);
    }

    /// <summary>
    /// Read the space of the destination, save it in the job and show it
    /// </summary>
    /// <param name="showErrors">Show a message if the space cannot be read</param>
    private async Task RefreshSpace(bool showErrors)
    {
        var job = _item;
        if (job?.Destination == null)
            return;

        var button = this.FindControl<Button>("SpaceRefresh")!;
        button.IsEnabled = false;
        try {
            var space = await Task.Run(async () =>
            {
                using var fm = job.Destination.GetFileManager(out var path);
                return await fm.GetSpace(path);
            });
            job.DestinationSpace = space;
            job.DestinationSpaceRead = DateTime.Now;
            if (!job.Destination.IsLocal)
                await App.Settings!.Save(App.SettingFile!);
        } catch (Exception ex) {
            if (showErrors) {
                await MessageDialog.ShowMessage(App.GetMainWindow(),
                    $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
            }
        } finally {
            button.IsEnabled = true;
        }

        if (ReferenceEquals(job, _item))
            ShowSpace();
    }

    private void ShowSpace()
    {
        var job = _item;
        var bar = this.FindControl<ProgressBar>("SpaceBar")!;
        var text = this.FindControl<TextBlock>("SpaceText")!;
        this.FindControl<Button>("SpaceRefresh")!.IsVisible = job?.Destination?.IsLocal == false;
        SpaceDisplay.Show(job?.DestinationSpace, bar, text);
        ToolTip.SetTip(text, job?.DestinationSpaceRead == null ? null :
            string.Format(Localizer.Localizer.Instance["SpaceReadAt"], job.DestinationSpaceRead.Value.ToString("g")));
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        JobOpen?.Invoke(this, _item!);
    }
}