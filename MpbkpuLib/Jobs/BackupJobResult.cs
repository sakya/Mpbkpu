using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class BackupJobJobResult : BaseJobResult
{
    /// <summary>
    /// The GUID of the backup
    /// </summary>
    public string? Guid { get; set; }
}