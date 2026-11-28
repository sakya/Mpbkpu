using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MpbkpuGUI.Controls;

/// <summary>
/// Percentage, phase, step and progress bar of a running job (<see cref="Models.JobProgressModel"/>)
/// </summary>
public partial class PhaseHeader : UserControl
{
    public PhaseHeader()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
