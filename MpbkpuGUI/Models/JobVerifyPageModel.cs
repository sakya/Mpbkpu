using System.Collections.ObjectModel;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Models;

public class JobVerifyPageModel : BaseModel
{
    private bool _running;
    public bool Running
    {
        get => _running;
        set => SetField(ref _running, value);
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

    private ObservableCollection<JobRunningPageModel.LogMessage>? _log;
    public ObservableCollection<JobRunningPageModel.LogMessage>? Log
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