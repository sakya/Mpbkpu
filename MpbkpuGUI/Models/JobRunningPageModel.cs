using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MpbkpuLib.Abstracts;

namespace MpbkpuGUI.Models;

public class JobRunningPageModel : INotifyPropertyChanged
{
    public class LogMessage
    {
        public BaseJobSettings.LogLevels Level { get; set; }
        public string? Message { get; set; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string? _step;
    public string? Step
    {
        get => _step;
        set
        {
            SetField(ref _step, value);
        }
    }

    private double? _progress;
    public double? Progress
    {
        get => _progress;
        set
        {
            SetField(ref _progress, value);
        }
    }

    private string? _progressMessageFiles;
    public string? ProgressMessageFiles
    {
        get => _progressMessageFiles;
        set
        {
            SetField(ref _progressMessageFiles, value);
        }
    }

    private string? _progressMessageBytes;
    public string? ProgressMessageBytes
    {
        get => _progressMessageBytes;
        set
        {
            SetField(ref _progressMessageBytes, value);
        }
    }

    private string? _progressMessageEta;
    public string? ProgressMessageEta
    {
        get => _progressMessageEta;
        set
        {
            SetField(ref _progressMessageEta, value);
        }
    }

    private ObservableCollection<LogMessage>? _log;
    public ObservableCollection<LogMessage>? Log
    {
        get => _log;
        set
        {
            SetField(ref _log, value);
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}