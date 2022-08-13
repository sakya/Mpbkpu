using System.Net;
using System.Text;
using DeviceId;
using Dropbox.Api;
using Dropbox.Api.Files;
using MpbkpuLib.Interfaces;
using Newtonsoft.Json;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

// https://github.com/dropbox/dropbox-sdk-dotnet/blob/master/dropbox-sdk-dotnet/Examples/SimpleTest/Program.cs#L239
public class Dropbox : IFileManager
{
    #region classes
    internal class TokenCache
    {
        public class Data
        {
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
        }

        public TokenCache()
        {
            Tokens = new Dictionary<string, Data>();
        }

        public Dictionary<string, Data> Tokens { get; set; }
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
            WriteTask?.Dispose();
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
            throw new NotImplementedException();
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
        }
    }
    #endregion

    private readonly string _deviceId = new DeviceIdBuilder().AddMachineName().AddMacAddress().AddOsVersion().AddUserName().ToString();
    private readonly string _tokenCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu", "DropboxTokenCache");
    private const string AppKey = "94qe1yortb3afkm";
    private const string AppSecret = "wbpax6rte3304t8";
    private const string LoopbackHost = "http://127.0.0.1:52475/";

    private const string ResponsePage = @"<html>
<script type=""text/javascript"">
    function redirect() {
        // Append fragment as query string so that server can receive it.
        document.location.href = ""/token?url_with_fragment="" + encodeURIComponent(document.location.href);
    }
