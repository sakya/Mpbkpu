using Newtonsoft.Json;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

public class VerifyJobSettings : BackupDestinationsJobSettings
{
    /// <summary>
    /// The GUID of the backup to verify
    /// </summary>
    public string? BackupGuid { get; set; }
}