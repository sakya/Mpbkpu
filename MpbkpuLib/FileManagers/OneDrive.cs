using System.Net;
using MpbkpuLib.Interfaces;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Newtonsoft.Json;

namespace MpbkpuLib.FileManagers
{
    public class OneDrive : IFileManager
    {
        #region classes
        private class AuthenticationProvider : IAuthenticationProvider
        {
            private string _accessToken;

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
        #endregion

        private GraphServiceClient? _client;
        private readonly string? _account;
        private readonly string[] _scopes = new[] { "User.Read", "files.readwrite", "offline_access" };
        private const string TenantId = "common";
        private const string AppName = "MicrosoftGraphTest";
        private const string AppId = "d7f0a79d-cfc0-4faa-9708-c0196c8eb26f";
        private const string RedirectUri = "http://localhost";

        public void Dispose()
        {

        }

        public bool CanOpenDatabase()
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
            string res = string.Empty;
            foreach (var p in paths) {
                if (string.IsNullOrEmpty(p))
                    throw new Exception("Path cannot be empty");

                var part = p;
                if (!part.EndsWith("/"))
                    part = $"{part}/";
                res = $"{res}{part}";
            }

            return res;
        }

        public IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> CreateDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            throw new NotImplementedException();
        }

        public Task<bool> DeleteDirectory(string? path)
        {
            throw new NotImplementedException();
        }

        public Task<bool> DeleteFile(string? path)
        {
            throw new NotImplementedException();
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

        public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CloseFile(Stream stream)
        {
            throw new NotImplementedException();
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
                var storageProperties = new StorageCreationPropertiesBuilder("UserTokenCache.txt", "./cache")
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
            if (position.StartsWith($"onedrive://{_account}"))
                return position.Remove(0, $"onedrive://{_account}".Length);

            return position;
        }
        #endregion
    }
}