</script>
<body onload=""redirect()""/>
</html>";
    private readonly Uri _redirectUri = new Uri(LoopbackHost + "authorize");
    private readonly Uri _jsRedirectUri = new Uri(LoopbackHost + "token");
    private DropboxClient? _client;
    private readonly string? _account;

    public Dropbox(string? account)
    {
        if (string.IsNullOrEmpty(account))
            throw new ArgumentNullException(nameof(account));
        _account = account;
    }

    public void Dispose()
    {
        _client?.Dispose();
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
        return Utility.Files.CombinePath('/', paths);
    }

    public async Task<bool> CreateDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var folderArg = new CreateFolderArg(path);
        var folder = await client.Files.CreateFolderV2Async(folderArg);

        return folder != null;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var args = new DeleteArg(path);
        var result = await client.Files.DeleteV2Async(args);
        return result != null;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var args = new DeleteArg(path);
        var result = await client.Files.DeleteV2Async(args);
        return result != null;
    }

    public async Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);

        var metadataArgs = new GetMetadataArg(path);
        try {
            var metadata = await client.Files.GetMetadataAsync(metadataArgs);
            return metadata?.IsFolder == true;
        } catch (ApiException<GetMetadataError> ex) {
            if (ex.ErrorResponse is GetMetadataError.Path)
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

        var metadataArgs = new GetMetadataArg(path);
        try {
            var metadata = await client.Files.GetMetadataAsync(metadataArgs);
            return metadata?.IsFile == true;
        } catch (ApiException<GetMetadataError> ex) {
            if (ex.ErrorResponse is GetMetadataError.Path)
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

        var metadataArgs = new GetMetadataArg(path);
        var item = await client.Files.GetMetadataAsync(metadataArgs);
        return new File()
        {
            FullPath = $"{path}/{item.Name}",
            Name = item.Name,
            Type = item.IsFolder ? File.FileTyes.Directory : File.FileTyes.File,
            Size = item.IsFile ? (long)item.AsFile.Size : 0,

            LastModified = item.IsFile ? item.AsFile.ServerModified : null
        };
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var list = await client.Files.ListFolderAsync(path);
        while (list != null) {
            foreach (var item in list.Entries) {
                var fInfo = new File()
                {
                    FullPath = $"{path}/{item.Name}",
                    Name = item.Name,
                    Type = item.IsFolder ? File.FileTyes.Directory : File.FileTyes.File,
                    Size = item.IsFile ? (long)item.AsFile.Size : 0,

                    LastModified = item.IsFile ? item.AsFile.ServerModified : null

                };
                yield return fInfo;
            }

            if (list.HasMore) {
                list = await client.Files.ListFolderContinueAsync(list.Cursor);
            } else {
                list = null;
            }
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
            var downloadRes = await client.Files.DownloadAsync(filePath);
            res = await downloadRes.GetContentAsStreamAsync();
        } else {
            if (size == null)
                throw new ArgumentNullException(nameof(size));

            res = new WriterStream(size.Value);
            await client.Files.UploadAsync(filePath, WriteMode.Overwrite.Instance, body: res);
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
    private async Task<DropboxClient> GetClient()
    {
        if (_client == null) {
            var saved = await GetConfig(_account!);
            if (saved == null) {
                var state = Guid.NewGuid().ToString("N");
                var codeVerifier = DropboxOAuth2Helper.GeneratePKCECodeVerifier();
                var codeChallenge = DropboxOAuth2Helper.GeneratePKCECodeChallenge(codeVerifier);
                var authorizeUri = DropboxOAuth2Helper.GetAuthorizeUri(OAuthResponseType.Code, AppKey, _redirectUri,
                    tokenAccessType: TokenAccessType.Offline, state: state, codeChallenge: codeChallenge);
                using var http = new HttpListener();
                http.Prefixes.Add(LoopbackHost);
                http.Start();

                if (OperatingSystem.IsLinux()) {
                    System.Diagnostics.Process.Start($"xdg-open", authorizeUri.ToString());
                } else {
                    System.Diagnostics.Process.Start(authorizeUri.ToString());
                }

                await HandleOAuth2Redirect(http);
                var redirectUri = await HandleJsRedirect(http);
                var tokenResult = await DropboxOAuth2Helper.ProcessCodeFlowAsync(redirectUri, AppKey, AppSecret,
                    _redirectUri.ToString(), state, codeVerifier: codeVerifier);

                var accessToken = tokenResult.AccessToken;
                var refreshToken = tokenResult.RefreshToken;
                _client = new DropboxClient(accessToken, refreshToken, AppKey, AppSecret, new DropboxClientConfig());
                var account = await _client.Users.GetCurrentAccountAsync();
                if (string.Compare(account.Email, _account, StringComparison.InvariantCultureIgnoreCase) != 0) {
                    _client.Dispose();
                    throw new Exception("Wrong account");
                }

                await SaveConfig(_account!, accessToken, refreshToken);
            } else {
                _client = new DropboxClient(saved.AccessToken, saved.RefreshToken, AppKey, AppSecret, new DropboxClientConfig());
            }
        }
        return _client;
    }

    private async Task HandleOAuth2Redirect(HttpListener http)
    {
        var context = await http.GetContextAsync();
        while (context.Request.Url!.AbsolutePath != _redirectUri.AbsolutePath) {
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
        var context = await http.GetContextAsync();
        while (context.Request.Url!.AbsolutePath != _jsRedirectUri.AbsolutePath) {
            context = await http.GetContextAsync();
        }
        var redirectUri = new Uri(context.Request.QueryString["url_with_fragment"]!);

        return redirectUri;
    }

    private async Task<TokenCache.Data?> GetConfig(string account)
    {
        if (!System.IO.File.Exists(_tokenCache))
            return null;

        TokenCache? cache = null;
        using (var fs = new FileStream(_tokenCache, FileMode.Open, FileAccess.Read)) {
            using (var sr = new StreamReader(fs)) {
                cache = JsonConvert.DeserializeObject<TokenCache>(await sr.ReadToEndAsync());
            }
        }

        if (cache == null)
            return null;

        var enc = new Utility.EncryptDecrypt(_deviceId);
        account = await enc.Encrypt(account);
        TokenCache.Data? data;
        if (cache.Tokens.TryGetValue(account, out data)) {
            data.AccessToken = await enc.Decrypt(data.AccessToken!);
            data.RefreshToken = await enc.Decrypt(data.RefreshToken!);

            return data;
        }

        return null;
    }

    private async Task SaveConfig(string account, string accessToken, string refreshToken)
    {
        var dir = Path.GetDirectoryName(_tokenCache);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir!);

        TokenCache? cache = null;
        using (var fs = new FileStream(_tokenCache, FileMode.OpenOrCreate, FileAccess.Read)) {
            using (var sr = new StreamReader(fs)) {
                cache = JsonConvert.DeserializeObject<TokenCache>(await sr.ReadToEndAsync());
            }
        }

        if (cache == null)
            cache = new TokenCache();

        var enc = new Utility.EncryptDecrypt(_deviceId);
        var data = new TokenCache.Data()
        {
            AccessToken = await enc.Encrypt(accessToken),
            RefreshToken = await enc.Encrypt(refreshToken)
        };
        account = await enc.Encrypt(account);

        cache.Tokens[account] = data;
        using (var fs = new FileStream(_tokenCache, FileMode.Create, FileAccess.Write)) {
            using (var sw = new StreamWriter(fs)) {
                await sw.WriteAsync(JsonConvert.SerializeObject(cache));
            }
        }
    }

    private string GetPath(string path)
    {
        return Utility.Files.GetPath('/', "dropbox://", path);
    }

    #endregion
}