using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

public class RestoreJobSettings : BaseJobSettings
{
    /// <summary>
    /// The path containing the backup
    /// </summary>
    public string? BackupPath { get; set; }
    /// <summary>
    /// The user to access to the backup path
    /// </summary>
    /// <value></value>
    public string? BackupPathUser { get; set; }
    /// <summary>
    /// The password to access to the backup path
    /// </summary>
    /// <value></value>
    public string? BackupPathPassword { get; set; }
    /// <summary>
    /// OAuth settings to access to the backup path
    /// </summary>
    public OAuth2Settings? BackupOauthSettings { get; set; }

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
}