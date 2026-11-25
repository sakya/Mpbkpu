using MpbkpuLib.Jobs;

namespace MpbkpuLib.Abstracts;

/// <summary>
/// Settings of the jobs working on existing backups (list, restore, verify, delete)
/// </summary>
public abstract class BackupDestinationsJobSettings : BaseJobSettings
{
    /// <summary>
    /// The destinations holding the backups: the database is read from the newest copy and the files of each volume
    /// from the destination recorded for it
    /// </summary>
    public List<BackupDestination> BackupDestinations { get; set; } = new();
}
