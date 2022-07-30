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
        Description = "The source path to backup")]
    public string? Source { get; set; }

    [Option("destination", ShortName = 'd',
        Required = true,
        Description = "The destination path to backup")]
    public string? Destination { get; set; }

    [Option("password", ShortName = 'p',
        Description = "Password to encrypt/decrypt the backup")]
    public string? Password { get; set; }

    [Option("compress", ShortName = 'c',
        Description = "If set the files are compressed during backup")]
    public bool Compress { get; set; }
}