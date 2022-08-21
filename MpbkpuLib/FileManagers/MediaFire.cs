using MediaFireApi;
using MediaFireApi.Exceptions;
using MediaFireApi.Models;
using MpbkpuLib.Interfaces;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class MediaFire : IFileManager
{
    public const string Scheme = "mediafire://";

    #region classes

    private class ReaderStream : Stream
    {
        private HttpClient _client;
        private Stream _baseStream;
        public ReaderStream(string url)
        {
            _client = new HttpClient();
            var res = _client.GetAsync(new Uri(url));
            _baseStream = res.Result.Content.ReadAsStream();
        }

        protected override void Dispose(bool disposing)
        {
            _baseStream?.Dispose();
            _client?.Dispose();
        }

        public override void Flush()
        {
            throw new InvalidOperationException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _baseStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException();
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get; }
        public override long Position { get; set; }
    }

    private class WriterStream : Stream
    {
        private long _pos;

        private readonly List<byte> _buffer = new();
        private readonly SemaphoreSlim _bufferSema = new(1,1);

        public WriterStream(long length)
        {
            Length = length;
        }

        public Task? WriteTask { get; set; }
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length { get; }

        public override long Position {
            get => _pos;
            set => _pos = value;
        }

        protected override void Dispose(bool disposing)
        {
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= Length)
                return 0;

            _bufferSema.Wait();
            int readCount = 0;
            for (int i = 0; i < count; i++) {
                if (_buffer.Count > 0) {
                    buffer[offset + i] = _buffer[0];
                    _buffer.RemoveAt(0);

                    _pos++;
                    readCount++;
                    if (_pos >= Length)
                        break;
                } else {
                    _bufferSema.Release();
                    Thread.Sleep(10);
                    _bufferSema.Wait();
                    i--;
                }
            }
            _bufferSema.Release();
            return readCount;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return offset;
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            while(_buffer.Count > 0)
                Thread.Sleep(10);

            _bufferSema.Wait();
            for (int i = 0; i < count; i++) {
                _buffer.Add(buffer[offset + i]);
            }
            _bufferSema.Release();
        }

        public async Task WaitTask()
        {
            if (WriteTask != null)
                await WriteTask;
            WriteTask?.Dispose();
        }
    }
    #endregion

    private readonly string? _username;
    private readonly string? _password;
    private Client? _client;

    public MediaFire(string username, string password)
    {
        _username = username;
        _password = password;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    public string? GetScheme()
    {
        return Scheme;
    }

    public char GetPathSeparator()
    {
        return '/';
    }

    public bool CanOpenDatabase()
    {
        return false;
    }

    public bool SupportCompression()
    {
        return false;
    }

    public string CombinePath(params string[] paths)
    {
        return Utility.Files.CombinePath(GetPathSeparator(), paths);
    }

    public async Task<bool> CreateDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        var parentPath = GetParent(GetPath(path), out var childName);
        await client.FolderCreate(parentPath: parentPath, name: childName);
        return true;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var info = (await client.FolderGetInfo(folderPath: path))?.ToList();
        if (info?.Count == 1) {
            await client.FolderDelete(new []{ info[0].FolderKey });
            await client.FolderPurge( new []{ info[0].FolderKey });
            return true;
        }

        return false;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var info = (await client.FileGetInfo(filePath: path))?.ToList();
        if (info?.Count == 1) {
            await client.FileDelete(new []{ info[0].QuickKey });
            await client.FilePurge( new []{ info[0].QuickKey });
            return true;
        }

        return false;
    }

    public async Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        try {
            var info = (await client.FolderGetInfo(folderPath: path))?.ToList();
            return info?.Count == 1;
        } catch (MediaFireApiException ex) {
            if (ex.ErrorCode == (int)ErrorCodes.ErrorInvalidFolderPath)
                return false;

            throw;
        }
    }

    public async Task<bool> FileExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        try {
            var info = (await client.FileGetInfo(filePath: path))?.ToList();
            return info?.Count == 1;
        } catch (MediaFireApiException ex) {
            if (ex.ErrorCode == (int)ErrorCodes.ErrorInvalidFilePath)
                return false;

            throw;
        }
    }

    public async Task<File> GetFileInfo(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var info = (await client.FileGetInfo(filePath: path))?.ToList();
        if (info?.Count == 1) {
            var fInfo = new File()
            {
                FullPath = path,
                Name = info[0].Filename,
                Type = File.FileTyes.File,
                Size = info[0].Size,

                Created = info[0].Created
            };

            return fInfo;
        }
        throw new FileNotFoundException($"File {path} not found", path);
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);

        // Read folders
        var content = await client.FolderGetContent(folderPath: path, contentType: FolderContentType.Folders);
        while (content?.Folders != null) {
            foreach (var f in content.Folders) {
                var fInfo = new File()
                {
                    FullPath = $"{path}/{f.Name}",
                    Name = f.Name,
                    Type = File.FileTyes.Directory,
                    Size = f.TotalSize,

                    Created = f.Created,
                };
                yield return fInfo;
            }

            if (content.MoreChunks == YesNo.Yes) {
                content = await client.FolderGetContent(folderPath: path, contentType: FolderContentType.Folders, chunk: content.ChunkNumber + 1);
            } else
                content = null;
        }

        // Read files
        content = await client.FolderGetContent(folderPath: path, contentType: FolderContentType.Files);
        while (content?.Files != null) {
            foreach (var f in content.Files) {
                var fInfo = new File()
                {
                    FullPath = $"{path}/{f.Filename}",
                    Name = f.Filename,
                    Type = File.FileTyes.File,
                    Size = f.Size,

                    Created = f.Created
                };
                yield return fInfo;
            }

            if (content.MoreChunks == YesNo.Yes) {
                content = await client.FolderGetContent(folderPath: path, contentType: FolderContentType.Files, chunk: content.ChunkNumber + 1);
            } else
                content = null;
        }
    }

    public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);

        filePath = GetPath(filePath);
        var client = await GetClient();
        Stream? res;
        if (!write) {
            try {
                var info = (await client.FileGetInfo(filePath: filePath))?.ToList();
                if (info != null) {
                    var links = await client.DownloadDirectLink(new[] { info[0].QuickKey });
                    res = new ReaderStream(links.First().DirectDownload);
                } else {
                    throw new FileNotFoundException($"File {filePath} not found", filePath);
                }
            } catch (MediaFireApiException ex) {
                if (ex.ErrorCode == (int)ErrorCodes.ErrorInvalidFilePath)
                    throw new FileNotFoundException($"File {filePath} not found", filePath);

                throw;
            }
        } else {
            if (size == null)
                throw new ArgumentNullException(nameof(size));

            var parentPath = GetParent(filePath, out var childName);
            var ws = new WriterStream(size.Value);
            ws.WriteTask = client.UploadSimple(ws, "application/octet-stream", childName, ws.Length, path: parentPath);
            res = ws;
        }
        return new FileSystemStream(this, res);
    }

    public async Task<bool> CloseFile(Stream stream)
    {
        if (stream is WriterStream ws) {
            await ws.WaitTask();
        }
        stream.Close();

        return true;
    }

    #region private operations
    private async Task<Client> GetClient()
    {
        if (_client == null) {
            _client = new Client(new ClientSettings());
            await _client.Login(_username, _password);
        }

        return _client;
    }

    private string GetPath(string path)
    {
        return Utility.Files.GetPath(GetPathSeparator(), Scheme, path);
    }

    private string GetParent(string position, out string? childName)
    {
        return Utility.Files.GetParent(GetPathSeparator(), GetPath(position), out childName);
    }
    #endregion
}