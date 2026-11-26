using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CmdLineArgsParser;
using Mpbkpu;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;
using NUnit.Framework;
using Cli = MpbkpuLib.Utility.CommandLine;

namespace Tests;

/// <summary>
/// The commands built by <see cref="Cli"/> must be read back by the CLI parser as the same job
/// </summary>
public class CommandLine
{
    private const string Placeholder = Cli.PasswordPlaceholder;

    [Test]
    public void BackupCommandIsParsedAsTheSameJob()
    {
        var settings = new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Incremental,
            Source = @"C:\My Documents\it's here",
            SourceUser = "me",
            SourcePassword = "secret1",
            Destinations =
            [
                new BackupDestination()
                {
                    Path = "onedrive://me@live.com/Backups", User = "me@live.com",
                    OauthSettings = new OAuth2Settings()
                    {
                        AppName = "App", AppId = "id", AppSecret = "secret2",
                        ServerUrl = new Uri("https://login.example.com/"), RedirectUrl = new Uri("http://localhost:1234/")
                    }
                },
                new BackupDestination() { Path = "ftp:///host:21/backups", User = "ftpuser", Password = "secret3" },
                new BackupDestination() { Path = "mega:///Backups", User = "a@b.c", Password = "secret4" },
                new BackupDestination() { Path = @"D:\Second" },
            ],
            CompressionLevel = CompressionLevel.Optimal,
            RetryOnError = 5,
            FullBackupEveryDays = 30,
            ParallelFiles = 4,
            KeepChains = 2,
            SkipHashCheck = true,
            EncryptPassword = "secret5",
        };

        var res = Cli.ForBackup(@"C:\Program Files\Mpbkpu\mpbkpu.exe", settings, true);
        Assert.That(res.Command, Does.StartWith("& 'C:\\Program Files\\Mpbkpu\\mpbkpu.exe' backup "));
        foreach (var secret in new[] { "secret1", "secret2", "secret3", "secret4", "secret5" })
            Assert.That(res.Command, Does.Not.Contain(secret));
        Assert.That(res.HasPlaceholders, Is.True);
        Assert.That(res.UnsupportedPaths, Is.Empty);

        var args = Parse(res.Command);
        Assert.That(args.Action, Is.EqualTo(Arguments.Actions.Backup));
        Assert.That(args.BackupType, Is.EqualTo(Backup.BackupTypes.Incremental));
        Assert.That(args.SourcePath, Is.EqualTo(settings.Source));
        Assert.That(args.SourceUser, Is.EqualTo("me"));
        Assert.That(args.SourcePassword, Is.EqualTo(Placeholder));
        Assert.That(args.DestinationPaths, Is.EqualTo(new[]
        {
            "onedrive://me@live.com/Backups",
            $"ftp://ftpuser:{Placeholder}@host:21/backups",
            $"mega://a@b.c:{Placeholder}/Backups",
            @"D:\Second",
        }));
        Assert.That(args.DestinationUser, Is.EqualTo("me@live.com"));
        Assert.That(args.DestinationPassword, Is.Null);
        Assert.That(args.DestinationOauthAppName, Is.EqualTo("App"));
        Assert.That(args.DestinationOauthAppId, Is.EqualTo("id"));
        Assert.That(args.DestinationOauthAppSecret, Is.EqualTo(Placeholder));
        Assert.That(args.DestinationOauthServerUrl, Is.EqualTo(new Uri("https://login.example.com/")));
        Assert.That(args.DestinationOauthRedirectUrl, Is.EqualTo(new Uri("http://localhost:1234/")));
        Assert.That(args.CompressionLevel, Is.EqualTo(CompressionLevel.Optimal));
        Assert.That(args.RetryOnError, Is.EqualTo(5));
        Assert.That(args.FullBackupEveryDays, Is.EqualTo(30));
        Assert.That(args.ParallelFiles, Is.EqualTo(4));
        Assert.That(args.KeepChains, Is.EqualTo(2));
        Assert.That(args.SkipHashCheck, Is.True);
        Assert.That(args.InteractivePassword, Is.True);
        Assert.That(args.Password, Is.Null);
        Assert.That(args.AlwaysYes, Is.True);

