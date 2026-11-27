using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;

namespace MpbkpuLib.FileManagers;

public class FileSystemStream : Stream
{
    private readonly IFileManager _fileManager;
    private readonly Stream _baseStream;

    public FileSystemStream(IFileManager fileManager, Stream baseStream)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
    }

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => _baseStream.CanWrite;
    public override long Length => _baseStream.Length;

    public override long Position
    {
        get => _baseStream.Position;
        set => _baseStream.Position = value;
    }

    /// <summary>
    /// All the data has been written. Required for a file written with an unknown size (<see cref="UploadStream"/>):
    /// closing it without this call discards it.
    /// </summary>
    public void EndOfData()
    {
        if (_baseStream is UploadStream us)
            us.EndOfData();
    }

    protected override void Dispose(bool disposing)
    {
        _fileManager.CloseFile(_baseStream).Wait();
    }

    public override void Flush()
    {
        _baseStream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _baseStream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _baseStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _baseStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _baseStream.Write(buffer, offset, count);
    }
}