using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using NUnit.Framework;

namespace Tests;

public class BackupAndRestore : BaseTest
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
    public async Task NoCompressionNoEncryption()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
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
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            CompressionLevel = CompressionLevel.SmallestSize,
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
    public async Task NoCompressionEncryption()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
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

    [Test]
    public async Task CompressionEncryption()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            CompressionLevel = CompressionLevel.SmallestSize,
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
}