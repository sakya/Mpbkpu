using CmdLineArgsParser;
using CmdLineArgsParser.Attributes;
using Microsoft.Graph;
using MpbkpuLib.Abstracts;
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

    [Option("destination", ShortName = 'd',
        Required = true,
        Description = "The destination path of a backup",
        OnlyForVerbs = "Backup")]
    public string? DestinationPath { get; set; }

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
        Description = "Ask for password")]
    public bool? InteractivePassword { get; set; }

    [Option("compress", ShortName = 'c',
        Description = "If set the files are compressed during backup",
        OnlyForVerbs = "Backup")]
    public bool Compress { get; set; }

    [Option("backup", ShortName = 'b',
        Required = true,
        Description = "The path containing the backup",
        OnlyForVerbs = "Restore;Delete;List;Verify")]
    public string? BackupPath { get; set; }

    [Option("restore", ShortName = 'r',
        Required = true,
        Description = "The path to restore a backup",
        OnlyForVerbs = "Restore")]
    public string? Restore { get; set; }

    [Option("guid", ShortName = 'g',
        Required = true,
        Description = "The backup GUID",
        OnlyForVerbs = "Delete;Restore;Verify")]
    public string? BackupGuid { get; set; }

    [Option("loglevel", 'l',
        Description = "The log level")]
    public BaseSettings.LogLevels LogLevel { get; set; } = BaseSettings.LogLevels.Warning;

    [Option("log",
        Description = "Path of the logfile")]
    public string? LogFile { get; set; }

    [Option("type", 't',
        Required = true,
        OnlyForVerbs = "Backup",
        Description = "The type of backup to execute")]
    public BackupJobSettings.BackupTypes? BackupType { get; set; }

    [Option("yes", 'y',
        Description = "Auto reply YES to any question")]
    public bool? AlwaysYes { get; set; }
}