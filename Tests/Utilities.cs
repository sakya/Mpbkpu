using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public async Task UploadStreamOfUnknownLengthDeliversAllTheData()
    {
        var data = new byte[3 * 1024 * 1024 + 7];
        Random.Shared.NextBytes(data);
        var uploaded = new MemoryStream();
        var seekable = true;

        var stream = new UploadStream(null, (s, _) =>
        {
            // The upload API must not rely on a length (Dropbox sessions, chunked transfer encoding)
            seekable = s.CanSeek;
            return s.CopyToAsync(uploaded);
        });
        for (var offset = 0; offset < data.Length; offset += 32768)
            await stream.WriteAsync(data.AsMemory(offset, Math.Min(32768, data.Length - offset)));
        Assert.That(stream.Length, Is.EqualTo(data.Length));
        stream.EndOfData();
        await stream.CompleteAsync().WaitAsync(Timeout);

        Assert.That(seekable, Is.False);
        Assert.That(uploaded.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public async Task UploadStreamOfUnknownLengthIsCancelledWithoutEndOfData()
    {
        // An interrupted copy (the stream closed without EndOfData) must not become a truncated file
        var cancelled = false;
        var stream = new UploadStream(null, async (s, token) =>
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
        var fileLinks = true;
        try {
            File.CreateSymbolicLink(Path.Combine(root, "brokenLink"), Path.Combine(root, "missing"));
            File.CreateSymbolicLink(Path.Combine(root, "fileLink"), file);
        } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            // File symbolic links need a privilege on Windows (Developer Mode or administrator): not tested without it
            fileLinks = false;
        }

        var items = new List<MpbkpuLib.DataModel.File>();
        await foreach (var item in fm.GetDirectoryContent(root))
            items.Add(item);
        Assert.That(items.Select(i => i.Name), Is.EquivalentTo(fileLinks ? ["dir", "file", "fileLink"] : new[] { "dir", "file" }));
        if (fileLinks) {
            // A link to a file is followed, a broken one is skipped
            var link = items.Single(i => i.Name == "fileLink");
            Assert.That(link.Size, Is.EqualTo(new FileInfo(file).Length));
            Assert.That(link.LastModified, Is.EqualTo(File.GetLastWriteTimeUtc(file)));
            Assert.That((await fm.GetFileInfo(link.FullPath)).Size, Is.EqualTo(link.Size));
            Assert.That(async () => await fm.GetFileInfo(Path.Combine(root, "brokenLink")), Throws.TypeOf<FileNotFoundException>());
        }

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
        Assert.That(sftpUrl.PrivateKeyPassphrase, Is.Null);
        Assert.That(sftpUrl.Port, Is.EqualTo(2222));
    }

    [TestCase("onedrive://me@live.com/Backups", typeof(MpbkpuLib.FileManagers.OneDrive), "onedrive://Backups")]
    [TestCase("dropbox://me@mail.com/Backups/Pc", typeof(MpbkpuLib.FileManagers.Dropbox), "dropbox://Backups/Pc")]
    [TestCase("nextcloud://me/Backups", typeof(MpbkpuLib.FileManagers.Nextcloud), "nextcloud://Backups")]
    [TestCase("pcloud://me@mail.com/Backups", typeof(MpbkpuLib.FileManagers.PCloud), "pcloud://Backups")]
    [TestCase("pcloud://me@mail.com/", typeof(MpbkpuLib.FileManagers.PCloud), "pcloud:///")]
    public void AccountPathsKeepThePositionWithoutTheAccount(string path, Type type, string position)
    {
        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(path, out var newPosition);
        Assert.That(fm, Is.TypeOf(type));
        Assert.That(newPosition, Is.EqualTo(position));
    }

    [Test]
    public void PCloudDatesAndServers()
    {
        Assert.That(MpbkpuLib.FileManagers.PCloud.ParseDate("Sun, 16 Mar 2014 17:26:04 +0000"),
            Is.EqualTo(new DateTime(2014, 3, 16, 17, 26, 4, DateTimeKind.Utc)));
        Assert.That(MpbkpuLib.FileManagers.PCloud.ParseDate("Sun, 16 Mar 2014 17:26:04 +0100"),
            Is.EqualTo(new DateTime(2014, 3, 16, 16, 26, 4, DateTimeKind.Utc)));
        Assert.That(MpbkpuLib.FileManagers.PCloud.ParseDate(null), Is.Null);

        Assert.That(MpbkpuLib.FileManagers.PCloud.GetApiHost("eapi.pcloud.com", "2"), Is.EqualTo("eapi.pcloud.com"));
        Assert.That(MpbkpuLib.FileManagers.PCloud.GetApiHost(null, "2"), Is.EqualTo("eapi.pcloud.com"));
        Assert.That(MpbkpuLib.FileManagers.PCloud.GetApiHost(null, null), Is.EqualTo("api.pcloud.com"));
        // The token is never sent to an unknown server
        Assert.That(() => MpbkpuLib.FileManagers.PCloud.GetApiHost("evil.example.com", "1"), Throws.Exception);
    }

    [Test]
    public void SmbDatesAreUtc()
    {
        var utc = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
        Assert.That(MpbkpuLib.FileManagers.Smb.ToUtc(utc), Is.EqualTo(utc));
        Assert.That(MpbkpuLib.FileManagers.Smb.ToUtc(utc.ToLocalTime())!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(MpbkpuLib.FileManagers.Smb.ToUtc(utc.ToLocalTime()), Is.EqualTo(utc));
        Assert.That(MpbkpuLib.FileManagers.Smb.ToUtc(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified))!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(MpbkpuLib.FileManagers.Smb.ToUtc(null), Is.Null);
    }

    [Test]
    public void FtpAndSftpReadThePathOptions()
    {
        using var ftp = (MpbkpuLib.FileManagers.Ftp)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(
            "ftp:///host:2121/backups?tls=required&fingerprint=ab:cd:01", out var ftpPath, "user", "secret");
        Assert.That(ftp.RequireTls, Is.True);
        Assert.That(MpbkpuLib.FileManagers.Ftp.NormalizeFingerprint(ftp.TrustedFingerprint), Is.EqualTo("ABCD01"));
        Assert.That(ftp.Host, Is.EqualTo("host"));
        Assert.That(ftp.Port, Is.EqualTo(2121));
        // The options are not part of the position the jobs use
        Assert.That(ftpPath, Is.EqualTo("/backups"));

        using var plain = (MpbkpuLib.FileManagers.Ftp)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager("ftp://host/backups", out _, "user", "secret");
        Assert.That(plain.RequireTls, Is.False);
        Assert.That(plain.TrustedFingerprint, Is.Null);

        using var sftp = (MpbkpuLib.FileManagers.Sftp)MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(
            "sftp://me:keyfile=/home/me/key;passphrase=p;w@host/x?hostkey=ssh-key", out var sftpPath);
        Assert.That(sftp.PrivateKeyFile, Is.EqualTo("/home/me/key"));
        Assert.That(sftp.PrivateKeyPassphrase, Is.EqualTo("p;w"));
        Assert.That(sftp.AcceptedHostKey, Is.EqualTo("ssh-key"));
        Assert.That(sftpPath, Is.EqualTo("/x"));
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

    [Test]
    public void EtaFollowsTheRecentSpeedAndCountsItems()
    {
        const long mb = 1024 * 1024;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // No estimate during the warm-up
        var eta = new EtaEstimator(1000 * mb, 0, EtaEstimator.LocalItemCost, () => now);
        now = now.AddSeconds(1);
        Assert.That(eta.Update(mb, 0), Is.Null);

        // 1 MB/s for 60 s, then 10 MB/s for 30 s: the estimate uses the recent speed
        long done = mb;
        for (var s = 2; s <= 60; s++) {
            now = now.AddSeconds(1);
            done += mb;
            eta.Update(done, 0);
        }
        Assert.That(eta.Update(done, 0)!.Value.TotalSeconds, Is.EqualTo((1000 - 60) / 1.0).Within(1));
        for (var s = 0; s < 30; s++) {
            now = now.AddSeconds(1);
            done += 10 * mb;
            eta.Update(done, 0);
        }
        Assert.That(eta.Update(done, 0)!.Value.TotalSeconds, Is.EqualTo((1000 - 360) / 10.0).Within(2));

        // Items without bytes (empty files, deletions) are estimated by count
        now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var items = new EtaEstimator(0, 1000, EtaEstimator.RemoteItemCost, () => now);
        now = now.AddSeconds(10);
        Assert.That(items.Update(0, 100)!.Value.TotalSeconds, Is.EqualTo(90).Within(0.5));
    }

    [Test]
    public async Task LocalAvailableSpaceIsTheFreeSpaceOfTheDrive()
    {
        var root = CreateTempDirectory();
        using var fm = MpbkpuLib.FileManagers.FileManagerFactory.GetFileManager(root, out _);
        var space = await fm.GetSpace(root);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        Directory.Delete(root);

        Assert.That(space, Is.Not.Null);
        // Other processes may write in the meantime
        Assert.That(space!.Available, Is.EqualTo(drive.AvailableFreeSpace).Within(100 * 1024 * 1024));
        Assert.That(space.Total, Is.EqualTo(drive.TotalSize));
        Assert.That(space.UsedPercentage, Is.InRange(0, 100));
    }

    [Test]
    public void NextcloudPropertiesComeFromTheFoundPropStat()
    {
        // WebDAV returns one propstat per status: found properties (200) and missing ones (404)
        const string xml = """
            <?xml version="1.0"?>
            <d:multistatus xmlns:d="DAV:" xmlns:oc="http://owncloud.org/ns" xmlns:nc="http://nextcloud.org/ns">
              <d:response>
                <d:href>/remote.php/dav/files/john/a.txt</d:href>
                <d:propstat>
                  <d:prop><d:getcontentlength>10</d:getcontentlength><d:quota-available-bytes>2048</d:quota-available-bytes></d:prop>
                  <d:status>HTTP/1.1 200 OK</d:status>
                </d:propstat>
                <d:propstat>
                  <d:prop><nc:contained-file-count /></d:prop>
                  <d:status>HTTP/1.1 404 Not Found</d:status>
                </d:propstat>
              </d:response>
            </d:multistatus>
            """;
        var ser = new System.Xml.Serialization.XmlSerializer(typeof(NextcloudClient.Models.ItemInfo), "DAV:");
        using var sr = new StringReader(xml);
        var info = (NextcloudClient.Models.ItemInfo)ser.Deserialize(sr)!;

        var props = info.Response[0].PropStat.Properties;
        Assert.That(props.ContentLength, Is.EqualTo(10));
        Assert.That(props.QuotaAvailableBytes, Is.EqualTo(2048));
        Assert.That(props.ContainedFileCount, Is.Null);
    }

    [TestCase("mega://user@mail.com:secret/backups", "mega://user@mail.com:***/backups")]
    [TestCase("\"Destination\": \"mediafire://user:secret\"", "\"Destination\": \"mediafire://user:***\"")]
    [TestCase("ftp://user:p4ss@host:21/path", "ftp://user:***@host:21/path")]
    [TestCase("sftp://user:keyfile=c:\\key@host/path", "sftp://user:***@host/path")]
    [TestCase("sftp://user:keyfile=c:\\key;passphrase=secret@host/path", "sftp://user:***@host/path")]
    [TestCase("ftp://host:21/path", "ftp://host:21/path")]
    [TestCase("C:\\data\\source", "C:\\data\\source")]
    public void CredentialsAreMasked(string text, string expected)
    {
        Assert.That(BaseJob.MaskCredentials(text), Is.EqualTo(expected));
    }
}
