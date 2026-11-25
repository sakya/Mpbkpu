using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        <d:quota-available-bytes />
        <d:quota-used-bytes />
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
            // No timeout: uploads and downloads of large files can take a long time
            _httpClient = new HttpClient() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            ValidateSettings();
        }

        public string UserId { get; private set; }
        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }
        public DateTime? TokenExpiration { get; private set; }

        /// <summary>
        /// Called after the OAuth tokens have been refreshed: the old refresh token is no longer valid
        /// </summary>
        public Func<Task> TokensRefreshed { get; set; }

        public Uri ServerUri => _settings.ServerUri;

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        /// <summary>
        /// Get the WebDAV URL of a path of the user's files (each segment is escaped)
        /// </summary>
        public string GetDavUrl(string path)
        {
            var segments = (path ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return $"{ServerUri}remote.php/dav/files/{Uri.EscapeDataString(UserId)}/{string.Join("/", segments.Select(Uri.EscapeDataString))}";
        }

        /// <summary>
        /// Convert a WebDAV href (/remote.php/dav/files/user/path) to the path of the user's files (/path)
        /// </summary>
        public string GetPathFromHref(string href)
        {
            var decoded = Uri.UnescapeDataString(href ?? string.Empty);
            var prefix = $"/remote.php/dav/files/{UserId}";
            var idx = decoded.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                throw new Exception($"Unexpected href {href}");
            return $"/{decoded.Substring(idx + prefix.Length).Trim('/')}";
        }

        public async Task<Stream> GetDownloadStream(Uri uri)
        {
            var res = await Send(() => new HttpRequestMessage(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, true);
            if (!res.IsSuccessStatusCode) {
                var status = res.StatusCode;
                res.Dispose();
                throw new ApiException(status, $"Error: download failed with status {(int)status}");
            }
            // Disposing the stream releases the response
            return await res.Content.ReadAsStreamAsync();
        }

        public async Task UploadStream(Uri uri, Stream stream, long length)
        {
            var content = new StreamContent(stream);
            content.Headers.ContentLength = length;
            // The stream can be read only once: no retry
            using (var res = await Send(() => new HttpRequestMessage(HttpMethod.Put, uri) { Content = content },
                       HttpCompletionOption.ResponseContentRead, false)) {
                if (!res.IsSuccessStatusCode)
                    throw new ApiException(res.StatusCode, $"Error: upload failed with status {(int)res.StatusCode}: {await ReadError(res)}");
            }
        }

        public async Task<T> CallApi<T>(HttpMethod method, Uri uri, XDocument data = null, Dictionary<string, string> headers = null)
        {
            var requestData = data?.ToString();
            using (var res = await Send(() =>
                   {
                       var request = new HttpRequestMessage(method, uri);
                       if (headers != null) {
                           foreach (var kvp in headers)
                               request.Headers.Add(kvp.Key, kvp.Value);
                       }
                       if (requestData != null)
                           request.Content = new StringContent(requestData);
                       return request;
                   }, HttpCompletionOption.ResponseContentRead, true)) {
                if (!res.IsSuccessStatusCode)
                    throw new ApiException(res.StatusCode, $"Error: {await ReadError(res)}");

                return Deserialize<T>(await res.Content.ReadAsStringAsync());
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

        /// <summary>
        /// Send a request with the authentication header. With OAuth a 401 refreshes the token and, if allowed,
        /// sends the request again.
        /// </summary>
        private async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> createRequest, HttpCompletionOption option, bool allowRetry)
        {
            await CheckAccessToken();
            var request = createRequest();
            AddAuthenticationHeader(request.Headers);
            var res = await _httpClient.SendAsync(request, option);
            if (res.StatusCode != HttpStatusCode.Unauthorized || !allowRetry || string.IsNullOrEmpty(AccessToken))
                return res;

            res.Dispose();
            await CheckAccessToken(true);
            request = createRequest();
            AddAuthenticationHeader(request.Headers);
            return await _httpClient.SendAsync(request, option);
        }

        /// <summary>
        /// The error message of a failed response (WebDAV XML error, or the raw content)
        /// </summary>
        private async Task<string> ReadError(HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync();
            try {
                var err = Deserialize<Error>(content);
                return err.Message ?? err.Exception ?? res.ReasonPhrase;
            } catch {
                return string.IsNullOrEmpty(content) ? res.ReasonPhrase : content;
            }
        }

        /// <summary>
        /// Request an OAuth token: parameters in the body, so secrets never end up in URLs
        /// </summary>
        private async Task RequestToken(Dictionary<string, string> parameters)
        {
            parameters["client_id"] = _settings.ClientId;
            parameters["client_secret"] = _settings.ClientSecret;
            var token = await GetPostResponse<AuthTokenModel>($"{_settings.ServerUri}index.php/apps/oauth2/api/v1/token",
                new FormUrlEncodedContent(parameters));
            AccessToken = token.AccessToken;
            RefreshToken = token.RefreshToken;
            UserId = token.UserId;
            TokenExpiration = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
        }

        private void AddAuthenticationHeader(HttpRequestHeaders headers)
        {
            if (!string.IsNullOrEmpty(AccessToken))
                headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            else {
                var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.UserId}:{_settings.Password}"));
                headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            }
        }

        private async Task CheckAccessToken(bool forceRefresh = false)
        {
            if (!string.IsNullOrEmpty(_settings.UserId) && !string.IsNullOrEmpty(_settings.Password))
                return;

            if (string.IsNullOrEmpty(AccessToken))
                throw new Exception("Not logged in");

            // Refresh before the expiration
            if (forceRefresh || TokenExpiration == null || TokenExpiration <= DateTime.UtcNow.AddMinutes(1)) {
                await RequestToken(new Dictionary<string, string>()
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", RefreshToken }
                });
                if (TokensRefreshed != null)
                    await TokensRefreshed();
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