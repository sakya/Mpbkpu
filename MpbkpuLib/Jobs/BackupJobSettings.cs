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
    /// The backup destinations: the files are split among them and every one holds a copy of the database
    /// </summary>
    public List<BackupDestination> Destinations { get; set; } = new();

    /// <summary>
    /// Maximum number of files copied at the same time
    /// </summary>
    public int ParallelFiles { get; set; } = 1;

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

    #region legacy settings
    // Settings saved before multiple destinations (in the backup databases) had a single destination:
    // these write-only properties read it into Destinations and are never serialized

    [JsonProperty("Destination")]
    private string? LegacyDestination
    {
        set
        {
            if (value != null)
                LegacyDestinationEntry.Path = value;
        }
    }

    [JsonProperty("DestinationUser")]
    private string? LegacyDestinationUser
    {
        set
        {
            if (value != null)
                LegacyDestinationEntry.User = value;
        }
    }

    [JsonProperty("DestinationOauthSettings")]
    private OAuth2Settings? LegacyDestinationOauthSettings
    {
        set
        {
            if (value != null)
                LegacyDestinationEntry.OauthSettings = value;
        }
    }

    private BackupDestination LegacyDestinationEntry
    {
        get
        {
            if (Destinations.Count == 0)
                Destinations.Add(new BackupDestination());
            return Destinations[0];
        }
    }
    #endregion
}
