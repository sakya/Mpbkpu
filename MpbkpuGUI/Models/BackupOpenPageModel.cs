using System.Collections.ObjectModel;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupOpenPageModel : BaseModel
{
    public class BackupData
    {
        public Backup? Backup { get; set; }
        public long Items { get; set; }
        public long Size { get; set; }
    }

    private string? _spinnerMessage;
    public string? SpinnerMessage
    {
        get => _spinnerMessage;
        set => SetField(ref _spinnerMessage, value);
    }

    private ObservableCollection<BackupData>? _backups;
    public ObservableCollection<BackupData>? Backups
    {
        get => _backups;
        set => SetField(ref _backups, value);
    }
}