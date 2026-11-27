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
    private readonly int _minLength;

    // The XAML runtime loader needs a constructor without parameters
    public PasswordDialog() : this(false)
    {
    }

    /// <param name="canRemember">Show the option to remember the password</param>
    /// <param name="label">The text above the password box, "Password" when null</param>
    /// <param name="minLength">The minimum length of the password</param>
    public PasswordDialog(bool canRemember, string? label = null, int minLength = 8)
    {
        _minLength = minLength;
        _model = new PasswordDialogModel()
        {
            CanRemember = canRemember,
            Label = label ?? Localizer.Localizer.Instance["Password"]
        };
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
        else if (_model.Password.Length < _minLength)
            errors.Add(new ErrorContainer.ErrorData("Password", string.Format(Localizer.Localizer.Instance["PasswordMinLengthError"], _minLength)));
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