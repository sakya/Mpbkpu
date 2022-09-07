using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MpbkpuLib.FileManagers;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Tests.FileManagers;

public class Tests
{
    private Settings? _settings;

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

    [SetUp]
    public void Setup()
    {
        using (var fs = new FileStream("../../../../filemanagers-settings.json", FileMode.Open, FileAccess.Read)) {
            using (var sr = new StreamReader(fs)) {
                _settings = JsonConvert.DeserializeObject<Settings>(sr.ReadToEnd());
                if (_settings == null)
                    throw new Exception("Cannot read file managers settings");
                if (_settings.FileManagers == null)
                    throw new Exception("No file manager defined in settings");
            }
        }
    }

    [Test]
    public async Task CreateDirectory()
    {
        foreach (var kvp in _settings!.FileManagers!) {
            var settings = kvp.Value;
            using var fm = FileManagerFactory.GetFileManager(settings.Path, out var path, settings.Username,
                settings.Password, settings.OAuth2Settings);

            if (!await fm.DirectoryExists(path))
                await fm.CreateDirectory(path);
            await fm.CreateDirectory(fm.CombinePath(path, "TestCreate"));
        }
        Assert.Pass();
    }
}