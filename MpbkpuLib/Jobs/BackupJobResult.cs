using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuLib.Jobs;

public class BackupJobJobResult : BaseJobResult
{
    /// <summary>
    /// The GUID of the backup
    /// </summary>
    public string? Guid { get; set; }

    /// <summary>
    /// The result saved for the backup (null when there was nothing to back up)
    /// </summary>
    public Backup.Results? Result { get; set; }
}