        // The credentials written in the paths are the ones the file managers read
        using (var ftp = (Ftp)FileManagerFactory.GetFileManager(args.DestinationPaths![1], out var position)) {
            Assert.That(ftp.Credentials!.UserName, Is.EqualTo("ftpuser"));
            Assert.That(ftp.Host, Is.EqualTo("host"));
            Assert.That(position, Is.EqualTo("/backups"));
        }
        FileManagerFactory.GetFileManager(args.DestinationPaths[2], out var megaPosition).Dispose();
        Assert.That(megaPosition, Is.EqualTo("mega://Backups"));
    }

    [Test]
    public void DefaultsAreNotWritten()
    {
        var res = Cli.ForBackup("mpbkpu", new BackupJobSettings()
        {
            Source = @"C:\Data",
            Destinations = [new BackupDestination() { Path = @"D:\Backup" }],
            // The defaults of the CLI (the library defaults to Optimal compression)
            CompressionLevel = CompressionLevel.NoCompression,
            RetryOnError = 3,
        }, false);

        Assert.That(res.Command, Is.EqualTo(@"mpbkpu backup -t Full -s C:\Data -d D:\Backup -y"));
        Assert.That(res.HasPlaceholders, Is.False);
        var args = Parse(res.Command);
        Assert.That(args.InteractivePassword, Is.False);
        Assert.That(args.ParallelFiles, Is.EqualTo(1));
    }

    [Test]
    public void DestinationsTheCliCannotReachAreReported()
    {
        var res = Cli.ForBackup("mpbkpu", new BackupJobSettings()
        {
            Source = @"C:\Data",
            Destinations =
            [
                new BackupDestination() { Path = @"D:\Backup" },
                new BackupDestination() { Path = "dropbox://me/Backup", OauthSettings = new OAuth2Settings() { AppId = "id" } },
                new BackupDestination() { Path = "nextcloud://me/Backup", User = "me", Password = "app-password" },
            ],
        }, false);

        Assert.That(res.UnsupportedPaths, Is.EqualTo(new[] { "dropbox://me/Backup", "nextcloud://me/Backup" }));
        Assert.That(res.Command, Does.Not.Contain("app-password"));
    }

    [Test]
    public void SyncCommandIsParsedAsTheSameJob()
    {
        var res = Cli.ForSync("mpbkpu", new SyncDirectoryJobSettings()
        {
            Source = @"C:\Data",
            Destination = "sftp:///server/data",
            DestinationUser = "user",
            DestinationPassword = "secret",
            SkipHashCheck = true,
        });

        Assert.That(res.Command, Does.Not.Contain("secret"));
        var args = Parse(res.Command);
        Assert.That(args.Action, Is.EqualTo(Arguments.Actions.SyncDirectory));
        Assert.That(args.SourcePath, Is.EqualTo(@"C:\Data"));
        Assert.That(args.DestinationPaths, Is.EqualTo(new[] { "sftp:///server/data" }));
        Assert.That(args.DestinationUser, Is.EqualTo("user"));
        Assert.That(args.DestinationPassword, Is.EqualTo(Placeholder));
        Assert.That(args.SkipHashCheck, Is.True);
        Assert.That(args.AlwaysYes, Is.True);
    }

    /// <summary>
    /// Parse a command as PowerShell passes it to the CLI: optional call operator, then words, single quoted
    /// words with doubled quotes
    /// </summary>
    private static Arguments Parse(string command)
    {
        var words = new List<string>();
        var i = 0;
        while (i < command.Length) {
            if (command[i] == ' ') {
                i++;
                continue;
            }
            var word = new StringBuilder();
            if (command[i] == '\'') {
                i++;
                while (true) {
                    if (command[i] == '\'' && i + 1 < command.Length && command[i + 1] == '\'') {
                        word.Append('\'');
                        i += 2;
                    } else if (command[i] == '\'') {
                        i++;
                        break;
                    } else {
                        word.Append(command[i++]);
                    }
                }
            } else {
                while (i < command.Length && command[i] != ' ')
                    word.Append(command[i++]);
            }
            words.Add(word.ToString());
        }

        // The executable (and the call operator) are not arguments
        var args = words.SkipWhile(w => w == "&").Skip(1).ToArray();
        var res = new Parser().Parse<Arguments>(args, out var errors);
        Assert.That(errors.Select(e => e.Message), Is.Empty, string.Join(" | ", args));
        return res;
    }
}
