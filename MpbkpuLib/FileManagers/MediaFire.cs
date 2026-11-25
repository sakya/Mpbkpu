using MediaFireApi;
using MediaFireApi.Exceptions;
using MediaFireApi.Models;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class MediaFire : IFileManager
{
    public const string Scheme = "mediafire://";

    #region classes
    /// <summary>
    /// Download stream: owns the HTTP response and reports the file size
    /// </summary>
    private sealed class ReaderStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _baseStream;
        private long _position;

        public ReaderStream(HttpResponseMessage response, Stream baseStream, long length)
        {
            _response = response;
            _baseStream = baseStream;
            Length = length;
        }

        protected override void Dispose(bool disposing)
        {
            _baseStream.Dispose();
            _response.Dispose();
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _baseStream.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _baseStream.ReadAsync(buffer, cancellationToken);
            _position += read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get; }

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    #endregion

    /// <summary>
    /// Shared by all the downloads; no timeout because files can be large
    /// </summary>
    private static readonly HttpClient DownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string? _username;
    private readonly string? _password;
    private Client? _client;

    public MediaFire(string username, string password)
    {
        if (string.IsNullOrEmpty(username))
            throw new ArgumentNullException(nameof(username));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));
        _username = username;
        _password = password;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    public bool IsCaseSensitive => true;

    public string GetScheme()
    {
        return Scheme;
    }

    public char PathSeparator => '/';

    public bool CanOpenDatabase => false;

    public bool SupportCompression => false;

    public string CombinePath(params string[] paths)
    {
        return Utility.Files.CombinePath(PathSeparator, paths);
    }

    public async Task<StorageSpace?> GetSpace(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var info = await (await GetClient()).UserGetInfo();
        return new StorageSpace(Math.Max(0, info.StorageLimit - info.UsedStorageSize), info.StorageLimit);
    }

    public async Task<bool> CreateDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        if (await DirectoryExists(path))
            return true;
        var client = await GetClient();
        var parentPath = GetParent(GetPath(path), out var childName);
        await client.FolderCreate(parentPath: parentPath, name: childName, actionOnDuplicate: ActionOnDuplicate.Skip);
        return true;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        if (!await DirectoryExists(path))
            throw new DirectoryNotFoundException($"Folder {path} not found");
        var client = await GetClient();
        var info = (await client.FolderGetInfo(folderPath: GetPath(path)))!.ToList();
        await client.FolderDelete(new []{ info[0].FolderKey });
        await client.FolderPurge( new []{ info[0].FolderKey });
        return true;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var quickKey = await GetQuickKey(path);
        var client = await GetClient();
        await client.FileDelete(new []{ quickKey });
        await client.FilePurge( new []{ quickKey });
        return true;
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
        var info = await TryGetFileInfo(client, path);
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
        var apiPath = GetPath(path);

        // Read folders (full paths keep the form of the given path, so relative paths can be computed)
        var content = await client.FolderGetContent(folderPath: apiPath, contentType: FolderContentType.Folders)
            .ContinueWith(t => t.Exception?.InnerException is MediaFireApiException { ErrorCode: (int)ErrorCodes.ErrorInvalidFolderPath } ?
                throw new DirectoryNotFoundException($"Folder {path} not found") : t.GetAwaiter().GetResult());
        while (content?.Folders != null) {
            foreach (var f in content.Folders) {
                var fInfo = new File()
                {
                    FullPath = CombinePath(path, f.Name),
                    Name = f.Name,
                    Type = File.FileTyes.Directory,
                    Size = f.TotalSize,

                    Created = f.Created,
                };
                yield return fInfo;
            }

            if (content.MoreChunks == YesNo.Yes) {
                content = await client.FolderGetContent(folderPath: apiPath, contentType: FolderContentType.Folders, chunk: content.ChunkNumber + 1);
            } else
                content = null;
        }

        // Read files
        content = await client.FolderGetContent(folderPath: apiPath, contentType: FolderContentType.Files);
        while (content?.Files != null) {
            foreach (var f in content.Files) {
                var fInfo = new File()
                {
                    FullPath = CombinePath(path, f.Filename),
                    Name = f.Filename,
                    Type = File.FileTyes.File,
                    Size = f.Size,

                    Created = f.Created
                };
                yield return fInfo;
            }

            if (content.MoreChunks == YesNo.Yes) {
                content = await client.FolderGetContent(folderPath: apiPath, contentType: FolderContentType.Files, chunk: content.ChunkNumber + 1);
            } else
                content = null;
        }
    }

    public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);

        var client = await GetClient();
        if (!write) {
            var info = await TryGetFileInfo(client, filePath);
            if (info is not { Count: 1 })
                throw new FileNotFoundException($"File {filePath} not found", filePath);
            var links = await client.DownloadDirectLink(new[] { info[0].QuickKey });
            // Stream the content: never load the whole file in memory
            var response = await DownloadClient.GetAsync(new Uri(links.First().DirectDownload), HttpCompletionOption.ResponseHeadersRead);
            try {
                // An error page must never be read as the file content
                response.EnsureSuccessStatusCode();
                return new FileSystemStream(this, new ReaderStream(response, await response.Content.ReadAsStreamAsync(), info[0].Size));
            } catch {
                response.Dispose();
                throw;
            }
        }

        if (size == null)
            throw new ArgumentNullException(nameof(size));

        var parentPath = GetParent(filePath, out var childName);
        var res = new UploadStream(size.Value, (stream, _) =>
            client.UploadSimple(stream, "application/octet-stream", childName, size.Value, path: parentPath,
                actionOnDuplicate: ActionOnDuplicate.Replace));
        return new FileSystemStream(this, res);
    }

    public async Task RenameFile(string? filePath, string? newName)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);
        if (string.IsNullOrEmpty(newName))
            throw new ArgumentNullException(newName);
        if (newName.Contains(PathSeparator))
            throw new ArgumentException($"The new name cannot contain '{PathSeparator}'", newName);

        var quickKey = await GetQuickKey(filePath);
        var parentPath = GetParent(filePath, out _);
        if (await FileExists(CombinePath(parentPath, newName)))
            throw new IOException($"A file named {newName} already exists");
        var client = await GetClient();
        await client.FileUpdate(quickKey: quickKey, filename: newName);
    }

    public async Task<bool> CloseFile(Stream stream)
    {
        try {
            if (stream is UploadStream us)
                await us.CompleteAsync();
        } finally {
            stream.Close();
        }

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

    /// <summary>
    /// File information, null if the file does not exist
    /// </summary>
    private async Task<List<FileItem>?> TryGetFileInfo(Client client, string path)
    {
        try {
            return (await client.FileGetInfo(filePath: GetPath(path)))?.ToList();
        } catch (MediaFireApiException ex) when (ex.ErrorCode == (int)ErrorCodes.ErrorInvalidFilePath) {
            return null;
        }
    }

    private async Task<string> GetQuickKey(string path)
    {
        var info = await TryGetFileInfo(await GetClient(), path);
        if (info is not { Count: 1 })
            throw new FileNotFoundException($"File {path} not found", path);
        return info[0].QuickKey;
    }

    private string GetPath(string path)
    {
        return Utility.Files.GetPath(PathSeparator, Scheme, path);
    }

    private string GetParent(string position, out string? childName)
    {
        return Utility.Files.GetParent(PathSeparator, GetPath(position), out childName);
    }
    #endregion
}