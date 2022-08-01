using CmdLineArgsParser;
using CmdLineArgsParser.Attributes;

namespace Mpbkpu;

public class Arguments : IOptions
{
    public enum Actions
    {
        [Description("Backup files")]
        Backup,
        [Description("Restore files")]
        Restore,
        [Description("List backups")]
        List,
        [Description("Delete a backup")]
        Delete
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

    [Option("password", ShortName = 'p',
        Description = "Password to encrypt/decrypt a backup/restore/delete/list")]
    public string? Password { get; set; }

    [Option("compress", ShortName = 'c',
        Description = "If set the files are compressed during backup",
        OnlyForVerbs = "Backup")]
    public bool Compress { get; set; }

    [Option("backup", ShortName = 'b',
        Required = true,
        Description = "The path containing the backup",
        OnlyForVerbs = "Restore;Delete;List")]
    public string? BackupPath { get; set; }

    [Option("restore", ShortName = 'r',
        Required = true,
        Description = "The path to restore a backup",
        OnlyForVerbs = "Restore")]
    public string? Restore { get; set; }

    [Option("guid", ShortName = 'g',
        Required = true,
        Description = "The backup GUID",
        OnlyForVerbs = "Delete;Restore")]
    public string? BackupGuid { get; set; }
}