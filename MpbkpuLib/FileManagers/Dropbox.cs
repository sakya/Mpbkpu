using System.Net;
using System.Text;
using Dropbox.Api;
using Dropbox.Api.Files;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class Dropbox : IFileManager, IOAuth2
{
    public const string Scheme = "dropbox://";

    /// <summary>
    /// Size of each request of an upload session (Dropbox accepts up to 150 MB)
    /// </summary>
    private const int ChunkSize = 4 * 1024 * 1024;

    private readonly string _tokenCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu", "DropboxTokenCache");

    private const string ResponsePage = @"<html>
<script type=""text/javascript"">
    function redirect() {
        // Append fragment as query string so that server can receive it.
        document.location.href = ""/token?url_with_fragment="" + encodeURIComponent(document.location.href);
    }
</script>
<body onload=""redirect()""/>
</html>";
    private const string TokenResponsePage = @"<html>
<body style=""height: 100%; width: 100%;"">
  <div style=""display: flex; display: flex; justify-content: center;align-items: center;height: 100%;"">
    <h2>You can close this tab</h2>    
  </div>
</body>
</html>";

    private DropboxClient? _client;
    private readonly string? _account;

    public Dropbox(string? account)
    {
        if (string.IsNullOrEmpty(account))
            throw new ArgumentNullException(nameof(account));

        _account = account;
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

    public bool IsCaseSensitive => false;

    public string GetScheme()
    {
        return Scheme;
    }

    public char PathSeparator => '/';

    public bool CanOpenDatabase => false;

    public bool SupportCompression => false;

    public string CombinePath(params string[] paths)
    {
        return Files.CombinePath(PathSeparator, paths);
    }

    public async Task<bool> CreateDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var existing = await GetItem(path);
        if (existing != null) {
            if (existing.IsFolder)
                return true;
            throw new IOException($"A file named {path} already exists");
        }

        var client = await GetClient();
        await client.Files.CreateFolderV2Async(new CreateFolderArg(GetPath(path)));
        return true;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        if ((await GetItem(path))?.IsFolder != true)
            throw new DirectoryNotFoundException($"Folder {path} not found");
        await (await GetClient()).Files.DeleteV2Async(new DeleteArg(GetPath(path)));
        return true;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        if ((await GetItem(path))?.IsFile != true)
            throw new FileNotFoundException($"File {path} not found", path);
        await (await GetClient()).Files.DeleteV2Async(new DeleteArg(GetPath(path)));
        return true;
    }

    public async Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        // The root has no metadata
        if (GetPath(path).Length == 0)
            return true;
        return (await GetItem(path))?.IsFolder == true;
    }

    public async Task<bool> FileExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        return (await GetItem(path))?.IsFile == true;
    }

    public async Task<File> GetFileInfo(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var item = await GetItem(path);
        if (item?.IsFile != true)
            throw new FileNotFoundException($"File {path} not found", path);
        return ToFile(item, path);
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        if (!await DirectoryExists(path))
            throw new DirectoryNotFoundException($"Folder {path} not found");

        var client = await GetClient();
        var list = await client.Files.ListFolderAsync(GetPath(path));
        while (list != null) {
            foreach (var item in list.Entries) {
                if (!item.IsFile && !item.IsFolder)
                    continue;
                // Full paths keep the form of the given path, so relative paths can be computed
                yield return ToFile(item, CombinePath(path, item.Name));
            }

            list = list.HasMore ? await client.Files.ListFolderContinueAsync(list.Cursor) : null;
        }
    }

    public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);

        var client = await GetClient();
        var apiPath = GetPath(filePath);
        if (!write) {
            if ((await GetItem(filePath))?.IsFile != true)
                throw new FileNotFoundException($"File {filePath} not found", filePath);
            var downloadRes = await client.Files.DownloadAsync(apiPath);
            return new FileSystemStream(this, await downloadRes.GetContentAsStreamAsync());
        }

        if (size == null)
            throw new ArgumentNullException(nameof(size));

        var res = new UploadStream(size.Value, (stream, token) => Upload(client, apiPath, stream, size.Value, token));
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

        if ((await GetItem(filePath))?.IsFile != true)
            throw new FileNotFoundException($"File {filePath} not found", filePath);

        var apiPath = GetPath(filePath);
        var parent = Files.GetParent(PathSeparator, apiPath, out _);
        var client = await GetClient();
        await client.Files.MoveV2Async(new RelocationArg(apiPath, Files.CombinePath(PathSeparator, parent, newName)));
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
    private async Task<DropboxClient> GetClient()
    {
        if (_client == null) {
            if (RedirectUrl == null)
                throw new Exception($"{nameof(RedirectUrl)} cannot be empty");
            if (string.IsNullOrEmpty(AppId))
                throw new Exception($"{nameof(AppId)} cannot be empty");
            if (string.IsNullOrEmpty(AppSecret))
                throw new Exception($"{nameof(AppSecret)} cannot be empty");

            var saved = await GetToken(_account!);
            if (saved == null) {
                var state = Guid.NewGuid().ToString("N");
                var codeVerifier = DropboxOAuth2Helper.GeneratePKCECodeVerifier();
                var codeChallenge = DropboxOAuth2Helper.GeneratePKCECodeChallenge(codeVerifier);
                var authorizeUri = DropboxOAuth2Helper.GetAuthorizeUri(OAuthResponseType.Code, AppId, RedirectUrl,
                    tokenAccessType: TokenAccessType.Offline, state: state,
                    codeChallenge: codeChallenge);
                using var http = new HttpListener();
                http.Prefixes.Add($"{RedirectUrl.Scheme}://{RedirectUrl.Host}:{RedirectUrl.Port}/");
                http.Start();

                Browser.Open(authorizeUri.ToString());
                await HandleOAuth2Redirect(http);
                var redirectUri = await HandleJsRedirect(http);
                var tokenResult = await DropboxOAuth2Helper.ProcessCodeFlowAsync(redirectUri, AppId, AppSecret,
                    RedirectUrl.ToString(), state, codeVerifier: codeVerifier);
                http.Stop();

                var accessToken = tokenResult.AccessToken;
                var refreshToken = tokenResult.RefreshToken;
                _client = new DropboxClient(accessToken, refreshToken, AppId, AppSecret, new DropboxClientConfig());
                var account = await _client.Users.GetCurrentAccountAsync();
                if (string.Compare(account.Email, _account, StringComparison.InvariantCultureIgnoreCase) != 0) {
                    _client.Dispose();
                    throw new Exception("Wrong account");
                }

                await SaveToken(_account!, accessToken, refreshToken, tokenResult.ExpiresAt);
            } else {
                _client = new DropboxClient(saved.AccessToken, saved.RefreshToken, AppId, AppSecret, new DropboxClientConfig());
            }
        }
        return _client;
    }

    private async Task HandleOAuth2Redirect(HttpListener http)
    {
        var context = await http.GetContextAsync();
        while (context.Request.Url!.AbsolutePath != RedirectUrl!.AbsolutePath) {
            context = await http.GetContextAsync();
        }

        context.Response.ContentType = "text/html";
        using (var file = new MemoryStream(Encoding.UTF8.GetBytes(ResponsePage))) {
            await file.CopyToAsync(context.Response.OutputStream);
        }

        context.Response.OutputStream.Close();
    }

    private async Task<Uri> HandleJsRedirect(HttpListener http)
    {
        var jsRedirectUri = new Uri($"{RedirectUrl!.Scheme}://{RedirectUrl.Host}:{RedirectUrl.Port}/token");
        var context = await http.GetContextAsync();
        while (context.Request.Url!.AbsolutePath != jsRedirectUri.AbsolutePath) {
            context = await http.GetContextAsync();
        }
        var redirectUri = new Uri(context.Request.QueryString["url_with_fragment"]!);
        using (var file = new MemoryStream(Encoding.UTF8.GetBytes(TokenResponsePage))) {
            await file.CopyToAsync(context.Response.OutputStream);
        }
        context.Response.OutputStream.Close();

        return redirectUri;
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

    /// <summary>
    /// Path for the Dropbox API: empty for the root, "/a/b" otherwise
    /// </summary>
    private string GetPath(string path)
    {
        var segments = Files.GetPath(PathSeparator, Scheme, path).Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? string.Empty : $"{PathSeparator}{string.Join(PathSeparator, segments)}";
    }

    /// <summary>
    /// Metadata of a file or folder, null if it does not exist (or for the root)
    /// </summary>
    private async Task<Metadata?> GetItem(string path)
    {
        var apiPath = GetPath(path);
        if (apiPath.Length == 0)
            return null;
        try {
            return await (await GetClient()).Files.GetMetadataAsync(new GetMetadataArg(apiPath));
        } catch (ApiException<GetMetadataError> ex) when (ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsNotFound) {
            return null;
        }
    }

    private static File ToFile(Metadata item, string path)
    {
        return new File()
        {
            FullPath = path,
            Name = item.Name,
            Type = item.IsFolder ? File.FileTyes.Directory : File.FileTyes.File,
            Size = item.IsFile ? (long)item.AsFile.Size : 0,
            LastModified = item.IsFile ? item.AsFile.ServerModified : null
        };
    }

    /// <summary>
    /// Upload the data read from a stream: one request up to <see cref="ChunkSize"/>, an upload session otherwise.
    /// Every request gets its own buffer of known length, so a failed request never leaves the stream in an odd state.
    /// </summary>
    private static async Task Upload(DropboxClient client, string apiPath, Stream stream, long size, CancellationToken token)
    {
        var buffer = new byte[(int)Math.Min(ChunkSize, Math.Max(size, 1))];

        async Task<MemoryStream> ReadChunk()
        {
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), token)) > 0)
                read += n;
            return new MemoryStream(buffer, 0, read, false);
        }

        var commit = new CommitInfo(apiPath, WriteMode.Overwrite.Instance);
        if (size <= ChunkSize) {
            await client.Files.UploadAsync(new UploadArg(apiPath, WriteMode.Overwrite.Instance), await ReadChunk());
            return;
        }

        string sessionId;
        ulong offset;
        using (var chunk = await ReadChunk()) {
            sessionId = (await client.Files.UploadSessionStartAsync(body: chunk)).SessionId;
            offset = (ulong)chunk.Length;
        }
        while (true) {
            using var chunk = await ReadChunk();
            if (chunk.Length == 0)
                throw new IOException($"Upload of {apiPath} incomplete: {offset} of {size} bytes");
            var cursor = new UploadSessionCursor(sessionId, offset);
            if ((long)offset + chunk.Length >= size) {
                await client.Files.UploadSessionFinishAsync(cursor, commit, body: chunk);
                return;
            }
            await client.Files.UploadSessionAppendV2Async(cursor, body: chunk);
            offset += (ulong)chunk.Length;
        }
    }

    #endregion
}