using System;
using System.Collections.Generic;
using System.IO;
using Dropbox.Api.Sharing;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using Newtonsoft.Json;

namespace Tests.FileManagers;

public abstract  class BaseTest
{
    private Settings? _settings;
    private IFileManager? _fileManager;

    public class FileManagerSetting
    {
        public string? Path { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public OAuth2Settings? OAuth2Settings { get; set; }
    }

    public class Settings
    {
        public Dictionary<string, FileManagerSetting>? FileManagers { get; set; }
    }

    public BaseTest()
    {
        using (var fs = new FileStream("../../../../filemanager-settings.json", FileMode.Open, FileAccess.Read)) {
            using (var sr = new StreamReader(fs)) {
                _settings = JsonConvert.DeserializeObject<Settings>(sr.ReadToEnd());
                if (_settings == null)
                    throw new Exception("Cannot read file managers settings");
            }
        }
    }
}