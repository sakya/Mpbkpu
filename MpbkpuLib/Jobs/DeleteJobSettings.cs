using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class DeleteJobSettings : BaseSettings
{
    /// <summary>
    /// The path containing the backup
    /// </summary>
    public string? BackupPath { get; set; }

    /// <summary>
    /// The GUID of the backup to delete
    /// </summary>
    public string? BackupGuid { get; set; }
}