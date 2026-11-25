using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using NUnit.Framework;

namespace Tests;

public class Verify : BaseTest
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
            Destinations = BackupDestinations(),
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var rSettings = new VerifyJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
        };
        var rJob = new VerifyJob(rSettings);
        using var rCts = new CancellationTokenSource();
        jobRes = await rJob.Run(rCts.Token);
        if (!jobRes.Success || ((VerifyJobResult)jobRes).Errors?.Count > 0)
            Assert.Fail("Verify failed");

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
            Destinations = BackupDestinations(),
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var rSettings = new VerifyJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
        };
        var rJob = new VerifyJob(rSettings);
        using var rCts = new CancellationTokenSource();
        jobRes = await rJob.Run(rCts.Token);
        if (!jobRes.Success || ((VerifyJobResult)jobRes).Errors?.Count > 0)
            Assert.Fail("Verify failed");

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
            Destinations = BackupDestinations(),
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var rSettings = new VerifyJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
            EncryptPassword = EncryptionPassword,
        };
        var rJob = new VerifyJob(rSettings);
        using var rCts = new CancellationTokenSource();
        jobRes = await rJob.Run(rCts.Token);
        if (!jobRes.Success || ((VerifyJobResult)jobRes).Errors?.Count > 0)
            Assert.Fail("Verify failed");

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
            Destinations = BackupDestinations(),
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var rSettings = new VerifyJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
            EncryptPassword = EncryptionPassword,
        };
        var rJob = new VerifyJob(rSettings);
        using var rCts = new CancellationTokenSource();
        jobRes = await rJob.Run(rCts.Token);
        if (!jobRes.Success || ((VerifyJobResult)jobRes).Errors?.Count > 0)
            Assert.Fail("Verify failed");

        Assert.Pass();
    }
}