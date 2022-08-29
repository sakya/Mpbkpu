using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MpbkpuGUI.Controls;

public partial class Spinner : UserControl
{
    public Spinner()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        var iv = this.GetObservable(UserControl.IsVisibleProperty);
        iv.Subscribe(value =>
        {
            var stack = this.FindControl<StackPanel>("WaitSpinner");
            if (value)
                stack.Classes.Add("spinner");
            else
                stack.Classes.Remove("spinner");
        });
    }
}