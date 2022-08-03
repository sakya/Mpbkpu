using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class VerifyJobSettings : BaseSettings
{
    /// <summary>
    /// The path containing the backup
    /// </summary>
    public string? BackupPath { get; set; }
}