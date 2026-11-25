using MpbkpuLib.Interfaces;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class OneDrive : IFileManager, IOAuth2
    {
        public const string Scheme = "onedrive://";

        #region classes
        private class TokenProvider : IAccessTokenProvider
        {
            private readonly string _accessToken;

            public TokenProvider(string accessToken)
            {
                _accessToken = accessToken;
            }

            public AllowedHostsValidator AllowedHostsValidator { get; } = new();

            public Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_accessToken);
            }
        }

        private class WriterStream : Stream
        {
            private readonly Task? _writeTask;
            private long _pos;

            private readonly long _cacheSize = 1310720;
            private readonly List<byte> _buffer = new();
            private readonly SemaphoreSlim _bufferSema = new(1,1);

            public WriterStream(UploadSession session, long length, IRequestAdapter requestAdapter)
            {
                Length = length;
                _writeTask = new LargeFileUploadTask<DriveItem>(session, this, (int)_cacheSize, requestAdapter).UploadAsync();
            }

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
                if (_writeTask != null)
                    await _writeTask;
                _writeTask?.Dispose();
            }
        }
        #endregion

        private GraphServiceClient? _client;
        private string? _driveId;
        private readonly string? _account;
        private readonly string[] _scopes = new[] { "User.Read", "files.readwrite", "offline_access" };

        public OneDrive(string? account)
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

        public async Task<File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            path = GetPath(path);
            try {
                var item = await client.Drives[_driveId].Root.ItemWithPath(path).GetAsync();
                if (item.File == null)
                    throw new FileNotFoundException($"File {path} not found", path);

                var fInfo = new File()
                {
                    FullPath = $"{path}/{item.Name}",
                    Name = item.Name,
                    Type = item.Folder != null ? File.FileTyes.Directory : File.FileTyes.File,
                    Size = item.Size ?? 0,

                    Created = item.CreatedDateTime?.UtcDateTime,
                    LastModified = item.LastModifiedDateTime?.UtcDateTime
                };
                return fInfo;
            } catch (ApiException ex) {
                if (ex.ResponseStatusCode == 404)
                    throw new FileNotFoundException($"File {path} not found", path);
                throw;
            }
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            path = GetPath(path);
            var children = client.Drives[_driveId].Root.ItemWithPath(path).Children;
            var page = await children.GetAsync();
            while (page != null) {
                foreach (var item in page.Value ?? new List<DriveItem>()) {
                    var fInfo = new File()
                    {
                        FullPath = $"{path}/{item.Name}",
                        Name = item.Name,
                        Type = item.Folder != null ? File.FileTyes.Directory : File.FileTyes.File,
                        Size = item.Size ?? 0,

                        Created = item.CreatedDateTime?.UtcDateTime,
                        LastModified = item.LastModifiedDateTime?.UtcDateTime
                    };
                    yield return fInfo;
                }

                page = page.OdataNextLink != null ? await children.WithUrl(page.OdataNextLink).GetAsync() : null;
            }
        }

        public async Task<bool> CreateDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            path = GetPath(path);
            var parent = GetParent(path, out var dirName);
            var folderDriveItem = new DriveItem()
            {
                Name = dirName,
                Folder = new Folder(),
            };

            if (string.IsNullOrEmpty(parent)) {
                await client.Drives[_driveId].Items["root"].Children.PostAsync(folderDriveItem);
            } else {
                await client.Drives[_driveId].Root.ItemWithPath(parent).Children.PostAsync(folderDriveItem);
            }

            return true;
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            path = GetPath(path);
            await client.Drives[_driveId].Root.ItemWithPath(path).DeleteAsync();
            return true;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            path = GetPath(path);
            await client.Drives[_driveId].Root.ItemWithPath(path).DeleteAsync();
            return true;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            path = GetPath(path);
            try {
                var item = await client.Drives[_driveId].Root.ItemWithPath(path).GetAsync();
                return item.Folder != null;
            } catch (ApiException ex) {
                if (ex.ResponseStatusCode == 404)
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
                var item = await client.Drives[_driveId].Root.ItemWithPath(path).GetAsync();
                return item.File != null;
            } catch (ApiException ex) {
                if (ex.ResponseStatusCode == 404)
                    return false;
                throw;
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
                res = await client.Drives[_driveId].Root.ItemWithPath(filePath).Content.GetAsync();
            } else {
                if (size == null)
                    throw new ArgumentNullException(nameof(size));
                UploadSession uploadSession = await client.Drives[_driveId].Root.ItemWithPath(filePath).CreateUploadSession.PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody()) ?? throw new Exception("Failed to create upload session");
                res = new WriterStream(uploadSession, size.Value, client.RequestAdapter);
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
            var item = await client.Drives[_driveId].Root.ItemWithPath(filePath).GetAsync();
            if (item.File == null)
                throw new FileNotFoundException($"File {filePath} not found", filePath);

            var updateItem = new DriveItem
            {
                Id = item.Id,
                ParentReference = new ItemReference
                {
                    Id = item.ParentReference?.Id
                },
                Name = newName
            };
            await client.Drives[_driveId].Root.ItemWithPath(filePath).PatchAsync(updateItem);
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

        private async Task<GraphServiceClient> GetClient()
        {
            if (_client == null) {
                if (RedirectUrl == null)
                    RedirectUrl = new Uri("http://localhost");
                if (string.IsNullOrEmpty(AppName))
                    AppName = "Mpbkpu";
                if (string.IsNullOrEmpty(AppId))
                    AppId = "d7f0a79d-cfc0-4faa-9708-c0196c8eb26f";
                if (string.IsNullOrEmpty(TenantId))
                    TenantId = "common";

                var appOptions = new PublicClientApplicationOptions()
                {
                    ClientName = AppName,
                    ClientId = AppId,
                    TenantId = TenantId,
                    RedirectUri = RedirectUrl.ToString()
                };
                var app = PublicClientApplicationBuilder.CreateWithApplicationOptions(appOptions).Build();

                // Create token cache
                var storageProperties = new StorageCreationPropertiesBuilder("OneDriveUserTokenCache", Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu"))
                    .WithLinuxKeyring(
                        "com.github.sakya.Mpbkpu",
                        MsalCacheHelper.LinuxKeyRingDefaultCollection,
                        "MSAL token cache for Mpbkpu app.",
                        new KeyValuePair<string, string>("Version", "1"),
                        new KeyValuePair<string, string>("ProductGroup", "com.github.sakya"))
                    .WithMacKeyChain("Mpbkpu_msal_service", "Mpbkpu_msal_account")
                    .Build();
                var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
                cacheHelper.VerifyPersistence();
                cacheHelper.RegisterCache(app.UserTokenCache);

                // Get token
                var accounts = await app.GetAccountsAsync();
                AuthenticationResult result;
                try {
                    result = await app.AcquireTokenSilent(_scopes, accounts.FirstOrDefault(a => string.Compare(a.Username, _account, StringComparison.InvariantCultureIgnoreCase) == 0))
                        .ExecuteAsync();
                } catch (MsalUiRequiredException) {
                    result = await app.AcquireTokenInteractive(_scopes)
                        .ExecuteAsync();
                    if (string.Compare(result.Account.Username, _account, StringComparison.InvariantCultureIgnoreCase) != 0)
                        throw new Exception("Account mismatch");
                }

                // Create client
                _client = new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(new TokenProvider(result.AccessToken)));
                var drive = await _client.Me.Drive.GetAsync();
                _driveId = drive?.Id ?? throw new Exception("OneDrive drive not found");
            }

            return _client;
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
}