using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using NUnit.Framework;

namespace Tests;

public class Delete  : BaseTest
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

        var dSettings = new DeleteJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = ((BackupJobJobResult)jobRes).Guid,
        };
        var rJob = new DeleteJob(dSettings);
        using var rCts = new CancellationTokenSource();
        jobRes = await rJob.Run(rCts.Token);
        if (!jobRes.Success)
            Assert.Fail("Delete failed");

        Assert.Pass();
    }
}