using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using NUnit.Framework;

namespace Tests;

public class List : BaseTest
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
    public async Task NoEncryption()
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

        var lSettings = new ListJobSettings()
        {
            BackupPath = BackupDestination,
        };
        var lJob = new ListJob(lSettings);
        jobRes = await lJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("List failed");

        Assert.Pass();
    }

    [Test]
    public async Task Encryption()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destination = BackupDestination,
            EncryptPassword = EncryptionPassword,
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var lSettings = new ListJobSettings()
        {
            BackupPath = BackupDestination,
            EncryptPassword = EncryptionPassword
        };
        var lJob = new ListJob(lSettings);
        jobRes = await lJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("List failed");

        Assert.Pass();
    }
}