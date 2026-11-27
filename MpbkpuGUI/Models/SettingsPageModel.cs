using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Models;

/// <summary>
/// The update section of the settings page
/// </summary>
public class SettingsPageModel : BaseModel
{
    private string? _updateStatus;
    public string? UpdateStatus
    {
        get => _updateStatus;
        set => SetField(ref _updateStatus, value);
    }

    private bool _isBusy;
    /// <summary>
    /// A check or a download is running
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    private bool _updateFound;
    public bool UpdateFound
    {
        get => _updateFound;
        set
        {
            if (SetField(ref _updateFound, value))
                OnPropertyChanged(nameof(CanInstall));
        }
    }

    /// <summary>
    /// This installation can be updated by the program (see UpdateChecker.CanInstall)
    /// </summary>
    public bool Installable { get; init; }

    /// <summary>
    /// The update can be downloaded and installed by the program
    /// </summary>
    public bool CanInstall => _updateFound && Installable;

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetField(ref _isDownloading, value);
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetField(ref _downloadProgress, value);
    }
}
