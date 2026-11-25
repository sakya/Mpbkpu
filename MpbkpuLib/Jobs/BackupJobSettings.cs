using Newtonsoft.Json;
using System.IO.Compression;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;
public class BackupJobSettings : BaseJobSettings
{
    public BackupJobSettings()
    {
        MaxFilesPerDirectory = 1000;
        RetryOnError = 3;
    }

    /// <summary>
    /// The job name
    /// </summary>
    /// <value></value>
    public string? Name { get; set; }

    /// <summary>
    /// The backup <see cref="Backup.BackupTypes"/>
    /// </summary>
    public Backup.BackupTypes BackupType { get; set; }

    /// <summary>
    /// The backup source
    /// </summary>
    /// <value></value>
    public string? Source { get; set; }
    /// <summary>
    /// The user to access to the source
    /// </summary>
    /// <value></value>
    public string? SourceUser { get; set; }
    /// <summary>
    /// The password to access to the source
    /// </summary>
    /// <value></value>
    [JsonIgnore] // Never logged or saved in the backup database
    public string? SourcePassword { get; set; }
    /// <summary>
    /// OAuth settings to access to the source
    /// </summary>
    public OAuth2Settings? SourceOauthSettings { get; set; }

    /// <summary>
    /// The backup destination
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
    /// A list of files/directories to exclude from the backup
    /// </summary>
    public List<string>? Exclude { get; set; }

    /// <summary>
    /// Maximum number of files to keep in each directory
    /// </summary>
    public int MaxFilesPerDirectory { get; set; }

    /// <summary>
    /// If set to true also hidden files are included in the backup
    /// </summary>
    public bool IncludeHiddenFiles { get; set; }

    /// <summary>
    /// The compression level used during backup
    /// </summary>
    /// <value></value>
    public CompressionLevel CompressionLevel { get; set; }

    /// <summary>
    /// Number of retries on error while backing up a file
    /// </summary>
    public int RetryOnError { get; set; }

    /// <summary>
    /// Skip hash check on existing file in the destination directory
    /// </summary>
    public bool SkipHashCheck { get; set; } = false;

    /// <summary>
    /// Number of days after which an incremental or differential backup starts a new chain with a full backup (0 = never)
    /// </summary>
    public int FullBackupEveryDays { get; set; }

    /// <summary>
    /// Number of chains (a full backup and its incremental or differential backups) to keep (0 = keep all).
    /// Older chains are deleted after a full backup completes.
    /// </summary>
    public int KeepChains { get; set; }
}
