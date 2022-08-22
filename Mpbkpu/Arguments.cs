using System.IO.Compression;
using CmdLineArgsParser;
using CmdLineArgsParser.Attributes;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;

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

    [Option("source", ShortName = 's',
        Required = true,
        Description = "The source path to backup",
        OnlyForVerbs = "Backup")]
    public string? SourcePath { get; set; }

    [Option("sourceuser",
        Description = "The user for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public string? SourceUser { get; set; }
    [Option("sourcepassword",
        Description = "The password for accessing the source file system",
        OnlyForVerbs = "Backup")]
    public string? SourcePassword { get; set; }

    [Option("destination", ShortName = 'd',
        Required = true,
        Description = "The destination path of a backup",
        OnlyForVerbs = "Backup;Restore")]
    public string? DestinationPath { get; set; }

    [Option("destinationuser",
        Description = "The user for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore")]
    public string? DestinationUser { get; set; }
    [Option("destinationpassword",
        Description = "The password for accessing the destination file system",
        OnlyForVerbs = "Backup;Restore")]
    public string? DestinationPassword { get; set; }

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

    [Option("compressionlevel", 'c',
        Description = "Set the file compression level",
        OnlyForVerbs = "Backup",
        DefaultValue = "NoCompression")]
    public CompressionLevel CompressionLevel { get; set; }

    [Option("backup", ShortName = 'b',
        Required = true,
        Description = "The path containing the backup",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupPath { get; set; }

    [Option("backupuser",
        Description = "The user for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupUser { get; set; }
    [Option("backuppassword",
        Description = "The password for accessing the backup file system",
        OnlyForVerbs = "Restore;Delete;List;ListContent;Verify")]
    public string? BackupPassword { get; set; }

    [Option("guid", ShortName = 'g',
        Required = true,
        Description = "The backup GUID",
        OnlyForVerbs = "Delete;Restore;ListContent;Verify")]
    public string? BackupGuid { get; set; }

    [Option("loglevel", 'l',
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

    [Option("skiphash",
        Description = "Skip SHA256 hash check on existing file in the destination directory",
        OnlyForVerbs = "Backup;Restore")]
    public bool SkipHashCheck { get; set; }
}