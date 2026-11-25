using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using NextcloudClient.Models;
using Exception = System.Exception;

namespace NextcloudClient
{
    public partial class Client
    {
        private const string ResponsePage = @"<html>
<script type=""text/javascript"">
    function redirect() {
        // Append fragment as query string so that server can receive it.
        document.location.href = ""/token?url_with_fragment="" + encodeURIComponent(document.location.href);
    }
</script>
<body onload=""redirect()""/>
</html>";

        public async Task Login()
        {
            if (!string.IsNullOrEmpty(_settings.UserId) && !string.IsNullOrEmpty(_settings.Password)) {
                UserId = _settings.UserId;
                return;
            }

            if (!string.IsNullOrEmpty(_settings.AccessToken) && !string.IsNullOrEmpty(_settings.RefreshToken) && _settings.TokenExpiration != null) {
                AccessToken = _settings.AccessToken;
                RefreshToken = _settings.RefreshToken;
                TokenExpiration = _settings.TokenExpiration;
                try {
                    await CheckAccessToken(true);
                    return;
                } catch {
                    AccessToken = null;
                    RefreshToken = null;
                    TokenExpiration = null;
                }
            }

            using (var http = new HttpListener()) {
                http.Prefixes.Add($"{_settings.RedirectUri.Scheme}://{_settings.RedirectUri.Host}:{_settings.RedirectUri.Port}/");
                http.Start();

                var state = Guid.NewGuid().ToString();
                var url = $"{_settings.ServerUri}index.php/apps/oauth2/authorize?response_type=code&client_id={_settings.ClientId}&redirect_uri={HttpUtility.UrlEncode(_settings.RedirectUri.ToString())}&state={state}";
                OpenBrowser(url);
                await HandleOAuth2Redirect(http);
                var redirectUri = await HandleJsRedirect(http);
                http.Stop();
                var queryDictionary = HttpUtility.ParseQueryString(redirectUri.Query);
                if (queryDictionary["state"] != state)
                    throw new Exception("Invalid state");
                var code = queryDictionary["code"];

                await RequestToken(new System.Collections.Generic.Dictionary<string, string>()
                {
                    { "grant_type", "authorization_code" },
                    { "redirect_uri", _settings.RedirectUri.ToString() },
                    { "code", code }
                });
            }
        }

        #region private operations
        private async Task HandleOAuth2Redirect(HttpListener http)
        {
            var context = await http.GetContextAsync();
            while (context.Request.Url.AbsolutePath != _settings.RedirectUri.AbsolutePath) {
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
            var jsRedirectUri = new Uri($"{_settings.RedirectUri.Scheme}://{_settings.RedirectUri.Host}:{_settings.RedirectUri.Port}/token");
            var context = await http.GetContextAsync();
            while (context.Request.Url.AbsolutePath != jsRedirectUri.AbsolutePath) {
                context = await http.GetContextAsync();
            }
            var redirectUri = new Uri(context.Request.QueryString["url_with_fragment"]);
            using (var file = new MemoryStream(Encoding.UTF8.GetBytes(_settings.ResponsePage))) {
                await file.CopyToAsync(context.Response.OutputStream);
            }
            context.Response.OutputStream.Close();

            return redirectUri;
        }

        private static void OpenBrowser(string url)
        {
            using (var process = new Process()) {
                var startInfo = new ProcessStartInfo()
                {
                    CreateNoWindow = true
                };
                process.StartInfo = startInfo;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    startInfo.FileName = "xdg-open";
                    startInfo.Arguments = url;
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    startInfo.FileName = "open";
                    startInfo.Arguments = url;
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    startInfo.FileName = "cmd";
                    startInfo.Arguments = $"/c start {url.Replace("&", "^&")}";
                } else {
                    throw new Exception("Unsupported OS");
                }

                process.Start();
                process.WaitForExit();
            }
        }
        #endregion
    }
}