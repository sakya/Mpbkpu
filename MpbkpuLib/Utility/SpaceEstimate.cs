using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;

namespace MpbkpuLib.Utility;

/// <summary>
/// The space of a destination: read from its file manager and estimated while a job writes or deletes files
/// </summary>
public class SpaceEstimate
{
    private StorageSpace? _read;
    private long _used;

    /// <summary>
    /// The estimated space, null when the file system cannot report it
    /// </summary>
    public StorageSpace? Current
    {
        get
        {
            if (_read == null)
                return null;
            var available = Math.Max(0, _read.Available - _used);
            return _read with { Available = _read.Total.HasValue ? Math.Min(available, _read.Total.Value) : available };
        }
    }

    /// <summary>
    /// Read the space from the file manager, resetting the estimate
    /// </summary>
    public async Task Read(IFileManager fm, string path)
    {
        _read = await fm.GetSpace(path);
        _used = 0;
    }

    /// <summary>
    /// Bytes written (or deleted, with negative bytes) since the last <see cref="Read"/>
    /// </summary>
    public void Used(long bytes)
    {
        _used += bytes;
    }
}
