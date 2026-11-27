using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Models;

public class AboutPageModel : BaseModel
{
    private string? _title;
    public string? Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    private string? _copyright;
    public string? Copyright
    {
        get => _copyright;
        set => SetField(ref _copyright, value);
    }

    private string? _description;
    public string? Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

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