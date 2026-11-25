using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using NUnit.Framework;
using File = System.IO.File;

namespace Tests;

public class Sync : BaseTest
{
    [SetUp]
    public void Setup()
    {
        PrepareSourceDirectory();
    }

    [TearDown]
    public void TestCleanup()
    {
        CleanupDirectories();
    }

    [Test]
    public async Task UnchangedFilesAreNotCopiedAgain()
    {
        var first = await RunSync();
        Assert.That(first.Result.Result, Is.EqualTo(Backup.Results.Completed));
        AssertSameContent(BackupSource, BackupDestination);

        var second = await RunSync();
        Assert.That(second.Confirm, Is.Null, "Nothing changed: no file must be copied or deleted");
        Assert.That(second.Result.Result, Is.Null);
        AssertSameContent(BackupSource, BackupDestination);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangedNewAndDeletedFilesAreSynchronized(bool skipHashCheck)
    {
        await RunSync();

        // Same size, different content: only the hash check detects it
        if (!skipHashCheck) {
            var path = Path.Combine(BackupSource, "file0");
            var data = File.ReadAllBytes(path);
            data[0] ^= 0xFF;
            File.WriteAllBytes(path, data);
        }
        CreateFile(Path.Combine(BackupSource, "file1"), 10 * 1024);
        CreateFile(Path.Combine(BackupSource, "SubDirectory2", "new"), 10 * 1024);
        File.Delete(Path.Combine(BackupSource, "file2"));
        Directory.Delete(Path.Combine(BackupSource, "SubDirectory1"), true);

        var res = await RunSync(s => s.SkipHashCheck = skipHashCheck);
        Assert.That(res.Result.Result, Is.EqualTo(Backup.Results.Completed));
        Assert.That(res.Confirm!.FilesToBackup, Is.EqualTo(skipHashCheck ? 2 : 3));
        AssertSameContent(BackupSource, BackupDestination);
        Assert.That(Directory.GetFiles(BackupDestination, "*.mpbkpu-tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task HiddenFilesInDestinationAreKept()
    {
        var hidden = Path.Combine(BackupDestination, ".hidden");
        File.WriteAllText(hidden, "hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        await RunSync(s => s.IncludeHiddenFiles = false);
        Assert.That(File.Exists(hidden), Is.True);
    }

    [Test]
    public async Task ExcludedItemsAreNotCopiedNorDeleted()
    {
        CreateFile(Path.Combine(BackupSource, "file.tmp"), 1024);
        var kept = Path.Combine(BackupDestination, "SubDirectory2", "kept");
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.WriteAllText(kept, "kept");

        await RunSync(s => s.Exclude = new List<string> { "SubDirectory2", "*.tmp", "SubDirectory1/SubDirectory1" });

        Assert.That(File.Exists(Path.Combine(BackupDestination, "file.tmp")), Is.False);
        Assert.That(File.Exists(kept), Is.True);
        Assert.That(Directory.GetFiles(Path.Combine(BackupDestination, "SubDirectory2")), Has.Length.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(BackupDestination, "SubDirectory1", "SubDirectory1")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(BackupDestination, "SubDirectory1")), Is.True);
    }

    [Test]
    public async Task FileReplacedByDirectoryAndViceVersa()
    {
        await RunSync();

        File.Delete(Path.Combine(BackupSource, "file0"));
        Directory.CreateDirectory(Path.Combine(BackupSource, "file0"));
        CreateFile(Path.Combine(BackupSource, "file0", "child"), 1024);
        Directory.Delete(Path.Combine(BackupSource, "SubDirectory2"), true);
        CreateFile(Path.Combine(BackupSource, "SubDirectory2"), 1024);

        var res = await RunSync();
        Assert.That(res.Result.Result, Is.EqualTo(Backup.Results.Completed));
        AssertSameContent(BackupSource, BackupDestination);
    }

    [Test]
    public async Task RefusedConfirmationDeletesNothing()
    {
        var existing = Path.Combine(BackupDestination, "existing");
        File.WriteAllText(existing, "existing");

        var res = await RunSync(confirm: false);
        Assert.That(res.Result.Result, Is.EqualTo(Backup.Results.Cancelled));
        Assert.That(res.Result.Success, Is.False);
        Assert.That(File.Exists(existing), Is.True);
        Assert.That(Directory.GetFileSystemEntries(BackupDestination), Has.Length.EqualTo(1));
    }

    private static async Task<(BaseJobResult Result, SyncDirectoryJob.ConfirmArgs? Confirm)> RunSync(
        Action<SyncDirectoryJobSettings>? configure = null, bool confirm = true)
    {
        var settings = new SyncDirectoryJobSettings()
        {
            Source = BackupSource,
            Destination = BackupDestination,
        };
        configure?.Invoke(settings);

        SyncDirectoryJob.ConfirmArgs? args = null;
        using var job = new SyncDirectoryJob(settings);
        job.ConfirmBackup += (_, e) =>
        {
            args = e;
            return Task.FromResult(confirm);
        };
        var res = await job.Run(CancellationToken.None);
        return (res, args);
    }

    private static void AssertSameContent(string expected, string actual)
    {
        Assert.That(GetContent(actual), Is.EquivalentTo(GetContent(expected)));
    }

    private static List<string> GetContent(string root)
    {
        return Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(p => Directory.Exists(p)
                ? $"{Path.GetRelativePath(root, p)}/"
                : $"{Path.GetRelativePath(root, p)} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}")
            .ToList();
    }
}
