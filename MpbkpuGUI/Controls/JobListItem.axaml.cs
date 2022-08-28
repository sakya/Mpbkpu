using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using Projektanker.Icons.Avalonia;

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

                var icon = this.FindControl<Icon>("Icon");
                if (_item is BackupJob) {
                    icon.Value = "fas fa-archive";
                } else {
                    icon.Value = "fas fa-sync";
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
}