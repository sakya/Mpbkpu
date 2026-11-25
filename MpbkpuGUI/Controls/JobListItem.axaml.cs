using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MpbkpuGUI.Abstracts;
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
                    icon.Kind = MaterialIconKind.ArchiveOutline;
                    typeText.Text = Localizer.Localizer.Instance[$"BackupType_{bkp.Type ?? MpbkpuLib.DataModel.Backup.BackupTypes.Full}"];
                } else {
                    // Synchronizations use a green tile to tell them apart from backups
                    icon.Kind = MaterialIconKind.Sync;
                    icon.Foreground = new SolidColorBrush(Color.Parse("#6EE7B7"));
                    this.FindControl<Border>("IconTile")!.Background = new SolidColorBrush(Color.Parse("#1C2A26"));
                    typeText.Text = Localizer.Localizer.Instance["Sync"];
                    this.FindControl<TextBlock>("RunText")!.Text = Localizer.Localizer.Instance["Synchronize"];
                }
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

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        JobOpen?.Invoke(this, _item!);
    }
}