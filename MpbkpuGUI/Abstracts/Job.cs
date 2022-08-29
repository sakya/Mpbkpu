using System;
using Microsoft.Graph;
using MpbkpuGUI.Controls.Models;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using Newtonsoft.Json;

namespace MpbkpuGUI.Abstracts;

public abstract class Job
{
    public class Position
    {
        public enum FileSystemTypes
        {
            FileSystem,
            DropBox,
            Ftp,
            MediaFire,
            Mega,
            Nextcloud,
            OneDrive,
            Sftp,
            Smb
        }

        public FileSystemTypes Type { get; set; }
        public string? Path { get; set; }
        public string? User { get; set; }
        public string? Password { get; set; }

        public Uri? OauthServerUrl { get; set; }
        public Uri? OauthRedirectUrl { get; set; }
        public string? OauthAppName { get; set; }
        public string? OauthAppId { get; set; }
        public string? OauthAppSecret { get; set; }

        [JsonIgnore]
        public string? FullPath => $"{GetScheme(Type)}{Path}";

        private static string? GetScheme(Job.Position.FileSystemTypes type)
        {
            IFileManager? fm = null;
            switch (type) {
                case Job.Position.FileSystemTypes.Ftp:
                    fm = new Ftp();
                    break;
                case Job.Position.FileSystemTypes.Mega:
                    fm = new Mega("account", "account");
                    break;
                case Job.Position.FileSystemTypes.Nextcloud:
                    fm = new Nextcloud("account");
                    break;
                case Job.Position.FileSystemTypes.Sftp:
                    fm = new Sftp();
                    break;
                case Job.Position.FileSystemTypes.Smb:
                    fm = new Smb(string.Empty, string.Empty, string.Empty, string.Empty);
                    break;
                case Job.Position.FileSystemTypes.DropBox:
                    fm = new MpbkpuLib.FileManagers.Dropbox("account");
                    break;
                case Job.Position.FileSystemTypes.MediaFire:
                    fm = new MediaFire("account", "account");
                    break;
                case Job.Position.FileSystemTypes.OneDrive:
                    fm = new OneDrive("account");
                    break;
            }

            return fm?.GetScheme();
        }
    }

    protected Job()
    {
        Id = Guid.NewGuid().ToString("N");
        Source = new Position();
        Destination = new Position();
    }

    public string Id { get; set; }
    public string? Name { get; set; }
    public Position? Source { get; set; }
    public Position? Destination { get; set; }
    public bool SkipHashCheck { get; set; } = false;
    public int RetryOnError { get; set; } = 3;
    public DateTime? LastRun { get; set; }
    public string? LogFile { get; set; }

    public Job Copy()
    {
        var settings = new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Objects };
        var objectSerialized = JsonConvert.SerializeObject(this, settings);
        return JsonConvert.DeserializeObject<Job>(objectSerialized, settings)!;
    }
}