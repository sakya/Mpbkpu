using System.Collections.Generic;
using System.IO.Compression;
using Newtonsoft.Json;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupJob : Job
{
    public Backup.BackupTypes? Type { get; set; }
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.NoCompression;
    public bool Encrypted { get; set; }
    public int FullBackupEveryDays { get; set; }
    public int KeepChains { get; set; }

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

    public override void GetSecrets(IDictionary<string, string> secrets)
    {
        base.GetSecrets(secrets);
        if (HasSavedPassword)
            secrets[EncryptPasswordKey] = Password!;
    }

    public override void SetSecrets(IReadOnlyDictionary<string, string> secrets)
    {
        base.SetSecrets(secrets);
        if (RememberPassword && secrets.TryGetValue(EncryptPasswordKey, out var password))
            Password = password;
    }

    [JsonIgnore]
    public bool IsCompressed => CompressionLevel != CompressionLevel.NoCompression;
}