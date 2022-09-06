using System.Collections.Generic;
using System.Collections.ObjectModel;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupBrowseModel : BaseModel
{
    public class Directory
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
        public ObservableCollection<Directory>? Children { get; set; }
    }

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

    private ObservableCollection<Directory>? _directories;
    public ObservableCollection<Directory>? Directories
    {
        get => _directories;
        set => SetField(ref _directories, value);
    }

    private ObservableCollection<BackupItem>? _files;
    public ObservableCollection<BackupItem>? Files
    {
        get => _files;
        set => SetField(ref _files, value);
    }
}