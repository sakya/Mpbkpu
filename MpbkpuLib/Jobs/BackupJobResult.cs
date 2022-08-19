using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class BackupJobJobResult : BaseJobResult
{
    public string? Guid { get; set; }
}