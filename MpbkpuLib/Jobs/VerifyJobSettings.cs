using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class VerifyJobSettings : BaseSettings
{
    /// <summary>
    /// The path containing the backup
    /// </summary>
    public string? BackupPath { get; set; }
    /// <summary>
    /// The GUID of the backup to verify
    /// </summary>
    public string? BackupGuid { get; set; }
}