using System.Net;
using MpbkpuLib.Interfaces;
using System.Net.Http.Headers;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class OneDrive : IFileManager
    {
        #region classes
        private class AuthenticationProvider : IAuthenticationProvider
        {
            private readonly string _accessToken;

            public AuthenticationProvider(string accessToken)
            {
                _accessToken = accessToken;
            }

            public Task AuthenticateRequestAsync(HttpRequestMessage request)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                return Task.CompletedTask;
            }
        }

        private class WriterStream : Stream
        {
            private readonly Task? _writeTask;
            private long _pos;

            private readonly long _cacheSize = 1310720;
            private readonly List<byte> _buffer = new();
            private readonly SemaphoreSlim _bufferSema = new(1,1);

            public WriterStream(UploadSession session, long length)
            {
                Length = length;
                _writeTask = new LargeFileUploadTask<DriveItem>(session, this, (int)_cacheSize).UploadAsync();
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
                _writeTask?.Dispose();
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
                        Thread.Sleep(20);
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
                if (_writeTask != null)
                    await _writeTask;
            }
        }
        #endregion

        private GraphServiceClient? _client;
        private readonly string? _account;
        private readonly string[] _scopes = new[] { "User.Read", "files.readwrite", "offline_access" };
        private const string TenantId = "common";
        private const string AppName = "Mpbkpu";
        private const string AppId = "d7f0a79d-cfc0-4faa-9708-c0196c8eb26f";
        private const string RedirectUri = "http://localhost";

        public void Dispose()
        {
        }

        public bool CanOpenDatabase()
        {
            return false;
        }

        public bool SupportCompression()
        {
            return false;
        }

        public OneDrive(string? account)
        {
            if (string.IsNullOrEmpty(account))
                throw new ArgumentNullException(nameof(account));
            _account = account;
        }

        public string CombinePath(params string[] paths)
        {
            return Utility.Files.CombinePath('/', paths);
        }

        public async Task<File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            try {
                var item = await client.Drive.Root.ItemWithPath(GetPath(path)).Request().GetAsync();
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
            } catch (ServiceException ex) {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    throw new FileNotFoundException($"File {path} not found", path);
                throw;
            }
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            var page = await client.Drive.Root.ItemWithPath(GetPath(path)).Children.Request().GetAsync();
            while (page != null) {
                foreach (var item in page.CurrentPage) {
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

                page = page.NextPageRequest != null ? await page.NextPageRequest.GetAsync() : null;
            }
        }

        public async Task<bool> CreateDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            var parent = GetParent(GetPath(path), out var dirName);
            var folderDriveItem = new DriveItem()
            {
                Name = dirName,
                Folder = new Folder(),
            };

            if (string.IsNullOrEmpty(parent)) {
                await client.Drive.Root.Children.Request().AddAsync(folderDriveItem);
            } else {
                await client.Drive.Root.ItemWithPath(parent).Children.Request().AddAsync(folderDriveItem);
            }

            return true;
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            await client.Drive.Root.ItemWithPath(path).Request().DeleteAsync();
            return true;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            await client.Drive.Root.ItemWithPath(path).Request().DeleteAsync();
            return true;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            try {
                var item = await client.Drive.Root.ItemWithPath(GetPath(path)).Request().GetAsync();
                return item.Folder != null;
            } catch (ServiceException ex) {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;
                throw;
            }
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            try {
                var item = await client.Drive.Root.ItemWithPath(GetPath(path)).Request().GetAsync();
                return item.File != null;
            } catch (ServiceException ex) {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;
                throw;
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
                res = await client.Drive.Root.ItemWithPath(filePath).Content.Request().GetAsync();
            } else {
                if (size == null)
                    throw new ArgumentNullException(nameof(size));
                UploadSession uploadSession = await client.Me.Drive.Root.ItemWithPath(filePath).CreateUploadSession().Request().PostAsync();
                res = new WriterStream(uploadSession, size.Value);
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

        private async Task<GraphServiceClient> GetClient()
        {
            if (_client == null) {
                var appOptions = new PublicClientApplicationOptions()
                {
                    ClientName = AppName,
                    ClientId = AppId,
                    TenantId = TenantId,
                    RedirectUri = RedirectUri
                };
                var app = PublicClientApplicationBuilder.CreateWithApplicationOptions(appOptions).Build();

                // Create token cache
                var storageProperties = new StorageCreationPropertiesBuilder("UserTokenCache", Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu"))
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
                _client = new GraphServiceClient(new AuthenticationProvider(result.AccessToken));
            }

            return _client;
        }

        private string GetPath(string position)
        {
            if (position.StartsWith($"onedrive://"))
                return position.Remove(0, $"onedrive://".Length);

            return position;
        }

        private string GetParent(string position, out string? childName)
        {
            return Utility.Files.GetParent('/', GetPath(position), out childName);
        }
        #endregion
    }
}