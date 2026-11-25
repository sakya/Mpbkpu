using System.IO.Compression;
using CmdLineArgsParser;
using CmdLineArgsParser.Attributes;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using MpbkpuLib.FileManagers;

namespace Mpbkpu;

public class Arguments : IOptions
{
    public enum Actions
    {
        [Description("Backup files")]
        Backup,
        [Description("Restore files from a backup")]
        Restore,
        [Description("List backups")]
        List,
        [Description("List the content of a backup")]
        ListContent,
        [Description("Delete a backup")]
        Delete,
        [Description("Verify a backup")]
        Verify,
        [Description("Sync a destination directory to a source directory")]
        SyncDirectory
    }

    [Option("action", Verb = true, Required = true)]
    public Actions? Action { get; set; }

    #region source
    [Option("source", ShortName = 's',
        Required = true,
        Description = "The source path to backup",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public string? SourcePath { get; set; }

    [Option("sourceUser",
        Description = "The user for accessing the source file system",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public string? SourceUser { get; set; }
    [Option("sourcePassword",
        Description = "The password for accessing the source file system",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public string? SourcePassword { get; set; }

    [Option("sourceOauthServer",
        Description = "The OAuth server url for accessing the source file system",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public Uri? SourceOauthServerUrl { get; set; }
    [Option("sourceOauthRedirect",
        Description = "The OAuth redirect url for accessing the source file system",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public Uri? SourceOauthRedirectUrl { get; set; }
    [Option("sourceOauthApp",
        Description = "The OAuth app name for accessing the source file system",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public string? SourceOauthAppName { get; set; }
    [Option("sourceOauthAppId",
        Description = "The OAuth app id for accessing the source file system",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public string? SourceOauthAppId { get; set; }
    [Option("sourceOauthSecret",
        Description = "The OAuth app secret for accessing the source file system",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public string? SourceOauthAppSecret { get; set; }
    #endregion

    #region destination
    [Option("destination", ShortName = 'd',
        Required = true,
        Description = "The destination path. A backup can be split among more destinations: repeat the option " +
                      "(the user, password and OAuth options apply to the first one, the others take the credentials in the path)",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationPaths { get; set; }

    [Option("destinationUser",
        Description = "The user for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public string? DestinationUser { get; set; }
    [Option("destinationPassword",
        Description = "The password for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]

    public string? DestinationPassword { get; set; }
    [Option("destinationOauthServer",
        Description = "The OAuth server url for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public Uri? DestinationOauthServerUrl { get; set; }
    [Option("destinationOauthRedirect",
        Description = "The OAuth redirect url for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public Uri? DestinationOauthRedirectUrl { get; set; }
    [Option("destinationOauthApp",
        Description = "The OAuth app name for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public string? DestinationOauthAppName { get; set; }
    [Option("destinationOauthAppId",
        Description = "The OAuth app id for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public string? DestinationOauthAppId { get; set; }
    [Option("destinationOauthSecret",
        Description = "The OAuth app secret for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public string? DestinationOauthAppSecret { get; set; }

    /// <summary>
    /// The destinations of a backup
    /// </summary>
    public List<BackupDestination> GetDestinations()
    {
        return GetDestinations(DestinationPaths, DestinationUser, DestinationPassword, new OAuth2Settings()
        {
            ServerUrl = DestinationOauthServerUrl,
            RedirectUrl = DestinationOauthRedirectUrl,
            AppName = DestinationOauthAppName,
            AppId = DestinationOauthAppId,
            AppSecret = DestinationOauthAppSecret,
        });
    }

    /// <summary>
    /// The destination of a restore or of a synchronization, which take only one (see <see cref="Validate"/>)
    /// </summary>
    public BackupDestination GetSingleDestination()
    {
        return GetDestinations()[0];
    }

    /// <summary>
    /// Checks the parser cannot do
    /// </summary>
    /// <returns>The error message, null when the arguments are valid</returns>
    public string? Validate()
    {
        if (Action is Actions.Restore or Actions.SyncDirectory && DestinationPaths?.Count > 1)
            return $"{Action} takes one destination";
        return null;
    }
    #endregion

    #region backup
    [Option("backup", ShortName = 'b',
        Required = true,
        Description = "The path containing the backup. Repeat the option for a backup split among more destinations " +
                      "(the user, password and OAuth options apply to the first one, the others take the credentials in the path)",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupPaths { get; set; }

    [Option("backupUser",
        Description = "The user for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupUser { get; set; }
    [Option("backupPassword",
        Description = "The password for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupPassword { get; set; }

    [Option("backupOauthServer",
        Description = "The OAuth server url for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public Uri? BackupOauthServerUrl { get; set; }
    [Option("backupOauthRedirect",
        Description = "The OAuth redirect url for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public Uri? BackupOauthRedirectUrl { get; set; }
    [Option("backupOauthApp",
        Description = "The OAuth app name for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupOauthAppName { get; set; }
    [Option("backupOauthAppId",
        Description = "The OAuth app id for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupOauthAppId { get; set; }
    [Option("backupOauthSecret",
        Description = "The OAuth app secret for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupOauthAppSecret { get; set; }

    /// <summary>
    /// The destinations holding the backups
    /// </summary>
    public List<BackupDestination> GetBackupDestinations()
    {
        return GetDestinations(BackupPaths, BackupUser, BackupPassword, new OAuth2Settings()
        {
            ServerUrl = BackupOauthServerUrl,
            RedirectUrl = BackupOauthRedirectUrl,
            AppName = BackupOauthAppName,
            AppId = BackupOauthAppId,
            AppSecret = BackupOauthAppSecret,
        });
    }
    #endregion

    /// <summary>
    /// Destinations from repeated path options: the user, password and OAuth options apply to the first path.
    /// The ids are not persisted by the CLI: the jobs match the destinations recorded in the database by path.
    /// </summary>
    private static List<BackupDestination> GetDestinations(List<string>? paths, string? user, string? password, OAuth2Settings oauthSettings)
    {
        return (paths ?? new List<string>()).Select((path, i) => i == 0 ?
            new BackupDestination() { Path = path, User = user, Password = password, OauthSettings = oauthSettings } :
            new BackupDestination() { Path = path }).ToList();
    }

    [Option("exclude", 'e',
        Description = "File or directory to exclude from the backup",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? Exclude { get; set; }

    [Option("password", ShortName = 'p',
        MutuallyExclusive = "password",
        OnlyForVerbs = "Backup;Restore;List;ListContent;Delete;Verify",
        Description = "Password to encrypt/decrypt a backup")]
    public string? Password { get; set; }
    [Option("interactive", ShortName = 'i',
        MutuallyExclusive = "password",
        OnlyForVerbs = "Backup;Restore;List;ListContent;Delete;Verify",
        Description = "Ask for encrypt/decrypt password")]
    public bool InteractivePassword { get; set; }

    [Option("compressionLevel", 'c',
        Description = "Set the file compression level",
        OnlyForVerbs = "Backup",
        DefaultValue = "NoCompression")]
    public CompressionLevel CompressionLevel { get; set; }

    [Option("guid", ShortName = 'g',
        Required = true,
        Description = "The backup GUID",
        OnlyForVerbs = "Delete;Restore;ListContent;Verify")]
    public string? BackupGuid { get; set; }

    [Option("logLevel", 'l',
        Description = "The log level")]
    public BaseJobSettings.LogLevels LogLevel { get; set; } = BaseJobSettings.LogLevels.Warning;

    [Option("log",
        Description = "Path of the logfile")]
    public string? LogFile { get; set; }

    [Option("type", 't',
        Required = true,
        OnlyForVerbs = "Backup",
        Description = "The type of backup to execute")]
    public Backup.BackupTypes? BackupType { get; set; }

    [Option("retry",
        Description = "Number of retries in case of error",
        OnlyForVerbs = "Backup;Restore;SyncDirectory",
        DefaultValue = "3")]
    public int? RetryOnError { get; set; }

    [Option("fullEvery",
        Description = "Start a new chain with a full backup when the last full backup is older than the given number of days (0 = never)",
        OnlyForVerbs = "Backup",
        DefaultValue = "0")]
    public int? FullBackupEveryDays { get; set; }

    [Option("parallelFiles",
        Description = "Maximum number of files copied at the same time (each file system can lower it)",
        OnlyForVerbs = "Backup",
        DefaultValue = "1")]
    public int? ParallelFiles { get; set; }

    [Option("keepChains",
        Description = "Number of backup chains (a full backup and its incremental or differential backups) to keep, older chains are deleted (0 = keep all)",
        OnlyForVerbs = "Backup",
        DefaultValue = "0")]
    public int? KeepChains { get; set; }

    [Option("yes", 'y',
        Description = "Auto reply YES to any question")]
    public bool AlwaysYes { get; set; }

    [Option("skipHash",
        Description = "Skip SHA256 hash check on existing file in the destination directory",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public bool SkipHashCheck { get; set; }

    [Option("filter",
        Description = "Regular expression used to filter results",
        OnlyForVerbs = "ListContent")]
    public string? Filter { get; set; }
}