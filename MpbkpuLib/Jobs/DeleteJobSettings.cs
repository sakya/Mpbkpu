using Newtonsoft.Json;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

public class DeleteJobSettings : BackupDestinationsJobSettings
{
    /// <summary>
    /// The GUID of the backup to delete
    /// </summary>
    public string? BackupGuid { get; set; }
}