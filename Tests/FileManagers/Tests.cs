using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Tests.FileManagers;

public class Tests : BaseTest
{
    private Settings? _settings;
    private string? _tempFile1;
    private long _tempFile1Size;
    private string? _tempFile2;
    private long _tempFile2Size;

    public class Settings
    {
        public class FileManagerSetting
        {
            public string? Path { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
            public OAuth2Settings? OAuth2Settings { get; set; }
        }

        public Dictionary<string, FileManagerSetting>? FileManagers { get; set; }
    }

    [OneTimeSetUp]
    public void Setup()
    {
        // Load settings
        using (var fs = new FileStream("../../../../filemanagers-settings.json", FileMode.Open, FileAccess.Read)) {
            using (var sr = new StreamReader(fs)) {
                _settings = JsonConvert.DeserializeObject<Settings>(sr.ReadToEnd());
                if (_settings == null)
                    throw new Exception("Cannot read file managers settings");
                if (_settings.FileManagers == null)
                    throw new Exception("No file manager defined in settings");
            }
        }

        // Create temp files
        var minSize = 64 * 1024;
        var maxSize = 5 * 1024 * 1024;

        _tempFile1 = MpbkpuLib.Utility.TempFile.GetTempFileName();
        _tempFile1Size = Random.Next(minSize, maxSize);
        CreateFile(_tempFile1!, _tempFile1Size);

        _tempFile2 = MpbkpuLib.Utility.TempFile.GetTempFileName();
        _tempFile2Size = Random.Next(minSize, maxSize);
        CreateFile(_tempFile2!, _tempFile2Size);
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        DoTest(async (fm, path) =>
        {
            if (await fm.DirectoryExists(path))
                await fm.DeleteDirectory(path);
        }).Wait();
    }

    [Test]
    public async Task Test01CreateDirectory()
    {
        await DoTest(async (fm, path) =>
        {
            await fm.CreateDirectory(fm.CombinePath(path, "TestCreate"));
        });

        Assert.Pass();
    }

    [Test]
    public async Task Test02DeleteDirectory()
    {
        await DoTest(async (fm, path) =>
        {
            await fm.CreateDirectory(fm.CombinePath(path, "TestDelete"));
            await fm.DeleteDirectory(fm.CombinePath(path, "TestDelete"));
        });

        Assert.Pass();
    }

    [Test]
    public async Task Test03DirectoryExists()
    {
        await DoTest(async (fm, path) =>
        {
            if (!await fm.DirectoryExists(path))
                Assert.Fail();
            if (await fm.DirectoryExists(fm.CombinePath(path, "DoesNotExist")))
                Assert.Fail();
        });

        Assert.Pass();
    }

    [Test]
    public async Task Test04CopyFile()
    {
        await DoTest(async (fm, path) =>
        {
            await using (var fs = new FileStream(_tempFile1!, FileMode.Open, FileAccess.Read)) {
                await using (var fsStream = await fm.OpenFile(fm.CombinePath(path, "TempFile1"), true, _tempFile1Size)) {
                    await fs.CopyToAsync(fsStream);
                }
            }

            await using (var fs = new FileStream(_tempFile2!, FileMode.Open, FileAccess.Read)) {
                await using (var fsStream = await fm.OpenFile(fm.CombinePath(path, "TempFile2"), true, _tempFile2Size)) {
                    await fs.CopyToAsync(fsStream);
                }
            }
        });

        Assert.Pass();
    }

    [Test]
    public async Task Test05FileExists()
    {
        await DoTest(async (fm, path) =>
        {
            if (!await fm.FileExists(fm.CombinePath(path, "TempFile1")))
                Assert.Fail();
            if (await fm.FileExists(fm.CombinePath(path, "DoesNotExist")))
                Assert.Fail();
        });

        Assert.Pass();
    }

    [Test]
    public async Task Test05GetFileInfo()
    {
        await DoTest(async (fm, path) =>
        {
            var info = await fm.GetFileInfo(fm.CombinePath(path, "TempFile1"));
            if (info.Size != _tempFile1Size)
                Assert.Fail();
            info = await fm.GetFileInfo(fm.CombinePath(path, "TempFile2"));
            if (info.Size != _tempFile2Size)
                Assert.Fail();
        });

        Assert.Pass();
    }

    [Test]
    public async Task Test06GetDirectoryContent()
    {
        await DoTest(async (fm, path) =>
        {
            var content = fm.GetDirectoryContent(path);
            await foreach (var item in content) {

            }
        });

        Assert.Pass();
    }

    [Test]
    public async Task Test07RenameFile()
    {
        await DoTest(async (fm, path) =>
        {
            await fm.RenameFile(fm.CombinePath(path, "TempFile1"), "RenamedFile");
            if (!await fm.FileExists(fm.CombinePath(path, "RenamedFile")))
                Assert.Fail();
        });

        Assert.Pass();
    }

    [Test]
    public async Task Test08DeleteFile()
    {
        await DoTest(async (fm, path) =>
        {
            await fm.DeleteFile(fm.CombinePath(path, "TempFile2"));
            if (await fm.FileExists(fm.CombinePath(path, "TempFile2")))
                Assert.Fail();
        });

        Assert.Pass();
    }

    private async Task DoTest(Func<IFileManager, string, Task> action)
    {
        foreach (var kvp in _settings!.FileManagers!) {
            var settings = kvp.Value;
            using var fm = FileManagerFactory.GetFileManager(settings.Path, out var path, settings.Username,
                settings.Password, settings.OAuth2Settings);

            if (!await fm.DirectoryExists(path))
                await fm.CreateDirectory(path);

            await action.Invoke(fm, path);
        }
    }
}