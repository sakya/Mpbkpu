using System.IO.Compression;
using CmdLineArgsParser;
using CmdLineArgsParser.Attributes;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;

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
        Verify
    }

    [Option("action", Verb = true, Required = true)]
    public Actions? Action { get; set; }

    #region source
    [Option("source", ShortName = 's',
        Required = true,
        Description = "The source path to backup",
        OnlyForVerbs = "Backup")]
    public string? SourcePath { get; set; }

    [Option("sourceUser",
        Description = "The user for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public string? SourceUser { get; set; }
    [Option("sourcePassword",
        Description = "The password for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public string? SourcePassword { get; set; }

    [Option("sourceOauthServer",
        Description = "The OAuth server url for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public Uri? SourceOauthServerUrl { get; set; }
    [Option("sourceOauthRedirect",
        Description = "The OAuth redirect url for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public Uri? SourceOauthRedirectUrl { get; set; }
    [Option("sourceOauthApp",
        Description = "The OAuth app name for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public string? SourceOauthAppName { get; set; }
    [Option("sourceOauthAppId",
        Description = "The OAuth app id for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public string? SourceOauthAppId { get; set; }
    [Option("sourceOauthSecret",
        Description = "The OAuth app secret for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public string? SourceOauthAppSecret { get; set; }
    #endregion

    #region destination
    [Option("destination", ShortName = 'd',
        Required = true,
        Description = "The destination path of a backup",
        OnlyForVerbs = "Backup;Restore")]
    public string? DestinationPath { get; set; }

    [Option("destinationUser",
        Description = "The user for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore")]
    public string? DestinationUser { get; set; }
    [Option("destinationPassword",
        Description = "The password for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore")]

    public string? DestinationPassword { get; set; }
    [Option("destinationOauthServer",
        Description = "The OAuth server url for accessing the destination file system",
        OnlyForVerbs = "Backup")]
    public Uri? DestinationOauthServerUrl { get; set; }
    [Option("destinationOauthRedirect",
        Description = "The OAuth redirect url for accessing the destination file system",
        OnlyForVerbs = "Backup")]
    public Uri? DestinationOauthRedirectUrl { get; set; }
    [Option("destinationOauthApp",
        Description = "The OAuth app name for accessing the destination file system",
        OnlyForVerbs = "Backup")]
    public string? DestinationOauthAppName { get; set; }
    [Option("destinationOauthAppId",
        Description = "The OAuth app id for accessing the destination file system",
        OnlyForVerbs = "Backup")]
    public string? DestinationOauthAppId { get; set; }
    [Option("destinationOauthSecret",
        Description = "The OAuth app secret for accessing the destination file system",
        OnlyForVerbs = "Backup")]
    public string? DestinationOauthAppSecret { get; set; }
    #endregion

    #region backup
    [Option("backup", ShortName = 'b',
        Required = true,
        Description = "The path containing the backup",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupPath { get; set; }

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
        OnlyForVerbs = "Backup")]
    public Uri? BackupOauthServerUrl { get; set; }
    [Option("backupOauthRedirect",
        Description = "The OAuth redirect url for accessing the backup file system",
        OnlyForVerbs = "Backup")]
    public Uri? BackupOauthRedirectUrl { get; set; }
    [Option("backupOauthApp",
        Description = "The OAuth app name for accessing the backup file system",
        OnlyForVerbs = "Backup")]
    public string? BackupOauthAppName { get; set; }
    [Option("backupOauthAppId",
        Description = "The OAuth app id for accessing the backup file system",
        OnlyForVerbs = "Backup")]
    public string? BackupOauthAppId { get; set; }
    [Option("backupOauthSecret",
        Description = "The OAuth app secret for accessing the backup file system",
        OnlyForVerbs = "Backup")]
    public string? BackupOauthAppSecret { get; set; }
    #endregion

    [Option("exclude", 'e',
        Description = "File or directory to exclude from the backup",
        OnlyForVerbs = "Backup;Restore")]
    public List<string>? Exclude { get; set; }

    [Option("password", ShortName = 'p',
        MutuallyExclusive = "password",
        Description = "Password to encrypt/decrypt a backup")]
    public string? Password { get; set; }
    [Option("interactive", ShortName = 'i',
        MutuallyExclusive = "password",
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
        OnlyForVerbs = "Backup;Restore",
        DefaultValue = "3")]
    public int? RetryOnError { get; set; }

    [Option("yes", 'y',
        Description = "Auto reply YES to any question")]
    public bool AlwaysYes { get; set; }

    [Option("skipHash",
        Description = "Skip SHA256 hash check on existing file in the destination directory",
        OnlyForVerbs = "Backup;Restore")]
    public bool SkipHashCheck { get; set; }

    [Option("filter",
        Description = "Regular expression used to filter results",
        OnlyForVerbs = "ListContent")]
    public string? Filter { get; set; }
}