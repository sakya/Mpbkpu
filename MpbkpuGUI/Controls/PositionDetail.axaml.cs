using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MpbkpuGUI.Controls;

public partial class PositionDetail : UserControl
{
    public PositionDetail()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}