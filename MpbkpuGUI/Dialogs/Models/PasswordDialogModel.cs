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

    private bool _remember;
    public bool Remember
    {
        get => _remember;
        set => SetField(ref _remember, value);
    }

    private bool _canRemember;
    public bool CanRemember
    {
        get => _canRemember;
        set => SetField(ref _canRemember, value);
    }

    private List<Controls.ErrorContainer.ErrorData>? _errors;
    public List<Controls.ErrorContainer.ErrorData>? Errors
    {
        get => _errors;
        set => SetField(ref _errors, value);
    }
}