using System;
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
        public string FullPath
        {
            get
            {
                var scheme = GetScheme(Type, out var pathSeparator);
                var res = scheme;
                if (Type is FileSystemTypes.OneDrive or FileSystemTypes.DropBox or FileSystemTypes.Nextcloud)
                    res = $"{res}{User}";
                if (Path?.StartsWith($"{pathSeparator}") == false)
                    res = $"{res}{pathSeparator}";
                return $"{res}{Path}";
            }
        }

        private static string? GetScheme(FileSystemTypes type, out char? pathSeparator)
        {
            IFileManager? fm = null;
            switch (type) {
                case FileSystemTypes.Ftp:
                    fm = new Ftp();
                    break;
                case FileSystemTypes.Mega:
                    fm = new Mega("account", "account");
                    break;
                case FileSystemTypes.Nextcloud:
                    fm = new Nextcloud("account");
                    break;
                case FileSystemTypes.Sftp:
                    fm = new Sftp();
                    break;
                case FileSystemTypes.Smb:
                    fm = new Smb(string.Empty, string.Empty, string.Empty, string.Empty);
                    break;
                case FileSystemTypes.DropBox:
                    fm = new MpbkpuLib.FileManagers.Dropbox("account");
                    break;
                case FileSystemTypes.MediaFire:
                    fm = new MediaFire("account", "account");
                    break;
                case FileSystemTypes.OneDrive:
                    fm = new OneDrive("account");
                    break;
            }

            pathSeparator = fm?.GetPathSeparator();
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