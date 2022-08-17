using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class ListJobSettings : BaseSettings
{
    /// <summary>
    /// The path containing the backup
    /// </summary>
    public string? BackupPath { get; set; }
}