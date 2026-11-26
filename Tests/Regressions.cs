using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MpbkpuLib;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using Newtonsoft.Json;
using NUnit.Framework;
using File = System.IO.File;

namespace Tests;

public class Regressions : BaseTest
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

    [TestCase(Backup.BackupTypes.Differential)]
    [TestCase(Backup.BackupTypes.Incremental)]
    public async Task ChainRestoreMatchesSource(Backup.BackupTypes type)
    {
        await RunBackup(Backup.BackupTypes.Full);

        // Changes before the first backup of the chain
        CreateFile(Path.Combine(BackupSource, "file0"), 100 * 1024);
        File.Delete(Path.Combine(BackupSource, "file1"));
        await RunBackup(type);

        // New file and directory after the first backup of the chain; file0 and file1 untouched
        CreateFile(Path.Combine(BackupSource, "newFile"), 100 * 1024);
        Directory.CreateDirectory(Path.Combine(BackupSource, "NewDirectory"));
        CreateFile(Path.Combine(BackupSource, "NewDirectory", "newFile"), 100 * 1024);
        var guid = await RunBackup(type);

        await RunRestore(guid);
        AssertSameContent(BackupSource, RestoreDestination);
    }

    [TestCase(null)]
    [TestCase(EncryptionPassword)]
    public async Task RestoreSingleItemKeepsOtherFiles(string? password)
    {
        var docs = Path.Combine(BackupSource, "Docs");
        Directory.CreateDirectory(Path.Combine(docs, "Sub"));
        CreateFile(Path.Combine(docs, "Sub", "a"), 100 * 1024);
        CreateFile(Path.Combine(docs, "b"), 1024);
        var guid = await RunBackup(Backup.BackupTypes.Full, password);

        Directory.CreateDirectory(RestoreDestination);
        CreateFile(Path.Combine(RestoreDestination, "keep"), 1024);
        // A directory: restored with its name and content, nothing else of the backup
        Assert.That((await Restore(guid, password, relativePath: "Docs")).Success, Is.True);
        AssertSameContent(docs, Path.Combine(RestoreDestination, "Docs"));
        Assert.That(Directory.GetFileSystemEntries(RestoreDestination).Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "Docs", "keep" }));

        // A file: restored without its parent directories, overwriting an existing copy
        CreateFile(Path.Combine(RestoreDestination, "a"), 10);
        Assert.That((await Restore(guid, password, relativePath: Path.Combine("Docs", "Sub", "a"))).Success, Is.True);
        Assert.That(File.ReadAllBytes(Path.Combine(RestoreDestination, "a")),
            Is.EqualTo(File.ReadAllBytes(Path.Combine(docs, "Sub", "a"))));
        Assert.That(File.Exists(Path.Combine(RestoreDestination, "keep")), Is.True);
    }

    [Test]
    public async Task RestoreRemovesExtraFilesAndOverwritesChanged()
    {
        var guid = await RunBackup(Backup.BackupTypes.Full);
        await RunRestore(guid);

        CreateFile(Path.Combine(RestoreDestination, "extraFile"), 1024);
        CreateFile(Path.Combine(RestoreDestination, "file0"), 1024);
        await RunRestore(guid);

        AssertSameContent(BackupSource, RestoreDestination);
    }

    [Test]
    public async Task FailedRestoreKeepsExistingFiles()
    {
        var guid = await RunBackup(Backup.BackupTypes.Full);
        await RunRestore(guid);

        // Destination content different from the backup
        var existingFiles = Directory.GetFiles(RestoreDestination, "*", SearchOption.AllDirectories);
        foreach (var file in existingFiles)
            File.WriteAllText(file, "existing");

        // Corrupt the backup data
        foreach (var file in Directory.GetFiles(Path.Combine(BackupDestination, guid), "*", SearchOption.AllDirectories)) {
            var bytes = File.ReadAllBytes(file);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(file, bytes);
        }

        var job = new RestoreJob(new RestoreJobSettings()
        {
            BackupDestinations = BackupDestinations(),
            BackupGuid = guid,
            Destination = RestoreDestination,
        });
        using var cts = new CancellationTokenSource();
        Assert.That(async () => await job.Run(cts.Token), Throws.Exception);

        foreach (var file in existingFiles)
            Assert.That(File.ReadAllText(file), Is.EqualTo("existing"), file);
        Assert.That(Directory.GetFiles(RestoreDestination, "*.mpbkpu-tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task DeleteChainRemovesFilesAndDatabaseEntries()
    {
        var fullGuid = await RunBackup(Backup.BackupTypes.Full, EncryptionPassword);
        CreateFile(Path.Combine(BackupSource, "newFile"), 100 * 1024);
        var incGuid = await RunBackup(Backup.BackupTypes.Incremental, EncryptionPassword);

        // The encrypted database is written to a temporary file and then moved in place
        Assert.That(File.Exists(Path.Combine(BackupDestination, BackupFile.DefaultDatabaseFileName)), Is.True);
        Assert.That(File.Exists(Path.Combine(BackupDestination, BackupFile.DatabaseBackupFileName)), Is.True);
        Assert.That(File.Exists(Path.Combine(BackupDestination, BackupFile.DatabaseTempFileName)), Is.False);

        var job = new DeleteJob(new DeleteJobSettings()
        {
            BackupDestinations = BackupDestinations(),
            BackupGuid = fullGuid,
            EncryptPassword = EncryptionPassword,
        });
        using var cts = new CancellationTokenSource();
        var res = await job.Run(cts.Token);
        Assert.That(res.Success, Is.True, "Delete failed");

        Assert.That(Directory.Exists(Path.Combine(BackupDestination, fullGuid)), Is.False);
        Assert.That(Directory.Exists(Path.Combine(BackupDestination, incGuid)), Is.False);
        Assert.That(File.Exists(Path.Combine(BackupDestination, BackupFile.DatabaseTempFileName)), Is.False);

        // The deleted backup is no longer in the database: the restore must report a failure
        Assert.That((await Restore(incGuid, EncryptionPassword)).Success, Is.False);
    }

    [Test]
    public async Task TypeChangeStartsNewChain()
    {
        await RunBackup(Backup.BackupTypes.Full);
        await RunBackup(Backup.BackupTypes.Incremental);
        CreateFile(Path.Combine(BackupSource, "file0"), 100 * 1024);
        await RunBackup(Backup.BackupTypes.Differential);
        CreateFile(Path.Combine(BackupSource, "newFile"), 100 * 1024);
        var guid = await RunBackup(Backup.BackupTypes.Differential);

        Assert.That((await GetBackups()).Select(b => b.Type), Is.EqualTo(new[] {
            Backup.BackupTypes.Full, Backup.BackupTypes.Incremental, Backup.BackupTypes.Full, Backup.BackupTypes.Differential
        }));
        await RunRestore(guid);
        AssertSameContent(BackupSource, RestoreDestination);
    }

    [Test]
    public async Task IncompleteFullStartsNewChain()
    {
        // Full backup cancelled at the confirmation
        await RunBackup(Backup.BackupTypes.Full, confirm: (_, _) => Task.FromResult(false));
        await RunBackup(Backup.BackupTypes.Incremental);

        var backups = await GetBackups();
        Assert.That(backups.Select(b => (b.Type, b.Result)), Is.EqualTo(new[] {
            (Backup.BackupTypes.Full, Backup.Results.Cancelled), (Backup.BackupTypes.Full, Backup.Results.Completed)
        }));
    }

    [Test]
    public async Task FullBackupEveryDays()
    {
        await RunBackup(Backup.BackupTypes.Full);
        CreateFile(Path.Combine(BackupSource, "newFile"), 100 * 1024);
        await RunBackup(Backup.BackupTypes.Incremental, configure: s => s.FullBackupEveryDays = 1);

        // Make the full backup two days old
        using (var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(BackupDestination, out var path)) {
            using var openRes = await BackupFile.OpenBackupFile(fm, path);
            var full = openRes.BackupFile.Backups.First(b => b.Type == Backup.BackupTypes.Full);
            full.StartedAt = full.StartedAt!.Value.AddDays(-2);
            await openRes.BackupFile.SaveChangesAsync();
        }
        CreateFile(Path.Combine(BackupSource, "newFile2"), 100 * 1024);
        await RunBackup(Backup.BackupTypes.Incremental, configure: s => s.FullBackupEveryDays = 1);

        Assert.That((await GetBackups()).Select(b => b.Type), Is.EqualTo(new[] {
            Backup.BackupTypes.Full, Backup.BackupTypes.Incremental, Backup.BackupTypes.Full
        }));
    }

    [Test]
    public async Task KeepChainsDeletesOlderChains()
    {
        var oldFull = await RunBackup(Backup.BackupTypes.Full, configure: s => s.KeepChains = 1);
        CreateFile(Path.Combine(BackupSource, "newFile"), 100 * 1024);
        var oldInc = await RunBackup(Backup.BackupTypes.Incremental, configure: s => s.KeepChains = 1);
        CreateFile(Path.Combine(BackupSource, "newFile2"), 100 * 1024);
        var newFull = await RunBackup(Backup.BackupTypes.Full, configure: s => s.KeepChains = 1);

        Assert.That((await GetBackups()).Select(b => b.Guid), Is.EqualTo(new[] { newFull }));
        Assert.That(Directory.Exists(Path.Combine(BackupDestination, oldFull)), Is.False);
        Assert.That(Directory.Exists(Path.Combine(BackupDestination, oldInc)), Is.False);
        await RunRestore(newFull);
        AssertSameContent(BackupSource, RestoreDestination);
    }

    [Test]
    public async Task EncryptionKeyIsDerivedOncePerInstance()
    {
        var enc = new MpbkpuLib.Utility.EncryptDecrypt(EncryptionPassword);
        var encrypted1 = await EncryptBytes(enc, new byte[] { 1, 2, 3 });
        var encrypted2 = await EncryptBytes(enc, new byte[] { 4, 5, 6 });

        // v2 header, same salt (same derived key), different IV
        var headerSize = MpbkpuLib.Utility.EncryptDecrypt.HeaderSize;
        var ivSize = MpbkpuLib.Utility.EncryptDecrypt.AesBlockByteSize;
        var saltSize = MpbkpuLib.Utility.EncryptDecrypt.PasswordSaltByteSize;
        Assert.That(System.Text.Encoding.ASCII.GetString(encrypted1, 0, headerSize), Is.EqualTo(MpbkpuLib.Utility.EncryptDecrypt.HeaderV2));
        Assert.That(encrypted1.AsSpan(headerSize + ivSize, saltSize).ToArray(), Is.EqualTo(encrypted2.AsSpan(headerSize + ivSize, saltSize).ToArray()));
        Assert.That(encrypted1.AsSpan(headerSize, ivSize).ToArray(), Is.Not.EqualTo(encrypted2.AsSpan(headerSize, ivSize).ToArray()));

        var dec = new MpbkpuLib.Utility.EncryptDecrypt(EncryptionPassword);
        Assert.That(await DecryptBytes(dec, encrypted1), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(await DecryptBytes(dec, encrypted2), Is.EqualTo(new byte[] { 4, 5, 6 }));
    }

    [Test]
    public async Task V1EncryptedDataCanBeDecrypted()
    {
        // v1 format: header | IV | salt | AES-CBC ciphertext, no MAC
        var data = System.Text.Encoding.UTF8.GetBytes("data encrypted with the v1 format");
        var iv = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(EncryptionPassword, salt, 100000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        var v1 = System.Text.Encoding.ASCII.GetBytes(MpbkpuLib.Utility.EncryptDecrypt.Header)
            .Concat(iv).Concat(salt).Concat(aes.EncryptCbc(data, iv)).ToArray();

        var dec = new MpbkpuLib.Utility.EncryptDecrypt(EncryptionPassword);
        Assert.That(await DecryptBytes(dec, v1), Is.EqualTo(data));
    }

    [Test]
    public async Task TamperedOrWrongPasswordIsRejected()
    {
        var encrypted = await EncryptBytes(new MpbkpuLib.Utility.EncryptDecrypt(EncryptionPassword), new byte[1000]);

        var tampered = encrypted.ToArray();
        tampered[100] ^= 0x01;
        Assert.That(async () => await DecryptBytes(new MpbkpuLib.Utility.EncryptDecrypt(EncryptionPassword), tampered),
            Throws.InstanceOf<System.Security.Cryptography.CryptographicException>().With.Message.Contains("Authentication failed"));
        Assert.That(async () => await DecryptBytes(new MpbkpuLib.Utility.EncryptDecrypt("wrong password"), encrypted),
            Throws.InstanceOf<System.Security.Cryptography.CryptographicException>().With.Message.Contains("Authentication failed"));
    }

    [Test]
    public async Task LockedFileIsSkippedWithWarning()
    {
        Backup.BackupTypes type = Backup.BackupTypes.Full;
        string guid;
        using (new FileStream(Path.Combine(BackupSource, "file0"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            guid = await RunBackup(type);

        Assert.That((await GetBackups())[0].Result, Is.EqualTo(Backup.Results.CompletedWithWarnings));
        Assert.That(await GetWarnings(), Has.One.Contains("file0"));
        var paths = await GetBackupItemPaths(guid);
        Assert.That(paths, Does.Not.Contain("file0"));
        Assert.That(paths, Does.Contain("file2"));
    }

    [Test]
    public async Task FileChangedDuringCopyIsRetried()
    {
        var changed = false;
        var guid = await RunBackup(Backup.BackupTypes.Full, fileProgress: (relativePath, bytes) =>
        {
            if (relativePath == "file0" && bytes > 0 && !changed) {
                changed = true;
                File.AppendAllText(Path.Combine(BackupSource, "file0"), "changed");
            }
        });

        Assert.That(changed, Is.True);
        Assert.That((await GetBackups())[0].Result, Is.EqualTo(Backup.Results.Completed));
        await RunRestore(guid);
        AssertSameContent(BackupSource, RestoreDestination);
    }

    [Test]
    public async Task FileAlwaysChangingIsBackedUpWithWarning()
    {
        var guid = await RunBackup(Backup.BackupTypes.Full, fileProgress: (relativePath, bytes) =>
        {
            if (relativePath == "file0" && bytes > 0)
                File.AppendAllText(Path.Combine(BackupSource, "file0"), "changed");
        });

        Assert.That((await GetBackups())[0].Result, Is.EqualTo(Backup.Results.CompletedWithWarnings));
        Assert.That(await GetWarnings(), Has.One.Contains("changed during the copy"));
        Assert.That(await GetBackupItemPaths(guid), Does.Contain("file0"));
    }

    private static async Task<byte[]> EncryptBytes(MpbkpuLib.Utility.EncryptDecrypt enc, byte[] data)
    {
        using var source = new MemoryStream(data);
        using var dest = new MemoryStream();
        await enc.Encrypt(source, dest, System.IO.Compression.CompressionLevel.NoCompression);
        return dest.ToArray();
    }

    private static async Task<byte[]> DecryptBytes(MpbkpuLib.Utility.EncryptDecrypt dec, byte[] data)
    {
        using var source = new MemoryStream(data);
        using var dest = new MemoryStream();
        await dec.Decrypt(source, dest, false);
        return dest.ToArray();
    }

    private static async Task<string?[]> GetWarnings()
    {
        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(BackupDestination, out var path);
        using var openRes = await BackupFile.OpenBackupFile(fm, path);
        return openRes.BackupFile.BackupWarnings.Select(w => w.Message).ToArray();
    }

    private static async Task<Backup[]> GetBackups()
    {
        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(BackupDestination, out var path);
        using var openRes = await BackupFile.OpenBackupFile(fm, path);
        return openRes.BackupFile.Backups.OrderBy(b => b.Id).ToArray();
    }

    [Test]
    public async Task WrongPasswordRaisesInvalidPasswordException()
    {
        await RunBackup(Backup.BackupTypes.Full, "correct password");

        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(BackupDestination, out var path);
        // The GUI forgets a saved password only for this exception
        await Assert.ThrowsAsync<MpbkpuLib.InvalidPasswordException>(async () =>
        {
            using var openRes = await BackupFile.OpenBackupFile(fm, path, "wrong password");
        });
        using var ok = await BackupFile.OpenBackupFile(fm, path, "correct password");
        Assert.That(ok.BackupFile.Backups.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task DirectoryTreeHasTheBackedUpFolderAsRoot()
    {
        var guid = await RunBackup(Backup.BackupTypes.Full);

        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(BackupDestination, out var path);
        using var openRes = await BackupFile.OpenBackupFile(fm, path);
        var id = openRes.BackupFile.Backups.First(b => b.Guid == guid).Id;
        var tree = await openRes.BackupFile.GetDirectoryTree(id);

        // The files directly in the source folder belong to the root node (path ""), shown in the browse page
        Assert.That(tree, Has.Count.EqualTo(1));
        Assert.That(tree[0].Path, Is.EqualTo(string.Empty));
        Assert.That(tree[0].Name, Is.EqualTo("source"));
        Assert.That(tree[0].Children.Select(c => c.Name), Is.EquivalentTo(new[] { "SubDirectory1", "SubDirectory2" }));
        Assert.That(tree[0].Children.First(c => c.Name == "SubDirectory1").Children.Select(c => c.Name),
            Is.EquivalentTo(new[] { "SubDirectory1" }));
    }

    private static async Task<string?[]> GetBackupItemPaths(string guid)
    {
        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(BackupDestination, out var path);
        using var openRes = await BackupFile.OpenBackupFile(fm, path);
        var id = openRes.BackupFile.Backups.First(b => b.Guid == guid).Id;
        var res = new System.Collections.Generic.List<string?>();
        await foreach (var item in openRes.BackupFile.GetBackupItems(id))
            res.Add(item.OriginalRelativePath);
        return res.ToArray();
    }

    [Test]
    public async Task IncrementalDeletedAndRecreatedFilesAreNotDuplicated()
    {
        await RunBackup(Backup.BackupTypes.Full);
        // file0 is in the full backup, newFile is added by the incremental backups
        File.Delete(Path.Combine(BackupSource, "file0"));
        CreateFile(Path.Combine(BackupSource, "newFile"), 1024);
        await RunBackup(Backup.BackupTypes.Incremental);
        File.Delete(Path.Combine(BackupSource, "newFile"));
        CreateFile(Path.Combine(BackupSource, "file0"), 1024);
        await RunBackup(Backup.BackupTypes.Incremental);
        CreateFile(Path.Combine(BackupSource, "newFile"), 1024);
        var guid = await RunBackup(Backup.BackupTypes.Incremental);

        var paths = await GetBackupItemPaths(guid);
        Assert.That(paths, Is.Unique);
        await RunRestore(guid);
        AssertSameContent(BackupSource, RestoreDestination);
    }

    [Test]
    [Platform("Win")]
    public async Task JunctionsAreNotFollowed()
    {
        // A junction to the source itself: following it would loop
        var junction = Path.GetFullPath(Path.Combine(BackupSource, "Loop"));
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{Path.GetFullPath(BackupSource)}\"") { CreateNoWindow = true })!)
            p.WaitForExit();
        Assert.That(Directory.Exists(junction), Is.True, "Cannot create the junction");

        var guid = await RunBackup(Backup.BackupTypes.Full);
        Directory.Delete(junction);

        await RunRestore(guid);
        AssertSameContent(BackupSource, RestoreDestination);
    }

    [Test]
    [Platform("Win")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task UnreadableDirectoryDoesNotAbortBackup()
    {
        var locked = new DirectoryInfo(Path.Combine(BackupSource, "Locked"));
        locked.Create();
        CreateFile(Path.Combine(locked.FullName, "file"), 1024);
        await RunBackup(Backup.BackupTypes.Full);

        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var security = locked.GetAccessControl();
        security.AddAccessRule(rule);
        locked.SetAccessControl(security);
        try {
            CreateFile(Path.Combine(BackupSource, "newFile"), 1024);
            var guid = await RunBackup(Backup.BackupTypes.Incremental);

            // The content of the unreadable directory is not recorded as deleted
            Assert.That(await GetBackupItemPaths(guid), Does.Contain(Path.Combine("Locked", "file")));
            Assert.That(await GetBackupItemPaths(guid), Does.Contain("newFile"));
        } finally {
            security.RemoveAccessRule(rule);
            locked.SetAccessControl(security);
        }
    }

    [Test]
    public void FailedBackupLeavesNoFiles()
    {
        var job = new BackupJob(new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destinations = BackupDestinations(),
        });
        job.Progress += (_, _) => throw new IOException("Simulated failure");
        using var cts = new CancellationTokenSource();
        Assert.That(async () => await job.Run(cts.Token), Throws.Exception);

        // Only the database: no backup directory, no partial files
        Assert.That(Directory.GetDirectories(BackupDestination), Is.Empty);
    }

    [TestCase("data", "data", true)]
    [TestCase("data", "data/sub", true)]
    [TestCase("data/", "data/sub/", true)]
    [TestCase("data", "data2", false)]
    [TestCase("data/sub", "data", false)]
    [TestCase("ftp://host/data", "ftp://host/data/sub", true)]
    [TestCase("ftp://host/data", "ftp://host/data2", false)]
    [TestCase("ftp://host/data", "data/sub", false)]
    public void IsSameOrSubPath(string parent, string path, bool expected)
    {
        Assert.That(MpbkpuLib.Utility.Files.IsSameOrSubPath(parent, path), Is.EqualTo(expected));
    }

    [Test]
    [Platform("Win")]
    public void IsSameOrSubPathIgnoresCaseOnWindows()
    {
        Assert.That(MpbkpuLib.Utility.Files.IsSameOrSubPath("data", "DATA/sub"), Is.True);
    }

    [Test]
    public void LegacySingleDestinationSettingsAreRead()
    {
        // Settings saved in the backup databases before multiple destinations
        var json = "{\"Source\":\"src\",\"Destination\":\"ftp://user:***@host/backup\",\"DestinationUser\":\"user\"," +
                   "\"DestinationOauthSettings\":{\"AppName\":\"app\"}}";
        var settings = JsonConvert.DeserializeObject<BackupJobSettings>(json)!;
        Assert.That(settings.Destinations, Has.Count.EqualTo(1));
        Assert.That(settings.Destinations[0].Path, Is.EqualTo("ftp://user:***@host/backup"));
        Assert.That(settings.Destinations[0].User, Is.EqualTo("user"));
        Assert.That(settings.Destinations[0].OauthSettings?.AppName, Is.EqualTo("app"));
        Assert.That(settings.ParallelFiles, Is.EqualTo(1));

        // Saved again only in the new form, without the password
        settings.Destinations[0].Password = "secret";
        var saved = JsonConvert.SerializeObject(settings);
        Assert.That(saved, Does.Not.Contain("\"Destination\"").And.Not.Contain("secret"));
        var reloaded = JsonConvert.DeserializeObject<BackupJobSettings>(saved)!;
        Assert.That(reloaded.Destinations.Select(d => d.Id), Is.EqualTo(settings.Destinations.Select(d => d.Id)));

        var noDestination = JsonConvert.DeserializeObject<BackupJobSettings>("{\"Source\":\"src\",\"Destination\":null}")!;
        Assert.That(noDestination.Destinations, Is.Empty);
    }

    [Test]
    public async Task InvalidDestinationSettingsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new BackupJob(new BackupJobSettings() { Source = BackupSource }).Run(CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new BackupJob(new BackupJobSettings() { Source = BackupSource, Destinations = [new()] }).Run(CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new BackupJob(new BackupJobSettings() { Source = BackupSource, Destinations = BackupDestinations(), ParallelFiles = 0 }).Run(CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new BackupJob(new BackupJobSettings() { Source = BackupSource, Destinations = Destinations(BackupDestination, BackupDestination + "/sub") }).Run(CancellationToken.None));
        var duplicateIds = Destinations(BackupDestination, ExtraDestinations[0]);
        duplicateIds[1].Id = duplicateIds[0].Id;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new BackupJob(new BackupJobSettings() { Source = BackupSource, Destinations = duplicateIds }).Run(CancellationToken.None));
    }

    [Test]
    public async Task DatabaseBeforeMultipleDestinationsIsMigrated()
    {
        var dbPath = Path.Combine(BackupDestination, "old.db");
        var options = new DbContextOptionsBuilder().UseSqlite($"Data Source={dbPath};Pooling=false").Options;
        await using (var context = new MpbkpuLib.Context.BackupContext(options)) {
            await context.GetService<IMigrator>().MigrateAsync("20220819143400_Initial");
            await context.Database.ExecuteSqlRawAsync("INSERT INTO Backups (Id, Guid, Type, Result) VALUES (1, 'old', 0, 1)");
            await context.Database.ExecuteSqlRawAsync("INSERT INTO BackupVolumes (Id, BackupId, Name) VALUES (1, 1, 'volume')");
        }

        using var backupFile = new BackupFile();
        await backupFile.Open(dbPath);
        var volume = await backupFile.BackupVolumes.SingleAsync();
        Assert.That(volume.Name, Is.EqualTo("volume"));
        Assert.That(volume.DestinationId, Is.Null);
        Assert.That(await backupFile.Destinations.CountAsync(), Is.Zero);
        backupFile.Close();
    }

    [Test]
    public async Task FilesAreSplitAmongDestinationsAndDatabaseIsInAll()
    {
        string[] all = [BackupDestination, ..ExtraDestinations];
        // New ids on every run (like the CLI): the destinations are matched by path
        var guid = await RunBackup(Backup.BackupTypes.Full, configure: s => s.Destinations = Destinations(all));
        foreach (var destination in all) {
            Assert.That(File.Exists(Path.Combine(destination, BackupFile.DefaultDatabaseFileName)), Is.True, destination);
            Assert.That(Directory.Exists(Path.Combine(destination, guid)), Is.True, $"No files in {destination}");
            await AssertItemsInTheirDestinations(destination, all.Length);
        }

        File.WriteAllText(Path.Combine(BackupSource, "changed.txt"), "changed");
        await RunBackup(Backup.BackupTypes.Incremental, configure: s => s.Destinations = Destinations(all));
        foreach (var destination in all)
            Assert.That(await AssertItemsInTheirDestinations(destination, all.Length), Is.EqualTo(2), destination);
    }

    [Test]
    public async Task LegacyVolumesBelongToTheDestinationOfTheDatabase()
    {
        await RunBackup(Backup.BackupTypes.Full);
        // Turn the database into one created before multiple destinations
        using (var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(BackupDestination, out var path))
        using (var openRes = await BackupFile.OpenBackupFile(fm, path)) {
            await openRes.BackupFile.BackupVolumes.ExecuteUpdateAsync(s => s.SetProperty(v => v.DestinationId, (int?)null));
            await openRes.BackupFile.Destinations.ExecuteDeleteAsync();
        }

        // The new destination comes first: the database must still be read from the old one
        File.WriteAllText(Path.Combine(BackupSource, "new.txt"), "new");
        await RunBackup(Backup.BackupTypes.Incremental, configure: s => s.Destinations = Destinations(ExtraDestinations[0], BackupDestination));
        Assert.That(await AssertItemsInTheirDestinations(ExtraDestinations[0], 2), Is.EqualTo(2));
    }

    [Test]
    public async Task DatabaseCopyFailureOnOneDestinationIsAWarning()
    {
        // A directory in place of the temporary database file makes the copy fail
        Directory.CreateDirectory(Path.Combine(ExtraDestinations[0], BackupFile.DatabaseTempFileName));
        using var job = new BackupJob(new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destinations = Destinations(BackupDestination, ExtraDestinations[0]),
        });
        var res = (BackupJobJobResult)await job.Run(CancellationToken.None);
        Assert.That(res.Success, Is.True);
        Assert.That(res.Result, Is.EqualTo(Backup.Results.CompletedWithWarnings));
        Assert.That(File.Exists(Path.Combine(ExtraDestinations[0], BackupFile.DefaultDatabaseFileName)), Is.False);
        await AssertItemsInTheirDestinations(BackupDestination, 2);
    }

    [TestCase(null)]
    [TestCase(EncryptionPassword)]
    public async Task ParallelBackupRestoresTheSource(string? password)
    {
        await RunBackup(Backup.BackupTypes.Full, password, s => s.ParallelFiles = 4);
        CreateFile(Path.Combine(BackupSource, "newFile"), 100 * 1024);
        var guid = await RunBackup(Backup.BackupTypes.Incremental, password, s => s.ParallelFiles = 4);

        Assert.That((await Restore(guid, password)).Success, Is.True, "Restore failed");
        AssertSameContent(BackupSource, RestoreDestination);
    }

    [Test]
    public async Task LocalFileSystemsCopyFilesAtTheSameTime()
    {
        for (var i = 0; i < 8; i++)
            CreateFile(Path.Combine(BackupSource, $"large{i}"), 4 * 1024 * 1024);

        string? workersMessage = null;
        var active = new System.Collections.Generic.HashSet<string>();
        var maxActive = 0;
        using var job = new BackupJob(new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destinations = BackupDestinations(),
            ParallelFiles = 4,
        });
        job.LogEmitted += (_, _, message) =>
        {
            if (message.StartsWith("Copying up to"))
                workersMessage = message;
        };
        // A file is being copied from its first progress to its completion
        job.FileProgress += (_, path, _, _) =>
        {
            lock (active) {
                active.Add(path);
                maxActive = Math.Max(maxActive, active.Count);
            }
        };
        job.FileCompleted += (_, path) =>
        {
            lock (active)
                active.Remove(path);
        };
        var res = await job.Run(CancellationToken.None);

        Assert.That(res.Success, Is.True);
        Assert.That(workersMessage, Is.EqualTo("Copying up to 4 files at the same time"));
        Assert.That(maxActive, Is.GreaterThan(1), "No files copied at the same time");
        Assert.That(active, Is.Empty, "Files never completed");
    }

    [Test]
    public async Task ParallelProgressReachesTheTotal()
    {
        BaseJob.ProgressHandlerArgs? last = null;
        using var job = new BackupJob(new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destinations = Destinations([BackupDestination, ..ExtraDestinations]),
            ParallelFiles = 4,
        });
        job.Progress += (_, args) => last = args;
        var res = await job.Run(CancellationToken.None);

        Assert.That(res.Success, Is.True);
        Assert.That(last, Is.Not.Null);
        Assert.That(last!.File, Is.EqualTo(last.TotalFiles));
        Assert.That(last.Bytes, Is.EqualTo(last.TotalBytes));
        await AssertItemsInTheirDestinations(BackupDestination, 1 + ExtraDestinations.Length);
    }

    [Test]
    public void FailedParallelBackupLeavesNoFiles()
    {
        using var job = new BackupJob(new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destinations = Destinations(BackupDestination, ExtraDestinations[0]),
            ParallelFiles = 4,
            RetryOnError = 1,
        });
        job.FileProgress += (_, _, bytes, _) =>
        {
            if (bytes > 0)
                throw new IOException("Simulated failure");
        };
        // The error of the failed copy, not the cancellation of the other copies
        Assert.That(async () => await job.Run(CancellationToken.None), Throws.TypeOf<IOException>());

        Assert.That(Directory.GetDirectories(BackupDestination), Is.Empty);
        Assert.That(Directory.GetDirectories(ExtraDestinations[0]), Is.Empty);
    }

    [Test]
    public async Task ParallelBackupCanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        using var job = new BackupJob(new BackupJobSettings()
        {
            BackupType = Backup.BackupTypes.Full,
            Source = BackupSource,
            Destinations = BackupDestinations(),
            ParallelFiles = 4,
        });
        job.FileProgress += (_, _, bytes, _) =>
        {
            if (bytes > 0)
                cts.Cancel();
        };
        var res = (BackupJobJobResult)await job.Run(cts.Token);

        Assert.That(res.Result, Is.EqualTo(Backup.Results.Cancelled));
        Assert.That((await GetBackups()).Single().Result, Is.EqualTo(Backup.Results.Cancelled));
    }

    [Test]
    public async Task BackupsSplitAmongDestinationsAreRestoredVerifiedAndDeleted()
    {
        string[] all = [BackupDestination, ..ExtraDestinations];
        var full = await RunBackup(Backup.BackupTypes.Full, configure: s => s.Destinations = Destinations(all));
        CreateFile(Path.Combine(BackupSource, "newFile"), 100 * 1024);
        var incremental = await RunBackup(Backup.BackupTypes.Incremental, configure: s => s.Destinations = Destinations(all));

        // The database is read from any destination, the files from their own destination
        Assert.That((await Restore(incremental, destinations: [..all.Reverse()])).Success, Is.True, "Restore failed");
        AssertSameContent(BackupSource, RestoreDestination);

        using (var verify = new VerifyJob(new VerifyJobSettings() { BackupDestinations = Destinations(all), BackupGuid = full })) {
            var res = (VerifyJobResult)await verify.Run(CancellationToken.None);
            Assert.That(res.Success, Is.True);
            Assert.That(res.Errors, Is.Empty);
        }

        // Deleting the full backup deletes its chain in every destination
        using (var delete = new DeleteJob(new DeleteJobSettings() { BackupDestinations = Destinations(all), BackupGuid = full })) {
            Assert.That((await delete.Run(CancellationToken.None)).Success, Is.True);
        }
        foreach (var destination in all) {
            Assert.That(Directory.GetDirectories(destination), Is.Empty, destination);
            using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(destination, out var path);
            using var openRes = await BackupFile.OpenBackupFile(fm, path);
            Assert.That(await openRes.BackupFile.Backups.CountAsync(), Is.Zero, destination);
        }
    }

    [Test]
    public async Task MissingDestinationIsReported()
    {
        string[] all = [BackupDestination, ..ExtraDestinations];
        var guid = await RunBackup(Backup.BackupTypes.Full, configure: s => s.Destinations = Destinations(all));

        Assert.That(async () => await Restore(guid, destinations: [BackupDestination]),
            Throws.TypeOf<MissingDestinationException>().With.Property(nameof(MissingDestinationException.Path)).EqualTo(ExtraDestinations[0])
                .Or.Property(nameof(MissingDestinationException.Path)).EqualTo(ExtraDestinations[1]));

        using var verify = new VerifyJob(new VerifyJobSettings() { BackupDestinations = BackupDestinations(), BackupGuid = guid });
        var res = (VerifyJobResult)await verify.Run(CancellationToken.None);
        Assert.That(res.Errors, Is.Not.Empty);
        Assert.That(res.Errors!.All(e => e.Code == VerifyJobResult.Error.ErrorCodes.FileNotFound), Is.True);
    }

    [Test]
    public async Task KeepChainsDeletesOlderChainsInEveryDestination()
    {
        string[] all = [BackupDestination, ..ExtraDestinations];
        var oldFull = await RunBackup(Backup.BackupTypes.Full, configure: s =>
        {
            s.Destinations = Destinations(all);
            s.KeepChains = 1;
        });
        CreateFile(Path.Combine(BackupSource, "newFile"), 100 * 1024);
        var newFull = await RunBackup(Backup.BackupTypes.Full, configure: s =>
        {
            s.Destinations = Destinations(all);
            s.KeepChains = 1;
        });

        foreach (var destination in all)
            Assert.That(Directory.Exists(Path.Combine(destination, oldFull)), Is.False, destination);
        Assert.That(await AssertItemsInTheirDestinations(ExtraDestinations[1], all.Length), Is.EqualTo(1));
        Assert.That((await Restore(newFull, destinations: all)).Success, Is.True, "Restore failed");
        AssertSameContent(BackupSource, RestoreDestination);
    }

    /// <summary>
    /// Check that every backed up item of the database in <paramref name="databaseDestination"/> is in the destination
    /// recorded for its volume
    /// </summary>
    /// <returns>The number of backups</returns>
    private static async Task<int> AssertItemsInTheirDestinations(string databaseDestination, int destinations)
    {
        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(databaseDestination, out var path);
        using var openRes = await BackupFile.OpenBackupFile(fm, path);
        var backupFile = openRes.BackupFile;
        var records = await backupFile.Destinations.ToDictionaryAsync(d => d.Id, d => d.Path);
        Assert.That(records, Has.Count.EqualTo(destinations));
        var items = await backupFile.BackupItems.Include(i => i.Backup).Include(i => i.Volume)
            .Where(i => i.VolumeId != null).ToListAsync();
        Assert.That(items, Is.Not.Empty);
        foreach (var item in items) {
            var destination = records[item.Volume!.DestinationId!.Value]!;
            var itemPath = Path.Combine(destination, item.Backup!.Guid, item.Volume.Name!, item.Name!);
            Assert.That(File.Exists(itemPath), Is.True, $"{item.OriginalPath} not found in {destination}");
        }
        return await backupFile.Backups.CountAsync();
    }

    private async Task<string> RunBackup(Backup.BackupTypes type, string? password = null,
        System.Action<BackupJobSettings>? configure = null, BackupJob.ConfirmHandler? confirm = null,
        System.Action<string, long>? fileProgress = null)
    {
        var settings = new BackupJobSettings()
        {
            BackupType = type,
            Source = BackupSource,
            Destinations = BackupDestinations(),
            EncryptPassword = password,
        };
        configure?.Invoke(settings);
        var job = new BackupJob(settings);
        if (confirm != null)
            job.ConfirmBackup += confirm;
        if (fileProgress != null)
            job.FileProgress += (_, path, bytes, _) => fileProgress(path, bytes);
        using var cts = new CancellationTokenSource();
        var res = await job.Run(cts.Token);
        Assert.That(res.Success, Is.True, $"{type} backup failed");
        return ((BackupJobJobResult)res).Guid!;
    }

    private async Task RunRestore(string guid)
    {
        Assert.That((await Restore(guid)).Success, Is.True, "Restore failed");
    }

    private async Task<BaseJobResult> Restore(string guid, string? password = null, string[]? destinations = null,
        string? relativePath = null)
    {
        var job = new RestoreJob(new RestoreJobSettings()
        {
            BackupDestinations = destinations != null ? Destinations(destinations) : BackupDestinations(),
            BackupGuid = guid,
            Destination = RestoreDestination,
            EncryptPassword = password,
            RelativePath = relativePath,
        });
        using var cts = new CancellationTokenSource();
        return await job.Run(cts.Token);
    }

    private static void AssertSameContent(string expectedDir, string actualDir)
    {
        string[] List(string dir) => Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(dir, p))
            .OrderBy(p => p)
            .ToArray();

        Assert.That(List(actualDir), Is.EqualTo(List(expectedDir)));
        foreach (var file in Directory.GetFiles(expectedDir, "*", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(expectedDir, file);
            Assert.That(File.ReadAllBytes(Path.Combine(actualDir, relative)), Is.EqualTo(File.ReadAllBytes(file)), relative);
        }
    }
}
