using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Controls;

public partial class Position : UserControl
{
    public static readonly DirectProperty<Position, Job.Position?> ValueProperty =
        AvaloniaProperty.RegisterDirect<Position, Job.Position?>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v);

    private Job.Position? _position;

    public Position()
    {
        InitializeComponent();
    }

    public Job.Position? Value
    {
        get => _position;
        set
        {
            if (SetAndRaise(ValueProperty, ref _position, value)) {
                DataContext = _position;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {

    }
}