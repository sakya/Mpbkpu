using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

/// <summary>
/// A scheduled job in the schedule page
/// </summary>
public class ScheduledJobRow
{
    public required Job Job { get; init; }
    public string? Name => Job.Name;
    public required string TypeText { get; init; }
    /// <summary>
    /// Days and time
    /// </summary>
    public required string ScheduleText { get; init; }
    public string? NextRunText { get; init; }
    public Backup.Results? LastResult => Job.LastResult;
    /// <summary>
    /// When the job last ran and whether automatically, null when it never ran
    /// </summary>
    public string? LastRunText { get; init; }
    /// <summary>
    /// Why the last automatic run failed
    /// </summary>
    public string? ErrorText { get; init; }
    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public bool HasLog { get; init; }
}
