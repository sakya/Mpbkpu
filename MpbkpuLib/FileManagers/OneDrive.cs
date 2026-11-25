using MpbkpuLib.Interfaces;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using MpbkpuLib.Utility;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class OneDrive : IFileManager, IOAuth2
    {
        public const string Scheme = "onedrive://";

        /// <summary>
        /// Upload slice size: must be a multiple of 320 KiB
        /// </summary>
        private const int UploadSliceSize = 4 * 320 * 1024;

        #region classes
        /// <summary>
        /// Gets a token from MSAL for every request: MSAL returns the cached token and refreshes it when it expires
        /// </summary>
        private class TokenProvider : IAccessTokenProvider
        {
            private readonly IPublicClientApplication _app;
            private readonly IAccount _account;
            private readonly string[] _scopes;

            public TokenProvider(IPublicClientApplication app, IAccount account, string[] scopes)
            {
                _app = app;
                _account = account;
                _scopes = scopes;
            }

            public AllowedHostsValidator AllowedHostsValidator { get; } = new();

            public async Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
            {
                var result = await _app.AcquireTokenSilent(_scopes, _account).ExecuteAsync(cancellationToken);
                return result.AccessToken;
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
            return Utility.Files.CombinePath(PathSeparator, paths);
        }

        public async Task<StorageSpace?> GetSpace(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            var quota = (await client.Drives[_driveId].GetAsync())?.Quota;
            return quota?.Remaining == null ? null : new StorageSpace(quota.Remaining.Value, quota.Total);
        }

        public async Task<File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var item = await GetItem(path);
            if (item?.File == null)
                throw new FileNotFoundException($"File {path} not found", path);
            return ToFile(item, path);
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var folder = await GetItem(path);
            if (folder?.Folder == null)
                throw new DirectoryNotFoundException($"Folder {path} not found");

            var children = (await GetClient()).Drives[_driveId].Items[folder.Id].Children;
            var page = await children.GetAsync();
            while (page != null) {
                foreach (var item in page.Value ?? new List<DriveItem>()) {
                    if (item.File == null && item.Folder == null)
                        continue;
                    // Full paths keep the form of the given path, so relative paths can be computed
                    yield return ToFile(item, CombinePath(path, item.Name!));
                }

                page = page.OdataNextLink != null ? await children.WithUrl(page.OdataNextLink).GetAsync() : null;
            }
        }

        public async Task<bool> CreateDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var existing = await GetItem(path);
            if (existing != null) {
                if (existing.Folder != null)
                    return true;
                throw new IOException($"A file named {path} already exists");
            }

            var parentPath = Utility.Files.GetParent(PathSeparator, GetPath(path), out var dirName);
            var parent = await GetItem(parentPath);
            if (parent?.Folder == null)
                throw new DirectoryNotFoundException($"Folder {parentPath} not found");

            await (await GetClient()).Drives[_driveId].Items[parent.Id].Children.PostAsync(new DriveItem()
            {
                Name = dirName,
                Folder = new Folder(),
            });
            return true;
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var item = await GetItem(path);
            if (item?.Folder == null)
                throw new DirectoryNotFoundException($"Folder {path} not found");
            // OneDrive moves deleted items to its recycle bin
            await (await GetClient()).Drives[_driveId].Items[item.Id].DeleteAsync();
            return true;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var item = await GetItem(path);
            if (item?.File == null)
                throw new FileNotFoundException($"File {path} not found", path);
            await (await GetClient()).Drives[_driveId].Items[item.Id].DeleteAsync();
            return true;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            return (await GetItem(path))?.Folder != null;
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            return (await GetItem(path))?.File != null;
        }

        public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);

            var client = await GetClient();
            if (!write) {
                var item = await GetItem(filePath);
                if (item?.File == null)
                    throw new FileNotFoundException($"File {filePath} not found", filePath);
                var content = await client.Drives[_driveId].Items[item.Id].Content.GetAsync()
                              ?? throw new IOException($"Cannot download {filePath}");
                return new FileSystemStream(this, content);
            }

            if (size == null)
                throw new ArgumentNullException(nameof(size));

            var parentPath = Utility.Files.GetParent(PathSeparator, GetPath(filePath), out var fileName);
            var parent = await GetItem(parentPath);
            if (parent?.Folder == null)
                throw new DirectoryNotFoundException($"Folder {parentPath} not found");
            var itemPath = GetItemPath(CombinePath(parentPath, fileName!));

            UploadStream res;
            if (size.Value == 0) {
                // Upload sessions do not accept empty files
                res = new UploadStream(0, (stream, token) =>
                    client.Drives[_driveId].Root.ItemWithPath(itemPath).Content.PutAsync(stream, cancellationToken: token));
            } else {
                var session = await client.Drives[_driveId].Root.ItemWithPath(itemPath).CreateUploadSession
                                  .PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody())
                              ?? throw new IOException("Failed to create upload session");
                res = new UploadStream(size.Value, async (stream, token) =>
                {
                    var task = new LargeFileUploadTask<DriveItem>(session, stream, UploadSliceSize, client.RequestAdapter);
                    var result = await task.UploadAsync(cancellationToken: token);
                    if (!result.UploadSucceeded)
                        throw new IOException($"Upload of {filePath} failed");
                });
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

            var item = await GetItem(filePath);
            if (item?.File == null)
                throw new FileNotFoundException($"File {filePath} not found", filePath);

            await (await GetClient()).Drives[_driveId].Items[item.Id].PatchAsync(new DriveItem { Name = newName });
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
                var storageProperties = SecretStore.CreateProperties("OneDriveUserTokenCache",
                    Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu"),
                    "MSAL token cache for Mpbkpu app.", "Mpbkpu_msal_service", "Mpbkpu_msal_account",
                    new KeyValuePair<string, string>("ProductGroup", "com.github.sakya"));
                var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
                cacheHelper.VerifyPersistence();
                cacheHelper.RegisterCache(app.UserTokenCache);

                // Sign in (silently when the account is in the cache)
                var accounts = await app.GetAccountsAsync();
                var account = accounts.FirstOrDefault(a => string.Compare(a.Username, _account, StringComparison.InvariantCultureIgnoreCase) == 0);
                AuthenticationResult result;
                try {
                    result = await app.AcquireTokenSilent(_scopes, account).ExecuteAsync();
                } catch (MsalUiRequiredException) {
                    result = await app.AcquireTokenInteractive(_scopes)
                        .WithLoginHint(_account)
                        .ExecuteAsync();
                }
                if (string.Compare(result.Account.Username, _account, StringComparison.InvariantCultureIgnoreCase) != 0)
                    throw new Exception("Account mismatch");

                // Create client: every request gets a valid token from MSAL
                _client = new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(new TokenProvider(app, result.Account, _scopes)));
                var drive = await _client.Me.Drive.GetAsync();
                _driveId = drive?.Id ?? throw new Exception("OneDrive drive not found");
            }

            return _client;
        }

        /// <summary>
        /// The item at a path, null if it does not exist. The root is addressed by id, other items by path;
        /// the other operations use the item id.
        /// </summary>
        private async Task<DriveItem?> GetItem(string path)
        {
            var client = await GetClient();
            var itemPath = GetItemPath(path);
            try {
                return itemPath.Length == 0
                    ? await client.Drives[_driveId].Items["root"].GetAsync()
                    : await client.Drives[_driveId].Root.ItemWithPath(itemPath).GetAsync();
            } catch (ODataError ex) when (ex.ResponseStatusCode == 404) {
                return null;
            }
        }

        /// <summary>
        /// Path relative to the root for ItemWithPath (it escapes the path), empty for the root
        /// </summary>
        private string GetItemPath(string path)
        {
            var segments = GetPath(path).Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 0 ? string.Empty : $"/{string.Join(PathSeparator, segments)}";
        }

        private static File ToFile(DriveItem item, string path)
        {
            return new File()
            {
                FullPath = path,
                Name = item.Name,
                Type = item.Folder != null ? File.FileTyes.Directory : File.FileTyes.File,
                Size = item.Folder != null ? 0 : item.Size ?? 0,
                Created = item.CreatedDateTime?.UtcDateTime,
                LastModified = item.LastModifiedDateTime?.UtcDateTime
            };
        }

        private string GetPath(string path)
        {
            return Utility.Files.GetPath(PathSeparator, Scheme, path);
        }
        #endregion
    }
}
