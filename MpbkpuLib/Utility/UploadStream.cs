using System.Buffers;
using System.IO.Pipelines;

namespace MpbkpuLib.Utility;

/// <summary>
/// Write side of an upload done by an API that reads its data from a stream.
/// The job writes to this stream, the upload reads from the pipe, which keeps a bounded buffer.
/// Call <see cref="CompleteAsync"/> when the job has written all the data. With an unknown length (compressed files)
/// the end of the data is told by <see cref="EndOfData"/>: completing without it cancels the upload, so an
/// interrupted copy never becomes a truncated file.
/// </summary>
public sealed class UploadStream : Stream
{
    #region classes
    /// <summary>
    /// Read side given to the upload API: it reports the declared length, if known (not seekable otherwise)
    /// </summary>
    private sealed class PipeReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long? _length;
        private long _position;

        public PipeReadStream(PipeReader reader, long? length)
        {
            _inner = reader.AsStream(true);
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => _length != null;
        public override bool CanWrite => false;
        public override long Length => _length ?? throw new NotSupportedException();

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
            var read = _inner.Read(buffer, offset, _length == null ? count : (int)Math.Min(count, _length.Value - _position));
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
    private readonly long? _length;
    private long _written;
    private bool _completed;
    private bool _endOfData;

    /// <param name="length">Number of bytes that will be written, null when unknown</param>
    /// <param name="upload">The upload: it reads from the given stream until its end</param>
    public UploadStream(long? length, Func<Stream, CancellationToken, Task> upload)
    {
        _length = length;
        var readStream = new PipeReadStream(_pipe.Reader, length);
        _uploadTask = Task.Run(() => upload(readStream, _cts.Token));
        // When the upload stops (also on error) the writer must not wait for a reader anymore
        _uploadTask.ContinueWith(t => _pipe.Reader.Complete(t.Exception?.InnerException), TaskScheduler.Default);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length ?? _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_completed)
            throw new ObjectDisposedException(nameof(UploadStream));
        if (_written + count > _length)
            throw new IOException($"Cannot write more than the declared {_length} bytes");

        FlushResult res;
        try {
            _pipe.Writer.Write(buffer.AsSpan(offset, count));
            _written += count;
            res = _pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
        } catch (Exception ex) when (ex is not IOException) {
            // The pipe rethrows the error of the failed upload
            throw new IOException("The upload failed", ex);
        }
        if (res.IsCompleted && (_length == null || _written < _length))
            throw new IOException("The upload stopped before receiving all the data", _uploadTask.Exception?.InnerException);
    }

    /// <summary>
    /// All the data has been written: needed to complete an upload of unknown length
    /// </summary>
    public void EndOfData()
    {
        _endOfData = true;
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

        if (_length == null ? !_endOfData : _written != _length) {
            // Never upload a truncated file
            await _cts.CancelAsync();
            await _pipe.Writer.CompleteAsync(new OperationCanceledException());
            try {
                await _uploadTask;
            } catch {
                // The upload has been cancelled
            }
            throw new IOException(_length == null ?
                $"Upload incomplete: {_written} bytes written, the end of the data was not reached" :
                $"Upload incomplete: {_written} of {_length} bytes written");
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
