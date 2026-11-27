using System.Net;
using System.Text;

namespace MpbkpuLib.Utility;

/// <summary>
/// The browser part of an OAuth 2.0 login: open the authorization page in the browser and wait for the redirect on a
/// local <see cref="HttpListener"/>. A page on the redirect url turns the fragment into a query string, so the result
/// is the full redirect url whether the service returns its values in the query (code flow) or in the fragment.
/// </summary>
public static class OAuthBrowserLogin
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
    private const string TokenResponsePage = @"<html>
<body style=""height: 100%; width: 100%;"">
  <div style=""display: flex; display: flex; justify-content: center;align-items: center;height: 100%;"">
    <h2>You can close this tab</h2>
  </div>
</body>
</html>";

    /// <summary>
    /// Open <paramref name="authorizeUri"/> in the browser and return the url the service redirected to
    /// </summary>
    /// <param name="redirectUrl">The redirect url registered for the app, on this computer (e.g. http://localhost:port/)</param>
    public static async Task<Uri> GetRedirectUri(Uri authorizeUri, Uri redirectUrl)
    {
        using var http = new HttpListener();
        http.Prefixes.Add($"{redirectUrl.Scheme}://{redirectUrl.Host}:{redirectUrl.Port}/");
        http.Start();
        try {
            Browser.Open(authorizeUri.ToString());
            await HandleRedirect(http, redirectUrl);
            return await HandleJsRedirect(http, redirectUrl);
        } finally {
            http.Stop();
        }
    }

    /// <summary>
    /// The value of a parameter of a redirect url, from its query
    /// </summary>
    public static string? GetParameter(Uri redirectUri, string name)
    {
        return System.Web.HttpUtility.ParseQueryString(redirectUri.Query)[name] ??
               System.Web.HttpUtility.ParseQueryString(redirectUri.Fragment.TrimStart('#'))[name];
    }

    private static async Task HandleRedirect(HttpListener http, Uri redirectUrl)
    {
        var context = await http.GetContextAsync();
        while (context.Request.Url!.AbsolutePath != redirectUrl.AbsolutePath) {
            context = await http.GetContextAsync();
        }

        context.Response.ContentType = "text/html";
        using (var file = new MemoryStream(Encoding.UTF8.GetBytes(ResponsePage))) {
            await file.CopyToAsync(context.Response.OutputStream);
        }

        context.Response.OutputStream.Close();
    }

    private static async Task<Uri> HandleJsRedirect(HttpListener http, Uri redirectUrl)
    {
        var jsRedirectUri = new Uri($"{redirectUrl.Scheme}://{redirectUrl.Host}:{redirectUrl.Port}/token");
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
}
