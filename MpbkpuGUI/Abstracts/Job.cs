using System;
using Microsoft.Graph;
using MpbkpuGUI.Controls.Models;
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