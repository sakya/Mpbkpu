using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using NextcloudClient;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class Nextcloud : IFileManager, IOAuth2
{
    public const string Scheme = "nextcloud://";

    #region classes
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

    private readonly string _userId;
    private Client? _client;
    private readonly string _tokenCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu", "NextcloudTokenCache");

    public Nextcloud(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentNullException(nameof(userId));
        _userId = userId;
    }

    #region IOAuth2 implementation
    public string? AppName { get; set; }
    public string? AppId { get; set; }
    public string? AppSecret { get; set; }
    public string? TenantId { get; set; }
    public Uri? ServerUrl { get; set; }
    public Uri? RedirectUrl { get; set; }
    #endregion

    public void Dispose()
    {
    }

    public string GetScheme()
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
        return Files.CombinePath(GetPathSeparator(), paths);
    }

    public async Task<bool> CreateDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        await Folder.Create(client, path);
        return true;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        await Folder.Delete(client, path);
        return true;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        await NextcloudClient.File.Delete(client, path);
        return true;
    }

    public async Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        try {
            var item = await Folder.GetInfo(client, path);
            return item.Response.Count == 1;
        } catch (DirectoryNotFoundException) {
            return false;
        }
    }

    public async Task<bool> FileExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        try {
            var item = await NextcloudClient.File.GetInfo(client, path);
            return item.Response.Count == 1;
        } catch (FileNotFoundException) {
            return false;
        }
    }

    public async Task<File> GetFileInfo(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var item = await NextcloudClient.File.GetInfo(client, path);
        if (item.Response.Count != 1)
            throw new FileNotFoundException($"File {path} not found", path);

        var fInfo = new File()
        {
            FullPath = path,
            Name = GetName(path),
            Type = File.FileTyes.File,
            Size = item.Response[0].PropStat.Properties.ContentLength,

            LastModified = item.Response[0].PropStat.Properties.LastModifiedDate
        };
        return fInfo;
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var res = await Folder.ListContent(client, path);
        foreach (var item in res.Response) {
            var fInfo = new File()
            {
                FullPath = item.Href,
                Name = GetName(item.Href),
                Type = item.PropStat.Properties.ResourceType.Collection != null ? File.FileTyes.Directory : File.FileTyes.File,
                Size = item.PropStat.Properties.ContentLength,

                LastModified = item.PropStat.Properties.LastModifiedDate
            };
            yield return fInfo;
        }
    }

    public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);

        var client = await GetClient();
        filePath = GetPath(filePath);
        Stream? res;
        if (!write) {
            res = await NextcloudClient.File.Download(client, filePath);
        } else {
            if (size == null)
                throw new ArgumentNullException(nameof(size));

            var ws = new WriterStream(size.Value);
            ws.WriteTask = NextcloudClient.File.Upload(client, filePath, ws);
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
            if (ServerUrl == null)
                throw new Exception($"{nameof(ServerUrl)} cannot be empty");
            if (RedirectUrl == null)
                throw new Exception($"{nameof(RedirectUrl)} cannot be empty");
            if (string.IsNullOrEmpty(AppId))
                throw new Exception($"{nameof(AppId)} cannot be empty");
            if (string.IsNullOrEmpty(AppSecret))
                throw new Exception($"{nameof(AppSecret)} cannot be empty");

            var saved = await GetToken(_userId);
            _client = new Client(new ClientSettings()
            {
                ServerUri = ServerUrl,
                RedirectUri = RedirectUrl,

                AppName = AppName,
                ClientId = AppId,
                ClientSecret = AppSecret,

                AccessToken = saved?.AccessToken,
                RefreshToken = saved?.RefreshToken,
                TokenExpiration = saved?.ExpiresAt
            });
            await _client.Login();
            if (string.Compare(_client.UserId, _userId, StringComparison.InvariantCultureIgnoreCase) != 0)
                throw new Exception("Account mismatch");
            await SaveToken(_userId, _client.AccessToken, _client.RefreshToken, _client.TokenExpiration);
        }

        return _client;
    }

    private string GetPath(string path)
    {
        return Files.GetPath(GetPathSeparator(), Scheme, path);
    }

    private string GetName(string path)
    {
        var idx = path.LastIndexOf($"{GetPathSeparator()}", StringComparison.InvariantCultureIgnoreCase);
        return idx >= 0 ? path.Substring(idx) : path;
    }

    private async Task<OAuthTokenCache.TokenData?> GetToken(string account)
    {
        var cache = new OAuthTokenCache(_tokenCache);
        return await cache.GetToken(account);
    }

    private async Task SaveToken(string account, string accessToken, string refreshToken, DateTime? expiresAt)
    {
        var cache = new OAuthTokenCache(_tokenCache);
        await cache.SaveToken(account, accessToken, refreshToken, expiresAt);
    }
    #endregion
}