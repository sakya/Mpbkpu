using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;
public class BackupJobSettings : BaseSettings
{
    /// <summary>
    /// The backup type
    /// https://phoenixnap.com/kb/full-vs-incremental-vs-differential-backup
    /// </summary>
    public enum BackupTypes
    {
        /// <summary>
        /// Backup all the files
        /// </summary>
        Full,
        /// <summary>
        /// Backup only changed files since the last full/incremental backup
        /// </summary>
        Incremental,
        /// <summary>
        /// Backup only changed files since the last full backup
        /// </summary>
        Differential
    }

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
    public BackupTypes BackupType { get; set; }
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
    public string? SourcePassword { get; set; }

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
    public string? DestinationPassword { get; set; }

    public List<string>? Exclude { get; set; }
    public int MaxFilesPerDirectory { get; set; }
    public bool IncludeHiddenFiles { get; set; }

    /// <summary>
    /// If set to true files are compressed during backup
    /// </summary>
    /// <value></value>
    public bool Compress { get; set; }

    /// <summary>
    /// Number of retries on error while backing up a file (Default: 3)
    /// </summary>
    public int RetryOnError { get; set; }
}
