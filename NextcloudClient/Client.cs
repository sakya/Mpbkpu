using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;
using Newtonsoft.Json;
using NextcloudClient.Exceptions;
using NextcloudClient.Interfaces;
using NextcloudClient.Models;

namespace NextcloudClient
{
    /// <summary>
    /// The client to access Nextcloud
    /// https://docs.nextcloud.com/server/latest/developer_manual/client_apis/WebDAV/basic.html
    /// </summary>
    public partial class Client : IDisposable
    {
        private readonly IClientSettings _settings;
        private readonly HttpClient _httpClient;

        public const string PropfindDoc = @"<?xml version=""1.0""?>
<d:propfind xmlns:d=""DAV:"" xmlns:oc=""http://owncloud.org/ns"" xmlns:nc=""http://nextcloud.org/ns"">
  <d:prop>
        <d:getlastmodified />
        <d:getetag />
        <d:getcontenttype />
        <d:resourcetype />
        <oc:id />
        <oc:fileid />
        <oc:permissions />
        <oc:size />
        <d:getcontentlength />
        <nc:has-preview />
        <oc:favorite />
        <oc:comments-href />
        <oc:comments-count />
        <oc:comments-unread />
        <oc:owner-id />
        <oc:owner-display-name />
        <oc:share-types />
        <nc:contained-folder-count />
        <nc:contained-file-count />
  </d:prop>
</d:propfind>";

        public Client(IClientSettings settings)
        {
            _settings = settings;
            _httpClient = new HttpClient();
            ValidateSettings();
        }

        public string UserId { get; private set; }
        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }
        public DateTime? TokenExpiration { get; private set; }

        public Uri ServerUri => _settings.ServerUri;

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        public async Task<Stream> GetDownloadStream(Uri uri)
        {
            await CheckAccessToken();
            using (var httpClient = new HttpClient()) {
                AddAuthenticationHeader(httpClient.DefaultRequestHeaders);
                return await httpClient.GetStreamAsync(uri);
            }
        }

        public async Task UploadStream(Uri uri, Stream stream)
        {
            await CheckAccessToken();
            using (var httpClient = new HttpClient()) {
                AddAuthenticationHeader(httpClient.DefaultRequestHeaders);
                var fileStreamContent = new StreamContent(stream);
                await httpClient.PutAsync(uri, fileStreamContent);
            }
        }

        public async Task<T> CallApi<T>(HttpMethod method, Uri uri, XDocument data = null, Dictionary<string, string> headers = null)
        {
            await CheckAccessToken();
            using (var request = new HttpRequestMessage()) {
                request.RequestUri = uri;
                request.Method = method;
                AddAuthenticationHeader(request.Headers);
                if (headers != null) {
                    foreach (var kvp in headers)
                        request.Headers.Add(kvp.Key, kvp.Value);
                }
                if (data != null)
                    request.Content = new StringContent(data.ToString());
                using (var res = await _httpClient.SendAsync(request)) {
                    string resContent;
                    using (var stream = await res.Content.ReadAsStreamAsync()) {
                        using (var sr = new StreamReader(stream)) {
                            resContent = await sr.ReadToEndAsync();
                        }
                    }

                    if (!res.IsSuccessStatusCode) {
                        var err = Deserialize<Error>(resContent);
                        throw new ApiException(res.StatusCode, $"Error: { err.Message ?? err.Exception}");
                    }

                    return Deserialize<T>(resContent);
                }
            }
        }

        #region private oeprations
        private void ValidateSettings()
        {
            if (_settings.ServerUri == null)
                throw new ArgumentNullException(nameof(_settings.ServerUri));

            if (string.IsNullOrEmpty(_settings.Password)) {
                if (_settings.RedirectUri == null)
                    throw new ArgumentNullException(nameof(_settings.RedirectUri));
                if (string.IsNullOrEmpty(_settings.ClientId))
                    throw new ArgumentNullException(nameof(_settings.ClientId));
                if (string.IsNullOrEmpty(_settings.ClientSecret))
                    throw new ArgumentNullException(nameof(_settings.ClientSecret));
            }

            if (!_settings.ServerUri.ToString().EndsWith("/"))
                _settings.ServerUri = new Uri($"{_settings.ServerUri}/");
        }

        private void AddAuthenticationHeader(HttpRequestHeaders headers)
        {
            if (!string.IsNullOrEmpty(AccessToken))
                headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            else {
                var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.UserId}:{_settings.Password}"));
                headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            }
        }

        private async Task CheckAccessToken(bool forceRefresh = false)
        {
            if (!string.IsNullOrEmpty(_settings.UserId) && !string.IsNullOrEmpty(_settings.Password))
                return;

            if (string.IsNullOrEmpty(AccessToken))
                throw new Exception("Not logged in");

            if (forceRefresh || TokenExpiration <= DateTime.UtcNow.AddMinutes(-1)) {
                var token = await GetPostResponse<AuthTokenModel>(
                    $"{_settings.ServerUri}index.php/apps/oauth2/api/v1/token?&grant_type=refresh_token&refresh_token={RefreshToken}&client_id={_settings.ClientId}&client_secret={_settings.ClientSecret}",
                    null);
                UserId = token.UserId;
                AccessToken = token.AccessToken;
                RefreshToken = token.RefreshToken;
                TokenExpiration = DateTime.Now.AddSeconds(token.ExpiresIn);
            }
        }

        private T Deserialize<T>(string content)
        {
            if (string.IsNullOrEmpty(content) && typeof(T) == typeof(string)) {
                return default;
            }

            var ser = new XmlSerializer(typeof(T), "DAV:");
            using (var sr = new StringReader(content)) {
                return (T)ser.Deserialize(sr);
            }
        }

        private Task<T> GetPostResponse<T>(string uri, HttpContent content)
        {
            return GetPostResponse<T>(new Uri(uri), content);
        }

        private async Task<T> GetPostResponse<T>(Uri uri, HttpContent content)
        {
            using (var res = await _httpClient.PostAsync(uri, content)) {
                string resContent;
                using (var stream = await res.Content.ReadAsStreamAsync()) {
                    using (var sr = new StreamReader(stream)) {
                        resContent = await sr.ReadToEndAsync();
                    }
                }
                if (!res.IsSuccessStatusCode)
                    throw new Exception($"Error: {resContent}");

                return JsonConvert.DeserializeObject<T>(resContent);
            }
        }
        #endregion
    }
}