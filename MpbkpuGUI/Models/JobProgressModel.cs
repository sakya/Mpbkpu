using System.Collections.ObjectModel;
using MpbkpuLib.Abstracts;
using BaseModel = MpbkpuGUI.Abstracts.BaseModel;

namespace MpbkpuGUI.Models;

/// <summary>
/// Progress, phase and log of a job shown by a page (run, verify, delete)
/// </summary>
public class JobProgressModel : BaseModel
{
    public class LogMessage
    {
        public BaseJobSettings.LogLevels Level { get; set; }
        public string? Message { get; set; }
    }

    private bool _running;
    public bool Running
    {
        get => _running;
        set
        {
            if (SetField(ref _running, value))
                OnPropertyChanged(nameof(ProgressIndeterminate));
        }
    }

    private string? _step;
    public string? Step
    {
        get => _step;
        set => SetField(ref _step, value);
    }

    private double? _progress;
    public double? Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    private string? _phaseText;
    /// <summary>
    /// "Phase N of M" while the job runs (<see cref="Controls.PhaseList"/>)
    /// </summary>
    public string? PhaseText
    {
        get => _phaseText;
        set => SetField(ref _phaseText, value);
    }

    private bool _phaseHasProgress = true;
    /// <summary>
    /// True when the current phase reports its progress: otherwise the percentage is hidden and the bar is indeterminate
    /// </summary>
    public bool PhaseHasProgress
    {
        get => _phaseHasProgress;
        set
        {
            if (SetField(ref _phaseHasProgress, value))
                OnPropertyChanged(nameof(ProgressIndeterminate));
        }
    }

    public bool ProgressIndeterminate => Running && !PhaseHasProgress;

    private string? _progressMessageFiles;
    public string? ProgressMessageFiles
    {
        get => _progressMessageFiles;
        set => SetField(ref _progressMessageFiles, value);
    }

    private string? _progressMessageBytes;
    public string? ProgressMessageBytes
    {
        get => _progressMessageBytes;
        set => SetField(ref _progressMessageBytes, value);
    }

    private string? _progressMessageEta;
    public string? ProgressMessageEta
    {
        get => _progressMessageEta;
        set => SetField(ref _progressMessageEta, value);
    }

    private ObservableCollection<LogMessage>? _log;
    public ObservableCollection<LogMessage>? Log
    {
        get => _log;
        set => SetField(ref _log, value);
    }

    private string? _buttonText;
    public string? ButtonText
    {
        get => _buttonText;
        set => SetField(ref _buttonText, value);
    }
}
