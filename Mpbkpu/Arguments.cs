using System.IO.Compression;
using System.Text.RegularExpressions;
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
    public string? SourceOauthServerUrl { get; set; }
    [Option("sourceOauthRedirect",
        Description = "The OAuth redirect url for accessing the source file system",
        OnlyForVerbs = "Backup;SyncDirectory")]
    public string? SourceOauthRedirectUrl { get; set; }
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
                      "(" + IndexedValueHelp + " for the credential options)",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationPaths { get; set; }

    [Option("destinationUser",
        Description = "The user for accessing the destination file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationUser { get; set; }
    [Option("destinationPassword",
        Description = "The password for accessing the destination file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationPassword { get; set; }
    [Option("destinationOauthServer",
        Description = "The OAuth server url for accessing the destination file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationOauthServerUrl { get; set; }
    [Option("destinationOauthRedirect",
        Description = "The OAuth redirect url for accessing the destination file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationOauthRedirectUrl { get; set; }
    [Option("destinationOauthApp",
        Description = "The OAuth app name for accessing the destination file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationOauthAppName { get; set; }
    [Option("destinationOauthAppId",
        Description = "The OAuth app id for accessing the destination file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationOauthAppId { get; set; }
    [Option("destinationOauthSecret",
        Description = "The OAuth app secret for accessing the destination file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Backup;Restore;SyncDirectory")]
    public List<string>? DestinationOauthAppSecret { get; set; }

    /// <summary>
    /// The destinations of a backup
    /// </summary>
    public List<BackupDestination> GetDestinations()
    {
        return GetDestinations(DestinationPaths, DestinationUser, DestinationPassword, DestinationOauthServerUrl,
            DestinationOauthRedirectUrl, DestinationOauthAppName, DestinationOauthAppId, DestinationOauthAppSecret);
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
        try {
            // Too many credential values and invalid urls
            GetDestinations();
            GetBackupDestinations();
            GetSourceOauthSettings();
        } catch (ArgumentException ex) {
            return ex.Message;
        }
        return null;
    }
    #endregion

    #region backup
    [Option("backup", ShortName = 'b',
        Required = true,
        Description = "The path containing the backup. Repeat the option for a backup split among more destinations " +
                      "(" + IndexedValueHelp + " for the credential options)",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupPaths { get; set; }

    [Option("backupUser",
        Description = "The user for accessing the backup file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupUser { get; set; }
    [Option("backupPassword",
        Description = "The password for accessing the backup file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupPassword { get; set; }
    [Option("backupOauthServer",
        Description = "The OAuth server url for accessing the backup file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupOauthServerUrl { get; set; }
    [Option("backupOauthRedirect",
        Description = "The OAuth redirect url for accessing the backup file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupOauthRedirectUrl { get; set; }
    [Option("backupOauthApp",
        Description = "The OAuth app name for accessing the backup file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupOauthAppName { get; set; }
    [Option("backupOauthAppId",
        Description = "The OAuth app id for accessing the backup file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupOauthAppId { get; set; }
    [Option("backupOauthSecret",
        Description = "The OAuth app secret for accessing the backup file system (" + IndexedValueHelp + ")",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public List<string>? BackupOauthAppSecret { get; set; }

    /// <summary>
    /// The destinations holding the backups
    /// </summary>
    public List<BackupDestination> GetBackupDestinations()
    {
        return GetDestinations(BackupPaths, BackupUser, BackupPassword, BackupOauthServerUrl, BackupOauthRedirectUrl,
            BackupOauthAppName, BackupOauthAppId, BackupOauthAppSecret);
    }
    #endregion

    /// <summary>
    /// Credential options of more paths: "N=value" belongs to the N-th path (1 based), a value without index to the
    /// first one. Indexes keep the values distinct (the parser drops repeated values of an option).
    /// </summary>
    private const string IndexedValueHelp = "repeat it as N=value for the N-th path, a value without N= is for the first one";

    public OAuth2Settings GetSourceOauthSettings()
    {
        return new OAuth2Settings()
        {
            ServerUrl = ToUri(SourceOauthServerUrl, "sourceOauthServer"),
            RedirectUrl = ToUri(SourceOauthRedirectUrl, "sourceOauthRedirect"),
            AppName = SourceOauthAppName,
            AppId = SourceOauthAppId,
            AppSecret = SourceOauthAppSecret,
        };
    }

    /// <summary>
    /// Destinations from repeated path options, with their credentials (see <see cref="IndexedValueHelp"/>).
    /// The ids are not persisted by the CLI: the jobs match the destinations recorded in the database by path.
    /// </summary>
    private static List<BackupDestination> GetDestinations(List<string>? paths, List<string>? users, List<string>? passwords,
        List<string>? oauthServers, List<string>? oauthRedirects, List<string>? oauthApps, List<string>? oauthAppIds,
        List<string>? oauthSecrets)
    {
        paths ??= new List<string>();
        var count = Math.Max(paths.Count, 1);
        var user = ByPath(users, count);
        var password = ByPath(passwords, count);
        var oauthServer = ByPath(oauthServers, count);
        var oauthRedirect = ByPath(oauthRedirects, count);
        var oauthApp = ByPath(oauthApps, count);
        var oauthAppId = ByPath(oauthAppIds, count);
        var oauthSecret = ByPath(oauthSecrets, count);

        return paths.Select((path, i) => new BackupDestination()
        {
            Path = path,
            User = user[i],
            Password = password[i],
            OauthSettings = new OAuth2Settings()
            {
                ServerUrl = ToUri(oauthServer[i], "OAuth server"),
                RedirectUrl = ToUri(oauthRedirect[i], "OAuth redirect"),
                AppName = oauthApp[i],
                AppId = oauthAppId[i],
                AppSecret = oauthSecret[i],
            }
        }).ToList();
    }

    /// <summary>
    /// The values of a credential option by path (see <see cref="IndexedValueHelp"/>)
    /// </summary>
    private static string?[] ByPath(List<string>? values, int pathsCount)
    {
        var res = new string?[pathsCount];
        foreach (var value in values ?? new List<string>()) {
            var m = Regex.Match(value, "^([0-9]+)=(.*)$", RegexOptions.Singleline);
            var index = m.Success ? int.Parse(m.Groups[1].Value) - 1 : 0;
            if (index < 0 || index >= pathsCount)
                throw new ArgumentException($"Credential value for path {index + 1}, but there are {pathsCount} paths");
            if (res[index] != null)
                throw new ArgumentException($"More credential values for path {index + 1}: use N=value for the N-th path");
            res[index] = m.Success ? m.Groups[2].Value : value;
        }
        return res;
    }

    private static Uri? ToUri(string? value, string name)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : throw new ArgumentException($"Invalid {name} url: {value}");
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