using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Models;

public class BackupBrowseModel : BaseModel
{
    private string? _buttonText;
    public string? ButtonText
    {
        get => _buttonText;
        set => SetField(ref _buttonText, value);
    }
}