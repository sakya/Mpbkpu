using System.Buffers;
using System.IO.Pipelines;

namespace MpbkpuLib.Utility;

/// <summary>
/// Write side of an upload done by an API that reads its data from a stream.
/// The job writes to this stream, the upload reads from <see cref="ReadStream"/>; the pipe keeps a bounded buffer.
/// Call <see cref="CompleteAsync"/> when the job has written all the data.
/// </summary>
public sealed class UploadStream : Stream
{
    #region classes
    /// <summary>
    /// Read side given to the upload API: it reports the declared length
    /// </summary>
    private sealed class PipeReadStream : Stream
    {
        private readonly Stream _inner;
        private long _position;

        public PipeReadStream(PipeReader reader, long length)
        {
            _inner = reader.AsStream(true);
            Length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length { get; }

        public override long Position
        {
            get => _position;
            set
            {
                if (value != _position)
                    throw new NotSupportedException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, (int)Math.Min(count, Length - _position));
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => Length + offset
            };
            Position = target;
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    #endregion

    private readonly Pipe _pipe = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _uploadTask;
    private long _written;
    private bool _completed;

    /// <param name="length">Number of bytes that will be written</param>
    /// <param name="upload">The upload: it reads from the given stream until the declared length</param>
    public UploadStream(long length, Func<Stream, CancellationToken, Task> upload)
    {
        Length = length;
        var readStream = new PipeReadStream(_pipe.Reader, length);
        _uploadTask = Task.Run(() => upload(readStream, _cts.Token));
        // When the upload stops (also on error) the writer must not wait for a reader anymore
        _uploadTask.ContinueWith(t => _pipe.Reader.Complete(t.Exception?.InnerException), TaskScheduler.Default);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length { get; }

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_completed)
            throw new ObjectDisposedException(nameof(UploadStream));
        if (_written + count > Length)
            throw new IOException($"Cannot write more than the declared {Length} bytes");

        FlushResult res;
        try {
            _pipe.Writer.Write(buffer.AsSpan(offset, count));
            _written += count;
            res = _pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
        } catch (Exception ex) when (ex is not IOException) {
            // The pipe rethrows the error of the failed upload
            throw new IOException("The upload failed", ex);
        }
        if (res.IsCompleted && _written < Length)
            throw new IOException("The upload stopped before receiving all the data", _uploadTask.Exception?.InnerException);
    }

    /// <summary>
    /// Wait for the end of the upload. If less data than declared was written the upload is cancelled.
    /// </summary>
    /// <exception cref="IOException">The upload is incomplete</exception>
    public async Task CompleteAsync()
    {
        if (_completed)
            return;
        _completed = true;

        if (_written != Length) {
            // Never upload a truncated file
            await _cts.CancelAsync();
            await _pipe.Writer.CompleteAsync(new OperationCanceledException());
            try {
                await _uploadTask;
            } catch {
                // The upload has been cancelled
            }
            throw new IOException($"Upload incomplete: {_written} of {Length} bytes written");
        }

        await _pipe.Writer.CompleteAsync();
        await _uploadTask;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _cts.Dispose();
        base.Dispose(disposing);
    }
}
