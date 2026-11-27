using System.Collections.Generic;
using System.IO.Compression;
using Newtonsoft.Json;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using MpbkpuLib.Utility;

namespace MpbkpuGUI.Models;

public class BackupJob : Job
{
    public Backup.BackupTypes? Type { get; set; }
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.NoCompression;
    public bool Encrypted { get; set; }
    public int FullBackupEveryDays { get; set; }
    public int KeepChains { get; set; }

    /// <summary>
    /// The destinations after the first one (<see cref="Job.Destination"/>): the files are split among all of them
    /// </summary>
    public List<Position> MoreDestinations { get; set; } = new();

    /// <summary>
    /// Maximum number of files copied at the same time
    /// </summary>
    public int ParallelFiles { get; set; } = 1;

    [JsonIgnore]
    public override IReadOnlyList<Position> Destinations => [..base.Destinations, ..MoreDestinations];

    /// <summary>
    /// The encryption password: never saved in the settings file, saved in the OS secure storage only when
    /// <see cref="RememberPassword"/> is set
    /// </summary>
    [JsonIgnore]
    public string? Password { get; set; }

    /// <summary>
    /// Save the encryption password in the OS secure storage
    /// </summary>
    public bool RememberPassword { get; set; }

    [JsonIgnore]
    public bool HasSavedPassword => Encrypted && RememberPassword && !string.IsNullOrEmpty(Password);

    public void ForgetPassword()
    {
        RememberPassword = false;
        Password = null;
    }

    private string EncryptPasswordKey => $"{Id}.EncryptPassword";

    private string MoreDestinationPrefix(Position destination) => $"{Id}.{nameof(Destination)}.{destination.Id}";

    public override void GetSecrets(IDictionary<string, string> secrets)
    {
        base.GetSecrets(secrets);
        foreach (var destination in MoreDestinations)
            destination.GetSecrets(MoreDestinationPrefix(destination), secrets);
        if (HasSavedPassword)
            secrets[EncryptPasswordKey] = Password!;
    }

    public override void SetSecrets(IReadOnlyDictionary<string, string> secrets)
    {
        base.SetSecrets(secrets);
        foreach (var destination in MoreDestinations)
            destination.SetSecrets(MoreDestinationPrefix(destination), secrets);
        if (RememberPassword && secrets.TryGetValue(EncryptPasswordKey, out var password))
            Password = password;
    }

    [JsonIgnore]
    public bool IsCompressed => CompressionLevel != CompressionLevel.NoCompression;

    /// <summary>
    /// The settings of the library job that runs this backup
    /// </summary>
    public BackupJobSettings CreateSettings()
    {
        return new BackupJobSettings()
        {
            BackupType = Type ?? Backup.BackupTypes.Full,
            EncryptPassword = Password,
            Source = Source!.FullPath,
            SourceUser = Source.User,
            SourcePassword = Source.Password,
            SourceOauthSettings = Source.GetOAuth2Settings(),

            Destinations = GetBackupDestinations(),
            ParallelFiles = ParallelFiles,
            Exclude = Exclude,

            SkipHashCheck = SkipHashCheck,
            RetryOnError = RetryOnError,
            CompressionLevel = CompressionLevel,
            FullBackupEveryDays = FullBackupEveryDays,
            KeepChains = KeepChains
        };
    }

    public override CommandLine.Result GetCommandLine(string executable)
    {
        return CommandLine.ForBackup(executable, CreateSettings(), Encrypted);
    }
}