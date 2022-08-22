using Dropbox.Api.TeamLog;
using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class ListContentJobSettings : BaseJobSettings
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
    /// The GUID of the backup
    /// </summary>
    public string? BackupGuid { get; set; }

    /// <summary>
    /// Regular expression used to filter results
    /// </summary>
    public string? Filter { get; set; }
}