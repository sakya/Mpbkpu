using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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

}