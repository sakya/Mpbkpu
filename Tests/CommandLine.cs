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
        var oauth = new OAuth2Settings()
        {
            AppName = "App", AppId = "id", AppSecret = "secret2",
            ServerUrl = new Uri("https://login.example.com/"), RedirectUrl = new Uri("http://localhost:1234/")
        };
        var settings = new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Incremental,
            Source = @"C:\My Documents\it's here",
            SourceUser = "me",
            SourcePassword = "secret1",
            Destinations =
            [
                new BackupDestination() { Path = @"D:\First" },
                new BackupDestination() { Path = "ftp:///host:21/backups", User = "ftpuser", Password = "secret3" },
                new BackupDestination() { Path = "onedrive://me@live.com/Backups", User = "me@live.com", OauthSettings = oauth },
                new BackupDestination() { Path = @"D:\Last" },
            ],
            CompressionLevel = CompressionLevel.Optimal,
            RetryOnError = 5,
            FullBackupEveryDays = 30,
            ParallelFiles = 4,
            KeepChains = 2,
            Verify = BackupJobSettings.VerifyModes.FullBackups,
            SkipHashCheck = true,
            EncryptPassword = "secret5",
            Exclude = ["bin", "*.tmp", "Old Files/it's here"],
        };

        var res = Cli.ForBackup(@"C:\Program Files\Mpbkpu\mpbkpu.exe", settings, true);
        Assert.That(res.Command, Does.StartWith("& 'C:\\Program Files\\Mpbkpu\\mpbkpu.exe' backup "));
        foreach (var secret in new[] { "secret1", "secret2", "secret3", "secret5" })
            Assert.That(res.Command, Does.Not.Contain(secret));
        Assert.That(res.HasPlaceholders, Is.True);

        var args = Parse(res.Command);
        Assert.That(args.Action, Is.EqualTo(Arguments.Actions.Backup));
        Assert.That(args.Validate(), Is.Null);
        Assert.That(args.BackupType, Is.EqualTo(Backup.BackupTypes.Incremental));
        Assert.That(args.SourcePath, Is.EqualTo(settings.Source));
        Assert.That(args.SourceUser, Is.EqualTo("me"));
        Assert.That(args.SourcePassword, Is.EqualTo(Placeholder));

        // Every destination gets its own credentials back
        var destinations = args.GetDestinations();
        Assert.That(destinations.Select(d => d.Path), Is.EqualTo(settings.Destinations.Select(d => d.Path)));
        Assert.That(destinations.Select(d => d.User), Is.EqualTo(new[] { null, "ftpuser", "me@live.com", null }));
        Assert.That(destinations.Select(d => d.Password), Is.EqualTo(new[] { null, Placeholder, null, null }));
        Assert.That(destinations.Select(d => d.OauthSettings!.IsEmpty), Is.EqualTo(new[] { true, true, false, true }), res.Command);
        var parsedOauth = destinations[2].OauthSettings!;
        Assert.That(parsedOauth.AppName, Is.EqualTo("App"));
        Assert.That(parsedOauth.AppId, Is.EqualTo("id"));
        Assert.That(parsedOauth.AppSecret, Is.EqualTo(Placeholder));
        Assert.That(parsedOauth.ServerUrl, Is.EqualTo(oauth.ServerUrl));
        Assert.That(parsedOauth.RedirectUrl, Is.EqualTo(oauth.RedirectUrl));

        Assert.That(args.CompressionLevel, Is.EqualTo(CompressionLevel.Optimal));
        Assert.That(args.RetryOnError, Is.EqualTo(5));
        Assert.That(args.FullBackupEveryDays, Is.EqualTo(30));
        Assert.That(args.ParallelFiles, Is.EqualTo(4));
        Assert.That(args.KeepChains, Is.EqualTo(2));
        Assert.That(args.Verify, Is.EqualTo(BackupJobSettings.VerifyModes.FullBackups));
        Assert.That(args.SkipHashCheck, Is.True);
        Assert.That(args.Exclude, Is.EqualTo(settings.Exclude), res.Command);
        Assert.That(args.InteractivePassword, Is.True);
        Assert.That(args.Password, Is.Null);
        Assert.That(args.AlwaysYes, Is.True);
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
    public void SingleCredentialValueBelongsToTheFirstPath()
    {
        var args = Parse(@"mpbkpu list -b ftp:///host/a -b D:\b --backupUser me --backupPassword pwd");
        var destinations = args.GetBackupDestinations();
        Assert.That(destinations.Select(d => d.User), Is.EqualTo(new[] { "me", null }));
        Assert.That(destinations.Select(d => d.Password), Is.EqualTo(new[] { "pwd", null }));
    }

    [Test]
    public void SameCredentialsForMorePathsAreKept()
    {
        // The parser drops repeated values: the indexes keep them distinct
        var res = Cli.ForBackup("mpbkpu", new BackupJobSettings()
        {
            Source = @"C:\Data",
            Destinations =
            [
                new BackupDestination() { Path = "ftp:///host/a", User = "bob", Password = "x" },
                new BackupDestination() { Path = "ftp:///host/b", User = "bob", Password = "x" },
            ],
        }, false);
        var destinations = Parse(res.Command).GetDestinations();
        Assert.That(destinations.Select(d => d.User), Is.EqualTo(new[] { "bob", "bob" }), res.Command);
        Assert.That(destinations.Select(d => d.Password), Is.EqualTo(new[] { Placeholder, Placeholder }), res.Command);
    }

    [TestCase(@"mpbkpu list -b D:\a --backupUser a --backupUser 1=b", "More credential values")]
    [TestCase(@"mpbkpu list -b D:\a --backupUser 2=b", "Credential value for path 2")]
    [TestCase(@"mpbkpu list -b D:\a --backupOauthServer not-a-url", "Invalid OAuth server url")]
    public void InvalidCredentialOptionsAreRejected(string command, string error)
    {
        Assert.That(Parse(command).Validate(), Does.StartWith(error));
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
            Exclude = ["node_modules"],
        });

        Assert.That(res.Command, Does.Not.Contain("secret"));
        var args = Parse(res.Command);
        Assert.That(args.Action, Is.EqualTo(Arguments.Actions.SyncDirectory));
        Assert.That(args.SourcePath, Is.EqualTo(@"C:\Data"));
        var destination = args.GetSingleDestination();
        Assert.That(destination.Path, Is.EqualTo("sftp:///server/data"));
        Assert.That(destination.User, Is.EqualTo("user"));
        Assert.That(destination.Password, Is.EqualTo(Placeholder));
        Assert.That(args.SkipHashCheck, Is.True);
        Assert.That(args.Exclude, Is.EqualTo(new[] { "node_modules" }));
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
