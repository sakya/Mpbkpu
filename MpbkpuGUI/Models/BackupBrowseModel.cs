using System.Collections.Generic;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupBrowseModel : BaseModel
{
    private string? _buttonText;
    public string? ButtonText
    {
        get => _buttonText;
        set => SetField(ref _buttonText, value);
    }

    private string? _spinnerMessage;
    public string? SpinnerMessage
    {
        get => _spinnerMessage;
        set => SetField(ref _spinnerMessage, value);
    }

    private List<MpbkpuLib.BackupFile.Directory>? _directories;
    public List<MpbkpuLib.BackupFile.Directory>? Directories
    {
        get => _directories;
        set => SetField(ref _directories, value);
    }

    private List<BackupItem>? _files;
    public List<BackupItem>? Files
    {
        get => _files;
        set => SetField(ref _files, value);
    }
}