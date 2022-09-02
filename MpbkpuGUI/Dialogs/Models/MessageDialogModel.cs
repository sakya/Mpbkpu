using Avalonia.Media;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Dialogs.Models;

public class MessageDialogModel : BaseModel
{
    public class ButtonData : BaseModel
    {
        public ButtonData()
        {
            IsVisible = true;
        }

        private string? _text;
        public string? Text
        {
            get => _text;
            set => SetField(ref _text, value);
        }

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetField(ref _isVisible, value);
        }

        private bool _isDefault;
        public bool IsDefault
        {
            get => _isDefault;
            set => SetField(ref _isDefault, value);
        }

        private bool _isCancel;
        public bool IsCancel
        {
            get => _isCancel;
            set => SetField(ref _isCancel, value);
        }
    }

    public MessageDialogModel()
    {
        IconVisible = true;
        _button1Data = new ButtonData();
        _button2Data = new ButtonData();
    }

    private string? _message;
    public string? Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    private bool _iconVisible;
    public bool IconVisible
    {
        get => _iconVisible;
        set => SetField(ref _iconVisible, value);
    }

    private string? _icon;
    public string? Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    private SolidColorBrush? _iconBrush;
    public SolidColorBrush? IconBrush
    {
        get => _iconBrush;
        set => SetField(ref _iconBrush, value);
    }

    private ButtonData _button1Data;
    public ButtonData Button1Data
    {
        get => _button1Data;
        set => SetField(ref _button1Data, value);
    }

    private ButtonData _button2Data;
    public ButtonData Button2Data
    {
        get => _button2Data;
        set => SetField(ref _button2Data, value);
    }
}