using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

public class VerifyJobSettings : BaseJobSettings
{
    /// <summary>
    /// The path containing the backup
    /// </summary>
    public string? BackupPath { get; set; }
    /// <summary>
    /// The user to access to the backup path
    /// </summary>
    /// <value></value>
    public string? BackupPathUser { get; set; }
    /// <summary>
    /// The password to access to the backup path
    /// </summary>
    /// <value></value>
    public string? BackupPathPassword { get; set; }
    /// <summary>
    /// OAuth settings to access to the backup path
    /// </summary>
    public OAuth2Settings? BackupOauthSettings { get; set; }

    /// <summary>
    /// The GUID of the backup to verify
    /// </summary>
    public string? BackupGuid { get; set; }
}