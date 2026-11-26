using Newtonsoft.Json;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

public class RestoreJobSettings : BackupDestinationsJobSettings
{
    /// <summary>
    /// The GUID of the backup to restore
    /// </summary>
    public string? BackupGuid { get; set; }

    /// <summary>
    /// The restore destination
    /// </summary>
    /// <value></value>
    public string? Destination { get; set; }
    /// <summary>
    /// The user to access to the destination
    /// </summary>
    /// <value></value>
    public string? DestinationUser { get; set; }
    /// <summary>
    /// The password to access to the destination
    /// </summary>
    /// <value></value>
    [JsonIgnore] // Never logged or saved in the backup database
    public string? DestinationPassword { get; set; }
    /// <summary>
    /// OAuth settings to access to the destination
    /// </summary>
    public OAuth2Settings? DestinationOauthSettings { get; set; }

    /// <summary>
    /// Number of retries on error while restoring a file
    /// </summary>
    public int RetryOnError { get; set; } = 3;

    /// <summary>
    /// Skip hash check on existing file in the destination directory
    /// </summary>
    public bool SkipHashCheck { get; set; } = false;

    /// <summary>
    /// Restore only this item of the backup (its original relative path; a directory with its content, "" for the
    /// backed up folder) into <see cref="Destination"/>, keeping its name but not its parent directories. Existing
    /// files are overwritten and nothing else in <see cref="Destination"/> is deleted. Null restores the whole backup
    /// and makes <see cref="Destination"/> identical to it.
    /// </summary>
    public string? RelativePath { get; set; }
}