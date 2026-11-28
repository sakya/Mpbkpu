using System.Collections.ObjectModel;
using BaseModel = MpbkpuGUI.Abstracts.BaseModel;

namespace MpbkpuGUI.Models;

public class JobRunningPageModel : JobProgressModel
{
    private string? _source;
    public string? Source
    {
        get => _source;
        set => SetField(ref _source, value);
    }

    private string? _destination;
    public string? Destination
    {
        get => _destination;
        set => SetField(ref _destination, value);
    }

    /// <summary>
    /// A file being copied
    /// </summary>
    public class CopyingFile : BaseModel
    {
        public string? Path { get; init; }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetField(ref _progress, value);
        }
    }

    /// <summary>
    /// The files being copied: changed on the UI thread only
    /// </summary>
    public ObservableCollection<CopyingFile> CopyingFiles { get; } = new();

    private bool _hasCopyingFiles;
    public bool HasCopyingFiles
    {
        get => _hasCopyingFiles;
        set => SetField(ref _hasCopyingFiles, value);
    }
}
