using System;
using Avalonia;
using Avalonia.Reactive;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Controls.Models;

namespace MpbkpuGUI.Controls;

public partial class Spinner : UserControl
{
    public static readonly DirectProperty<Spinner, string?> MessageProperty =
        AvaloniaProperty.RegisterDirect<Spinner, string?>(
            nameof(Message),
            o => o.Message,
            (o, v) => o.Message = v);

    private readonly SpinnerModel _model = new();
    private string? _message;

    public Spinner()
    {
        DataContext = _model;
        InitializeComponent();
    }

    public string? Message
    {
        get => _message;
        set
        {
            if (SetAndRaise(MessageProperty, ref _message, value)) {
                _model.Message = value;
            }
        }
    }

    /// <summary>
    /// Raised by the cancel button, shown only while a handler is attached
    /// </summary>
    public event EventHandler? CancelRequested
    {
        add
        {
            _cancelRequested += value;
            UpdateCancelButton();
        }
        remove
        {
            _cancelRequested -= value;
            UpdateCancelButton();
        }
    }
    private EventHandler? _cancelRequested;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        var iv = this.GetObservable(UserControl.IsVisibleProperty);
        iv.Subscribe(new AnonymousObserver<bool>(value =>
        {
            var stack = this.FindControl<StackPanel>("WaitSpinner")!;
            if (value)
                stack.Classes.Add("spinner");
            else {
                stack.Classes.Remove("spinner");
                // The next use starts without the progress of this one
                ShowProgress(null, null);
            }
        }));
    }

    /// <summary>
    /// Show the progress bar and a detail line under the message; null hides them
    /// </summary>
    public void ShowProgress(double? percentage, string? detail)
    {
        var bar = this.FindControl<ProgressBar>("Progress")!;
        bar.IsVisible = percentage != null;
        bar.Value = percentage ?? 0;
        var text = this.FindControl<TextBlock>("Detail")!;
        text.IsVisible = !string.IsNullOrEmpty(detail);
        text.Text = detail;
    }

    private void UpdateCancelButton()
    {
        this.FindControl<Button>("CancelButton")!.IsVisible = _cancelRequested != null;
    }

    /// <summary>
    /// True while a cancellable operation is shown
    /// </summary>
    public bool CanCancel => _cancelRequested != null;

    /// <summary>
    /// Ask to cancel the operation, as the cancel button does (the handler decides, e.g. after a confirmation)
    /// </summary>
    public void RequestCancel()
    {
        _cancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        RequestCancel();
    }
}