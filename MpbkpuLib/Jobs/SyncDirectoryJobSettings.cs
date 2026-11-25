using Newtonsoft.Json;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

public class SyncDirectoryJobSettings : BaseJobSettings
{
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
    /// Number of retries on error while backing up a file
    /// </summary>
    public int RetryOnError { get; set; } = 3;

    /// <summary>
    /// Skip hash check on existing file in the destination directory
    /// </summary>
    public bool SkipHashCheck { get; set; } = false;
    /// <summary>
    /// If set to true also hidden files are included in the backup
    /// </summary>
    public bool IncludeHiddenFiles { get; set; }
}