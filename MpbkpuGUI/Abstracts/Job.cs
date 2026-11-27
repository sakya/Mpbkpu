using System;
using System.Collections.Generic;
using System.Linq;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;
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
            Smb,
            // Saved as numbers in settings.json: new types go at the end
            PCloud
        }

        /// <summary>
        /// Stable identifier: the backup database records the destination of each volume with it
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public FileSystemTypes Type { get; set; }
        public string? Path { get; set; }
        public string? User { get; set; }
        [JsonIgnore]
        public string? Password { get; set; }

        public Uri? OauthServerUrl { get; set; }
        public Uri? OauthRedirectUrl { get; set; }
        public string? OauthAppName { get; set; }
        public string? OauthAppId { get; set; }
        [JsonIgnore]
        public string? OauthAppSecret { get; set; }

        /// <summary>
        /// True if the settings file contained plain-text secrets written by an older version
        /// </summary>
        [JsonIgnore]
        public bool HasLegacySecrets { get; private set; }

        // Plain-text secrets written by older versions: read once, then moved to the secret store
        [JsonProperty(nameof(Password))]
        private string? LegacyPassword
        {
            set { Password = value; HasLegacySecrets = true; }
        }

        [JsonProperty(nameof(OauthAppSecret))]
        private string? LegacyOauthAppSecret
        {
            set { OauthAppSecret = value; HasLegacySecrets = true; }
        }

        /// <summary>
        /// Last space read from the position (remote positions are read only when asked or during a run)
        /// </summary>
        public StorageSpace? Space { get; set; }
        public DateTime? SpaceRead { get; set; }

        /// <summary>
        /// True for the local file system
        /// </summary>
        [JsonIgnore]
        public bool IsLocal => Type == FileSystemTypes.FileSystem;

        /// <summary>
        /// The position as a destination of a backup
        /// </summary>
        public BackupDestination ToBackupDestination()
        {
            return new BackupDestination()
            {
                Id = Id,
                Path = FullPath,
                User = User,
                Password = Password,
                OauthSettings = GetOAuth2Settings(),
            };
        }

        public OAuth2Settings GetOAuth2Settings()
        {
            return new OAuth2Settings()
            {
                AppName = OauthAppName,
                AppId = OauthAppId,
                AppSecret = OauthAppSecret,
                ServerUrl = OauthServerUrl,
                RedirectUrl = OauthRedirectUrl
            };
        }

        /// <summary>
        /// Create the file manager of this position
        /// </summary>
        /// <param name="path">The path to use with the file manager</param>
        public IFileManager GetFileManager(out string path)
        {
            return FileManagerFactory.GetFileManager(FullPath, out path, User, Password, GetOAuth2Settings());
        }

        public void GetSecrets(string prefix, IDictionary<string, string> secrets)
        {
            if (!string.IsNullOrEmpty(Password))
                secrets[$"{prefix}.{nameof(Password)}"] = Password;
            if (!string.IsNullOrEmpty(OauthAppSecret))
                secrets[$"{prefix}.{nameof(OauthAppSecret)}"] = OauthAppSecret;
        }

        public void SetSecrets(string prefix, IReadOnlyDictionary<string, string> secrets)
        {
            if (secrets.TryGetValue($"{prefix}.{nameof(Password)}", out var password))
                Password = password;
            if (secrets.TryGetValue($"{prefix}.{nameof(OauthAppSecret)}", out var appSecret))
                OauthAppSecret = appSecret;
        }

        [JsonIgnore]
        public string FullPath
        {
            get
            {
                var scheme = GetScheme(Type, out var pathSeparator);
                var res = scheme;
                if (Type is FileSystemTypes.OneDrive or FileSystemTypes.DropBox or FileSystemTypes.Nextcloud or FileSystemTypes.PCloud)
                    res = $"{res}{User}";
                if (Path?.StartsWith($"{pathSeparator}") == false)
                    res = $"{res}{pathSeparator}";
                return $"{res}{Path}";
            }
        }

        /// <summary>
        /// True when backups to this type of position can be compressed (the file manager accepts files of unknown size)
        /// </summary>
        public static bool SupportsCompression(FileSystemTypes type)
        {
            using var fm = CreatePlaceholder(type);
            return fm?.SupportCompression ?? true;
        }

        private static string? GetScheme(FileSystemTypes type, out char? pathSeparator)
        {
            using var fm = CreatePlaceholder(type);
            pathSeparator = fm?.PathSeparator;
            return fm?.GetScheme();
        }

        /// <summary>
        /// A file manager of the type with placeholder credentials, to read its properties; null for the local file system
        /// </summary>
        private static IFileManager? CreatePlaceholder(FileSystemTypes type)
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
                case FileSystemTypes.PCloud:
                    fm = new PCloud("account");
                    break;
                case FileSystemTypes.MediaFire:
                    fm = new MediaFire("account", "account");
                    break;
                case FileSystemTypes.OneDrive:
                    fm = new OneDrive("account");
                    break;
            }

            return fm;
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
    /// <summary>
    /// Files and directories to skip: names, relative paths or wildcard patterns (CLI --exclude)
    /// </summary>
    public List<string> Exclude { get; set; } = new();
    public DateTime? LastRun { get; set; }
    public MpbkpuLib.DataModel.Backup.Results? LastResult { get; set; }

    /// <summary>
    /// Every destination of the job
    /// </summary>
    [JsonIgnore]
    public virtual IReadOnlyList<Position> Destinations => Destination == null ? [] : [Destination];

    /// <summary>
    /// The destinations as the jobs of the library take them
    /// </summary>
    public List<BackupDestination> GetBackupDestinations()
    {
        return Destinations.Select(d => d.ToBackupDestination()).ToList();
    }

    /// <summary>
    /// "source → destination", with the number of the other destinations
    /// </summary>
    public string GetPathsText()
    {
        var res = $"{Source?.FullPath}  →  {Destination?.FullPath}";
        if (Destinations.Count > 1)
            res += $"  {string.Format(Localizer.Localizer.Instance["MoreDestinationsCount"], Destinations.Count - 1)}";
        return res;
    }

    /// <summary>
    /// The CLI command that runs this job, without secrets
    /// </summary>
    /// <param name="executable">The CLI executable</param>
    public abstract MpbkpuLib.Utility.CommandLine.Result GetCommandLine(string executable);

    public Job Copy()
    {
        var settings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.Objects,
            NullValueHandling = NullValueHandling.Ignore
        };
        var objectSerialized = JsonConvert.SerializeObject(this, settings);
        var res = JsonConvert.DeserializeObject<Job>(objectSerialized, settings)!;
        // Secrets are not serialized
        var secrets = new Dictionary<string, string>();
        GetSecrets(secrets);
        res.SetSecrets(secrets);
        return res;
    }

    [JsonIgnore]
    public bool HasLegacySecrets => Source?.HasLegacySecrets == true || Destination?.HasLegacySecrets == true;

    public virtual void GetSecrets(IDictionary<string, string> secrets)
    {
        Source?.GetSecrets($"{Id}.{nameof(Source)}", secrets);
        Destination?.GetSecrets($"{Id}.{nameof(Destination)}", secrets);
    }

    public virtual void SetSecrets(IReadOnlyDictionary<string, string> secrets)
    {
        Source?.SetSecrets($"{Id}.{nameof(Source)}", secrets);
        Destination?.SetSecrets($"{Id}.{nameof(Destination)}", secrets);
    }
}