using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using NextcloudClient;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class Nextcloud : IFileManager, IOAuth2
{
    public const string Scheme = "nextcloud://";

    private readonly string _userId;
    private readonly string? _password;
    private Client? _client;
    private readonly string _tokenCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu", "NextcloudTokenCache");

    public Nextcloud(string? userId, string? password = null)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentNullException(nameof(userId));
        _userId = userId;
        _password = password;
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

    // Thread-safe HTTP client; a small limit avoids the rate limits of the service
    public int MaxParallelTransfers => 4;

    public string CombinePath(params string[] paths)
    {
        return Files.CombinePath(PathSeparator, paths);
    }

    public async Task<StorageSpace?> GetSpace(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        var info = await Folder.GetInfo(client, GetPath(path));
        var props = info.Response.Count == 1 ? info.Response[0].PropStat.Properties : null;
        // Negative values: unknown or unlimited
        if (props?.QuotaAvailableBytes is not >= 0)
            return null;
        var available = props.QuotaAvailableBytes.Value;
        return new StorageSpace(available, props.QuotaUsedBytes is >= 0 ? available + props.QuotaUsedBytes.Value : null);
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
        var item = await NextcloudClient.File.GetInfo(client, GetPath(path));
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
        var folderPath = GetPath(path).Trim(PathSeparator);
        var res = await Folder.ListContent(client, folderPath);
        foreach (var item in res.Response) {
            // The response includes the folder itself
            var itemPath = client.GetPathFromHref(item.Href).Trim(PathSeparator);
            if (itemPath == folderPath)
                continue;

            // Full paths keep the form of the given path, so relative paths can be computed
            var name = GetName(itemPath);
            var fInfo = new File()
            {
                FullPath = CombinePath(path, name),
                Name = name,
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

            var uploadPath = filePath;
            res = new UploadStream(size.Value, (stream, _) => NextcloudClient.File.Upload(client, uploadPath, stream, size.Value));
        }

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

        var client = await GetClient();
        filePath = GetPath(filePath);

        await GetFileInfo(filePath);
        var parentPath = Utility.Files.GetParent(PathSeparator, filePath, out _);
        var targetPath = Utility.Files.CombinePath(PathSeparator, parentPath, newName);
        if (await FileExists(targetPath))
            throw new Exception($"A file named {newName} already exists");

        await NextcloudClient.File.Move(client, filePath, targetPath, false);
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
            if (ServerUrl == null)
                throw new Exception($"{nameof(ServerUrl)} cannot be empty");
            if (string.IsNullOrEmpty(_password)) {
                if (RedirectUrl == null)
                    throw new Exception($"{nameof(RedirectUrl)} cannot be empty");
                if (string.IsNullOrEmpty(AppId))
                    throw new Exception($"{nameof(AppId)} cannot be empty");
                if (string.IsNullOrEmpty(AppSecret))
                    throw new Exception($"{nameof(AppSecret)} cannot be empty");
            }

            var saved = string.IsNullOrEmpty(_password) ? await GetToken(_userId) : null;
            _client = new Client(new ClientSettings()
            {
                ServerUri = ServerUrl,
                RedirectUri = RedirectUrl,

                ClientName = AppName,
                ClientId = AppId,
                ClientSecret = AppSecret,

                AccessToken = saved?.AccessToken,
                RefreshToken = saved?.RefreshToken,
                TokenExpiration = saved?.ExpiresAt,

                UserId = _userId,
                Password = _password
            });
            await _client.Login();
            if (string.Compare(_client.UserId, _userId, StringComparison.InvariantCultureIgnoreCase) != 0)
                throw new Exception("Account mismatch");
            if (string.IsNullOrEmpty(_password)) {
                // Refresh tokens are single use: save every new one
                var client = _client;
                client.TokensRefreshed = () => SaveToken(_userId, client.AccessToken, client.RefreshToken, client.TokenExpiration);
                await SaveToken(_userId, _client.AccessToken, _client.RefreshToken, _client.TokenExpiration);
            }
        }

        return _client;
    }

    private string GetPath(string path)
    {
        return Files.GetPath(PathSeparator, Scheme, path);
    }

    private string GetName(string path)
    {
        path = path.TrimEnd(PathSeparator);
        return path[(path.LastIndexOf(PathSeparator) + 1)..];
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