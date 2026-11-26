using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Controls;
using MpbkpuGUI.Dialogs.Models;

namespace MpbkpuGUI.Dialogs;

public partial class PasswordDialog : BaseDialog
{
    /// <summary>
    /// The password typed and if it must be remembered
    /// </summary>
    public record Result(string Password, bool Remember);

    private readonly PasswordDialogModel _model;

    /// <param name="canRemember">Show the option to remember the password</param>
    // The XAML runtime loader needs a constructor without parameters
    public PasswordDialog() : this(false)
    {
    }

    public PasswordDialog(bool canRemember)
    {
        _model = new PasswordDialogModel() { CanRemember = canRemember };
        DataContext = _model;

        VerticalAlignment = VerticalAlignment.Bottom;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        KeyDown += OnKeyDown;
    }

    protected override void Opened()
    {
        this.FindControl<TextBox>("Password")!.Focus();
        base.Opened();
    }

    public override void OnKeyDown(object? sender, KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Enter) {
           OnOkClick(null, new RoutedEventArgs());
        } else if (e.Key == Key.Escape) {
            OnCancelClick(null, new RoutedEventArgs());
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        _model.Errors = null;
        var errors = new List<Controls.ErrorContainer.ErrorData>();
        if (string.IsNullOrEmpty(_model.Password))
            errors.Add(new ErrorContainer.ErrorData("Password", Localizer.Localizer.Instance["RequiredError"]));
        else if (_model.Password.Length < 8)
            errors.Add(new ErrorContainer.ErrorData("Password", Localizer.Localizer.Instance["PasswordMinLengthError"]));
        if (errors.Count > 0) {
            _model.Errors = errors;
            return;
        }

        Close(new Result(_model.Password!, _model.CanRemember && _model.Remember));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}