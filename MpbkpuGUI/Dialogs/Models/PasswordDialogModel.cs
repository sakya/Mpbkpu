using System.Collections.Generic;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Dialogs.Models;

public class PasswordDialogModel : BaseModel
{
    private string? _password;
    public string? Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    private List<Controls.ErrorContainer.ErrorData>? _errors;
    public List<Controls.ErrorContainer.ErrorData>? Errors
    {
        get => _errors;
        set => SetField(ref _errors, value);
    }
}