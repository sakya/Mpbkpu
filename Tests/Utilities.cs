using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuLib.Abstracts;
using MpbkpuLib.Utility;
using NUnit.Framework;

namespace Tests;

public class Utilities
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task UploadStreamDeliversAllTheData()
    {
        var data = new byte[1024 * 1024 + 123];
        Random.Shared.NextBytes(data);
        var uploaded = new MemoryStream();

        var stream = new UploadStream(data.Length, (s, _) => s.CopyToAsync(uploaded));
        for (var offset = 0; offset < data.Length; offset += 32768)
            await stream.WriteAsync(data.AsMemory(offset, Math.Min(32768, data.Length - offset)));
        await stream.CompleteAsync().WaitAsync(Timeout);

        Assert.That(uploaded.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public async Task UploadStreamCancelsAnIncompleteUpload()
    {
        var cancelled = false;
        var stream = new UploadStream(1000, async (s, token) =>
        {
            token.Register(() => cancelled = true);
            await s.CopyToAsync(Stream.Null, token);
        });
        await stream.WriteAsync(new byte[500]);

        await Assert.ThrowsAsync<IOException>(() => stream.CompleteAsync().WaitAsync(Timeout));
        Assert.That(cancelled, Is.True);
    }

    [Test]
    public async Task UploadStreamDoesNotHangWhenTheUploadFails()
    {
        var stream = new UploadStream(10 * 1024 * 1024, (_, _) => throw new InvalidOperationException("upload failed"));
        var buffer = new byte[32768];

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            for (var i = 0; i < 320; i++)
                await stream.WriteAsync(buffer).AsTask().WaitAsync(Timeout);
        });
    }

    [Test]
    public void UploadStreamRejectsMoreDataThanDeclared()
    {
        var stream = new UploadStream(10, (s, _) => s.CopyToAsync(Stream.Null));
        Assert.Throws<IOException>(() => stream.Write(new byte[11], 0, 11));
    }

    [Test]
    public async Task NextcloudPathsAreEscapedAndParsed()
    {
        using var client = new NextcloudClient.Client(new NextcloudClient.ClientSettings()
        {
            ServerUri = new Uri("https://cloud.example.com/nc"),
            UserId = "john doe",
            Password = "password"
        });
        // With a password no request is sent
        await client.Login();

        Assert.That(client.GetDavUrl("/Photos 2024/a#b.txt"),
            Is.EqualTo("https://cloud.example.com/nc/remote.php/dav/files/john%20doe/Photos%202024/a%23b.txt"));
        Assert.That(client.GetDavUrl("/"), Is.EqualTo("https://cloud.example.com/nc/remote.php/dav/files/john%20doe/"));
        Assert.That(client.GetPathFromHref("/nc/remote.php/dav/files/john%20doe/Photos%202024/"), Is.EqualTo("/Photos 2024"));
        Assert.That(client.GetPathFromHref("/nc/remote.php/dav/files/john%20doe/"), Is.EqualTo("/"));
    }

    [Test]
    public async Task LocalReadOnlyFilesCanBeDeleted()
    {
        var root = CreateTempDirectory();
        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(root, out _);
        var file = Path.Combine(root, "file");
        var dir = Path.Combine(root, "dir");
        Directory.CreateDirectory(dir);
        foreach (var f in new[] { file, Path.Combine(dir, "inner") }) {
            File.WriteAllText(f, "x");
            File.SetAttributes(f, FileAttributes.ReadOnly);
        }

        await fm.DeleteFile(file);
        await fm.DeleteDirectory(dir);
        Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
        Directory.Delete(root);
    }

    [Test]
    public async Task LocalDatesAreUtcAndLinksAreNotFollowed()
    {
        var root = CreateTempDirectory();
        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(root, out _);
        var file = Path.Combine(root, "file");
        File.WriteAllText(file, "x");
        Directory.CreateDirectory(Path.Combine(root, "dir"));

        var info = await fm.GetFileInfo(file);
        Assert.That(info.LastModified, Is.EqualTo(File.GetLastWriteTimeUtc(file)));

        var dirLink = Path.Combine(root, "dirLink");
        if (OperatingSystem.IsWindows()) {
            // Junctions need no privilege
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{dirLink}\" \"{Path.Combine(root, "dir")}\"")
                { CreateNoWindow = true, UseShellExecute = false })!;
            await p.WaitForExitAsync();
        } else {
            Directory.CreateSymbolicLink(dirLink, Path.Combine(root, "dir"));
        }
        Assert.That(Directory.Exists(dirLink), Is.True);
        try {
            File.CreateSymbolicLink(Path.Combine(root, "brokenLink"), Path.Combine(root, "missing"));
        } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            // File symbolic links need a privilege on Windows: the broken link case is not tested
        }

        var names = new List<string>();
        await foreach (var item in fm.GetDirectoryContent(root))
            names.Add(item.Name!);
        Assert.That(names, Is.EquivalentTo(new[] { "dir", "file" }));

        // Deleting removes the link, not the content of its target
        File.WriteAllText(Path.Combine(root, "dir", "inner"), "x");
        await fm.DeleteDirectory(dirLink);
        Assert.That(File.Exists(Path.Combine(root, "dir", "inner")), Is.True);
        // A directory containing a link can be deleted
        dirLink = Path.Combine(root, "dir", "dirLink2");
        if (OperatingSystem.IsWindows()) {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{dirLink}\" \"{root}\"")
                { CreateNoWindow = true, UseShellExecute = false })!;
            await p.WaitForExitAsync();
        } else {
            Directory.CreateSymbolicLink(dirLink, root);
        }
        await fm.DeleteDirectory(root);
        Assert.That(Directory.Exists(root), Is.False);
    }

    [Test]
    public void ModificationTimesSavedAsLocalTimeStillMatch()
    {
        var utc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        var savedByOldVersion = DateTime.SpecifyKind(utc.ToLocalTime(), DateTimeKind.Unspecified);
        Assert.That(Files.SameModificationTime(utc, DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)), Is.True);
        Assert.That(Files.SameModificationTime(utc, savedByOldVersion), Is.True);
        Assert.That(Files.SameModificationTime(utc, utc.AddSeconds(1)), Is.False);
        Assert.That(Files.SameModificationTime(utc, null), Is.False);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mpbkpu-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    [Test]
    public void FtpAndSftpUseTheGivenCredentials()
    {
        using var ftp = (MpbkpuLib.FileManagers.Ftp)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager("ftp://host/backups", out var ftpPath, "user", "secret");
        Assert.That(ftp.Credentials?.UserName, Is.EqualTo("user"));
        Assert.That(ftp.Credentials?.Password, Is.EqualTo("secret"));
        Assert.That(ftpPath, Is.EqualTo("/backups"));

        // Form written by the GUI (scheme + "/" + "host/path")
        using var gui = (MpbkpuLib.FileManagers.Ftp)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager("ftp:///host:2121/backups", out var guiPath, "user", "secret");
        Assert.That(gui.Host, Is.EqualTo("host"));
        Assert.That(gui.Port, Is.EqualTo(2121));
        Assert.That(guiPath, Is.EqualTo("/backups"));

        using var sftp = (MpbkpuLib.FileManagers.Sftp)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager("sftp://host/backups", out _, "user", "secret");
        Assert.That(sftp.Credentials?.UserName, Is.EqualTo("user"));
        Assert.That(sftp.Port, Is.EqualTo(22));

        using var sftpUrl = (MpbkpuLib.FileManagers.Sftp)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager("sftp://me:keyfile=/home/me/key@host:2222/x", out _);
        Assert.That(sftpUrl.PrivateKeyFile, Is.EqualTo("/home/me/key"));
        Assert.That(sftpUrl.Port, Is.EqualTo(2222));
    }

    [Test]
    public void SmbAddressesAreParsed()
    {
        using var gui = (MpbkpuLib.FileManagers.Smb)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(
            "smb:///nas/share/backups", out var guiPath, "OFFICE\\john", "secret");
        Assert.That(gui.Host, Is.EqualTo("nas"));
        Assert.That(guiPath, Is.EqualTo("smb://nas/share/backups"));
        Assert.That(GetField(gui, "_domainName"), Is.EqualTo("OFFICE"));
        Assert.That(GetField(gui, "_username"), Is.EqualTo("john"));

        using var url = (MpbkpuLib.FileManagers.Smb)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(
            "smb://john:secret@nas/share", out var urlPath);
        Assert.That(url.Host, Is.EqualTo("nas"));
        Assert.That(urlPath, Is.EqualTo("smb://nas/share"));
        Assert.That(GetField(url, "_domainName"), Is.Empty);
        Assert.That(GetField(url, "_password"), Is.EqualTo("secret"));

        using var domain = (MpbkpuLib.FileManagers.Smb)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(
            "smb://OFFICE\\john:secret@nas/share/x", out _);
        Assert.That(GetField(domain, "_domainName"), Is.EqualTo("OFFICE"));
        Assert.That(GetField(domain, "_username"), Is.EqualTo("john"));

        static object? GetField(object o, string name) =>
            o.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(o);
    }

    [TestCase("mega://user@mail.com:secret/backups", "mega://user@mail.com:***/backups")]
    [TestCase("\"Destination\": \"mediafire://user:secret\"", "\"Destination\": \"mediafire://user:***\"")]
    [TestCase("ftp://user:p4ss@host:21/path", "ftp://user:***@host:21/path")]
    [TestCase("sftp://user:keyfile=c:\\key@host/path", "sftp://user:***@host/path")]
    [TestCase("ftp://host:21/path", "ftp://host:21/path")]
    [TestCase("C:\\data\\source", "C:\\data\\source")]
    public void CredentialsAreMasked(string text, string expected)
    {
        Assert.That(BaseJob.MaskCredentials(text), Is.EqualTo(expected));
    }
}
