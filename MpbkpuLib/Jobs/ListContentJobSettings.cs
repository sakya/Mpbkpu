using Newtonsoft.Json;
using Dropbox.Api.TeamLog;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

public class ListContentJobSettings : BackupDestinationsJobSettings
{
    /// <summary>
    /// The GUID of the backup
    /// </summary>
    public string? BackupGuid { get; set; }

    /// <summary>
    /// Regular expression used to filter results
    /// </summary>
    public string? Filter { get; set; }
}