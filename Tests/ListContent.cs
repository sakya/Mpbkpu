using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using NUnit.Framework;

namespace Tests;

public class ListContent : BaseTest
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
            Destinations = BackupDestinations(),
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var lSettings = new ListContentJobSettings()
        {
            BackupDestinations = BackupDestinations(),
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
        };
        var lJob = new ListContentJob(lSettings);
        jobRes = await lJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("List content failed");

        Assert.Pass();
    }

    [Test]
    public async Task Encryption()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destinations = BackupDestinations(),
            EncryptPassword = EncryptionPassword,
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var lSettings = new ListContentJobSettings()
        {
            BackupDestinations = BackupDestinations(),
            EncryptPassword = EncryptionPassword,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
        };
        var lJob = new ListContentJob(lSettings);
        jobRes = await lJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("List content failed");

        Assert.Pass();
    }

    [Test]
    public async Task EncryptionFilter()
    {
        var bSettings = new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destinations = BackupDestinations(),
            EncryptPassword = EncryptionPassword,
        };
        var bJob = new BackupJob(bSettings);
        using var bCts = new CancellationTokenSource();
        var jobRes = await bJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Backup failed");

        var lSettings = new ListContentJobSettings()
        {
            BackupDestinations = BackupDestinations(),
            EncryptPassword = EncryptionPassword,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
            Filter = ".*SubDirectory1.*"
        };
        var lJob = new ListContentJob(lSettings);
        jobRes = await lJob.Run(bCts.Token);
        if (!jobRes.Success)
            Assert.Fail("List content failed");

        Assert.Pass();
    }
}