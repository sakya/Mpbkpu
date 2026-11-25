using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
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
            BackupPath = BackupDestination,
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
            BackupPath = BackupDestination,
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
            Destination = BackupDestination,
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

    private async Task<string> RunBackup(Backup.BackupTypes type, string? password = null,
        System.Action<BackupJobSettings>? configure = null, BackupJob.ConfirmHandler? confirm = null,
        System.Action<string, long>? fileProgress = null)
    {
        var settings = new BackupJobSettings()
        {
            BackupType = type,
            Source = BackupSource,
            Destination = BackupDestination,
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

    private async Task<BaseJobResult> Restore(string guid, string? password = null)
    {
        var job = new RestoreJob(new RestoreJobSettings()
        {
            BackupPath = BackupDestination,
            BackupGuid = guid,
            Destination = RestoreDestination,
            EncryptPassword = password,
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
