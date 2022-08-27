using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupJob : Job
{
    public Backup.BackupTypes? Type { get; set; }
}