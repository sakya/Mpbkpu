using System;
using Material.Icons;
using BaseModel = MpbkpuGUI.Abstracts.BaseModel;

namespace MpbkpuGUI.Models;

/// <summary>
/// A phase of a running job (a step of the library job), shown in the phase list of the running page
/// </summary>
public class JobPhase : BaseModel
{
    public enum States
    {
        Pending,
        Running,
        Done,
        /// <summary>
        /// The job did not need it (e.g. no old backups to delete)
        /// </summary>
        Skipped,
        Failed,
        Cancelled
    }

    /// <summary>
    /// A phase for a step of a library job, named with its localized text (Step_{key})
    /// </summary>
    public static JobPhase FromStep(string key, bool hasProgress = false, double weight = 1)
    {
        return new JobPhase()
        {
            Key = key,
            Name = Localizer.Localizer.Instance[$"Step_{key}"],
            HasProgress = hasProgress,
            Weight = weight
        };
    }

    /// <summary>
    /// The name of the library step
    /// </summary>
    public required string Key { get; init; }
    public required string Name { get; init; }
    /// <summary>
    /// True when the job reports the progress of this phase
    /// </summary>
    public bool HasProgress { get; init; }
    /// <summary>
    /// Relative width of the phase in the overall progress bar
    /// </summary>
    public double Weight { get; init; } = 1;
    public DateTime? Started { get; set; }

    private States _state;
    public States State
    {
        get => _state;
        set
        {
            if (!SetField(ref _state, value))
                return;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsInactive));
        }
    }

    public MaterialIconKind Icon => State switch
    {
        States.Running => MaterialIconKind.ProgressClock,
        States.Done => MaterialIconKind.CheckCircleOutline,
        States.Skipped => MaterialIconKind.MinusCircleOutline,
        States.Failed => MaterialIconKind.AlertCircleOutline,
        States.Cancelled => MaterialIconKind.CloseCircleOutline,
        _ => MaterialIconKind.CircleOutline
    };

    public bool IsRunning => State == States.Running;
    public bool IsDone => State == States.Done;
    public bool IsFailed => State == States.Failed;
    public bool IsInactive => State is States.Pending or States.Skipped or States.Cancelled;

    private string? _detail;
    /// <summary>
    /// Duration of a finished phase, "in progress" or "not needed"
    /// </summary>
    public string? Detail
    {
        get => _detail;
        set => SetField(ref _detail, value);
    }
}
