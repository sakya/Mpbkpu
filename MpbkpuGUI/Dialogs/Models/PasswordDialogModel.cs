using System.ComponentModel.DataAnnotations;
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
}