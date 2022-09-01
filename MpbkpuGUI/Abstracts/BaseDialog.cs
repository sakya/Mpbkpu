using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace MpbkpuGUI.Abstracts;

public abstract class BaseDialog : UserControl, IDisposable
{
    private bool _closed;
    private object? _result;

    public virtual void Dispose()
    {
    }

    public virtual Task Show(Window? owner)
    {
        return Show<object?>(owner);
    }

    public virtual async Task<T?> Show<T>(Window? owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        var container = owner.FindControl<Grid>("Container");
        if (container == null)
            throw new Exception("Container not found");

        _closed = false;
        owner.Closing += OwnerOnClosing;

        // Create the backdrop
        var border = new Border()
        {
            Background = new SolidColorBrush(new Color(255, 0, 0, 0)),
            Opacity = 0.8
        };
        container.Children.Add(border);

        /*VerticalAlignment = VerticalAlignment.Bottom;
        container.Children.Add(this);
        while (!_closed)
            await Task.Delay(100);
        */

        // Get dialog size
        VerticalAlignment = VerticalAlignment.Bottom;
        var size = new Size(container.Bounds.Width, container.Bounds.Height);
        Arrange(new Rect(size));

        // Animate entrance
        IsHitTestVisible = false;

        AvaloniaProperty? property = TranslateTransform.YProperty;
        var from = Bounds.Size.Height;
        var to = container.Bounds.Height - Bounds.Size.Height;
        RenderTransform = new TranslateTransform() {
            Y = from
        };

        var enterAnim = new Animation()
        {
            Duration = TimeSpan.FromMilliseconds(500),
            Easing = new LinearEasing()
        };

        var kf = new KeyFrame()
        {
            Cue = new Cue(0.0)
        };
        kf.Setters.Add(new Setter()
        {
            Property = property,
            Value = from
        });
        enterAnim.Children.Add(kf);

        kf = new KeyFrame()
        {
            Cue = new Cue(1.0)
        };
        kf.Setters.Add(new Setter()
        {
            Property = property,
            Value = to
        });
        enterAnim.Children.Add(kf);

        container.Children.Add(this);
        await enterAnim.RunAsync(this, null);

        IsHitTestVisible = true;
        RenderTransform = new TranslateTransform() {
            Y = to
        };

        while (!_closed)
            await Task.Delay(100);

        container.Children.Remove(this);
        container.Children.Remove(border);
        owner.Closing -= OwnerOnClosing;

        return (T?)_result;
    }

    private void OwnerOnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Close(null);
    }

    public virtual void Close(object? result)
    {
        _result = result;
        _closed = true;
    }
}