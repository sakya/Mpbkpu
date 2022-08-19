using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.Common.Interfaces;
using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;
using NUnit.Framework;

namespace Tests;

public class BackupAndRestore
{
    private const string BackupSource = "/home/sakya/Mpbkpu_Test/source";
    private const string BackupDestination = "/home/sakya/Mpbkpu_Test/backup";
    private const string RestoreDestination = "/home/sakya/Mpbkpu_Test/restore";
    private const string EncryptionPassword = "password";
    private readonly Random _random = new();

    [SetUp]
    public void Setup()
    {
        // Prepare files in the source directory
        if (Directory.Exists(BackupSource))
            Directory.Delete(BackupSource, true);
        if (Directory.Exists(BackupDestination))
            Directory.Delete(BackupDestination, true);
        if (Directory.Exists(RestoreDestination))
            Directory.Delete(RestoreDestination, true);

        Directory.CreateDirectory(BackupSource);
        Directory.CreateDirectory(Path.Combine(BackupSource, "SubDirectory1"));
        Directory.CreateDirectory(Path.Combine(BackupSource, "SubDirectory2"));
        Directory.CreateDirectory(BackupDestination);
        Directory.CreateDirectory(RestoreDestination);

        var minSize = 64 * 1024;
        var maxSize = 5 * 1024 * 1024;

        for (var i = 0; i < _random.Next(3, 10); i++) {
            CreateFile(Path.Combine(BackupSource, $"file{i}"), _random.Next(minSize, maxSize));
        }

        for (var i = 0; i < _random.Next(3, 10); i++) {
            CreateFile(Path.Combine(BackupSource, "SubDirectory1", $"file{i}"), _random.Next(minSize, maxSize));
        }

        for (var i = 0; i < _random.Next(3, 10); i++) {
            CreateFile(Path.Combine(BackupSource, "SubDirectory2", $"file{i}"), _random.Next(minSize, maxSize));
        }
    }

    [TearDown]
    public void TestCleanup()
    {
        // Delete BackupSource and BackupDestination
        if (Directory.Exists(BackupSource))
            Directory.Delete(BackupSource, true);
        if (Directory.Exists(BackupDestination))
            Directory.Delete(BackupDestination, true);
        if (Directory.Exists(RestoreDestination))
            Directory.Delete(RestoreDestination, true);
    }

    [Test]
    public async Task NoCompressionNoEncryption()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = BackupJobSettings.BackupTypes.Full,
            Source = BackupSource,
            Destination = BackupDestination,
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var rSettings = new RestoreJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
            Destination = RestoreDestination,
        };
        var rJob = new RestoreJob(rSettings);
        using var rCts = new CancellationTokenSource();
        jobRes = await rJob.Run(rCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Restore failed");

        Assert.Pass();
    }

    [Test]
    public async Task CompressionNoEncryption()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = BackupJobSettings.BackupTypes.Full,
            Source = BackupSource,
            Compress = true,
            Destination = BackupDestination,
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var rSettings = new RestoreJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
            Destination = RestoreDestination,
        };
        var rJob = new RestoreJob(rSettings);
        using var rCts = new CancellationTokenSource();
        jobRes = await rJob.Run(rCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Restore failed");

        Assert.Pass();
    }

    [Test]
    public async Task CompressionEncryption()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = BackupJobSettings.BackupTypes.Full,
            Source = BackupSource,
            Compress = true,
            EncryptPassword = EncryptionPassword,
            Destination = BackupDestination,
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var rSettings = new RestoreJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
            EncryptPassword = EncryptionPassword,
            Destination = RestoreDestination,
        };
        var rJob = new RestoreJob(rSettings);
        using var rCts = new CancellationTokenSource();
        jobRes = await rJob.Run(rCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Restore failed");

        Assert.Pass();
    }

    private void CreateFile(string path, long size)
    {
        long write = 0;
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) {
            while (write < size) {
                var buffer = new byte[4096];
                int toWrite = buffer.Length;
                if (size - write < toWrite)
                    toWrite = (int)(size - write);

                for (var i = 0; i < toWrite; i++) {
                    buffer[i] = (byte)_random.Next(0, 255);
                }
                fs.Write(buffer, 0, toWrite);
                write += toWrite;
            }
        }
    }
}