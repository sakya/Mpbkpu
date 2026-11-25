using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MpbkpuGUI.Controls;

public partial class ErrorContainer : UserControl
{
    public partial class ErrorData
    {
        public ErrorData(string fieldName, string message)
        {
            FieldName = fieldName;
            Message = message;
        }

        public string? FieldName { get; set; }
        public string? Message { get; set; }
    }

    public static readonly DirectProperty<ErrorContainer, Control?> ControlProperty =
        AvaloniaProperty.RegisterDirect<ErrorContainer, Control?>(
            nameof(Control),
            o => o.Control,
            (o, v) => o.Control = v);

    public static readonly DirectProperty<ErrorContainer, string?> FieldNameProperty =
        AvaloniaProperty.RegisterDirect<ErrorContainer, string?>(
            nameof(FieldName),
            o => o.FieldName,
            (o, v) => o.FieldName = v);

    public static readonly DirectProperty<ErrorContainer, string?> ErrorProperty =
        AvaloniaProperty.RegisterDirect<ErrorContainer, string?>(
            nameof(Error),
            o => o.Error,
            (o, v) => o.Error = v);

    public static readonly DirectProperty<ErrorContainer, List<ErrorData>?> ErrorsProperty =
        AvaloniaProperty.RegisterDirect<ErrorContainer, List<ErrorData>?>(
            nameof(Errors),
            o => o.Errors,
            (o, v) => o.Errors = v);

    public ErrorContainer()
    {
        DataContext = this;
        InitializeComponent();
    }

    private Control? _control;
    public Control? Control
    {
        get => _control;
        set
        {
            if (SetAndRaise(ControlProperty, ref _control, value)) {

            }
        }
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        set
        {
            if (SetAndRaise(ErrorProperty, ref _error, value)) {
                if (!string.IsNullOrEmpty(value))
                    _control?.Classes.Add("Error");
                else
                    _control?.Classes.Remove("Error");
            }
        }
    }

    private string? _fieldName;
    public string? FieldName
    {
        get => _fieldName;
        set
        {
            if (SetAndRaise(FieldNameProperty, ref _fieldName, value)) {

            }
        }
    }

    private List<ErrorData>? _errors;
    public List<ErrorData>? Errors
    {
        get => _errors;
        set
        {
            if (SetAndRaise(ErrorsProperty, ref _errors, value)) {
                Error = null;
                var err = value?
                    .FirstOrDefault(e => e.FieldName == FieldName);
                if (err != null)
                    Error = err.Message;

            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}