using System.Globalization;
using System.Net.Http.Headers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using Newtonsoft.Json.Linq;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

/// <summary>
/// pCloud (https://docs.pcloud.com): HTTP JSON API, items addressed by path, OAuth 2.0 code flow with the app of the
/// user. The access token does not expire; the account lives in a data center (US or Europe) whose API server is
/// returned by the login.
/// </summary>
public class PCloud : IFileManager, IOAuth2
{
    public const string Scheme = "pcloud://";

    /// <summary>
    /// The API servers: US and Europe. The server returned by the login must be one of them, so the token is never
    /// sent elsewhere.
    /// </summary>
    public static readonly string[] ApiHosts = ["api.pcloud.com", "eapi.pcloud.com"];

    private const string AuthorizeUrl = "https://my.pcloud.com/oauth2/authorize";

    /// <summary>
    /// Result codes of a missing item: parent folder missing, folder missing, file missing
    /// </summary>
    private static readonly int[] NotFoundCodes = [2002, 2005, 2009];

    private readonly string _tokenCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu", "PCloudTokenCache");
    private readonly string _account;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private string? _accessToken;
    private string _apiHost = ApiHosts[0];

    public PCloud(string? account)
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

    /// <summary>
    /// An error returned by the API (result code other than 0)
    /// </summary>
    public class ApiException(int result, string? message) : Exception($"pCloud error {result}: {message}")
    {
        public int Result { get; } = result;
        public bool IsNotFound => NotFoundCodes.Contains(Result);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    // Not verified: names that differ only in case are treated as different
    public bool IsCaseSensitive => true;

    public string GetScheme()
    {
        return Scheme;
    }

    public char PathSeparator => '/';

    public bool CanOpenDatabase => false;

    // Uploads declare their size (UploadStream): the size of a compressed file is not known in advance
    public bool SupportCompression => false;

    public string CombinePath(params string[] paths)
    {
        return Files.CombinePath(PathSeparator, paths);
    }

    public async Task<StorageSpace?> GetSpace(string? path)
    {
        var info = await Call("userinfo", new());
        var quota = info.Value<long>("quota");
        return new StorageSpace(Math.Max(0, quota - info.Value<long>("usedquota")), quota);
    }

    public async Task<File> GetFileInfo(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var item = await TryCall("stat", new() { ["path"] = GetPath(path) });
        if (item?["metadata"] is not JObject metadata || metadata.Value<bool>("isfolder"))
            throw new FileNotFoundException($"File {path} not found", path);
        return ToFile(metadata, path);
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var res = await TryCall("listfolder", new() { ["path"] = GetPath(path) });
        if (res?["metadata"]?["contents"] is not JArray contents)
            throw new DirectoryNotFoundException($"Folder {path} not found");
        foreach (var item in contents.OfType<JObject>()) {
            // Full paths keep the form of the given path, so relative paths can be computed
            var file = ToFile(item, CombinePath(path, item.Value<string>("name")!));
            if (includeHidden || !file.Hidden)
                yield return file;
        }
    }

    public async Task<bool> CreateDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        await Call("createfolderifnotexists", new() { ["path"] = GetPath(path) });
        return true;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        if (await TryCall("deletefolderrecursive", new() { ["path"] = GetPath(path) }) == null)
            throw new DirectoryNotFoundException($"Folder {path} not found");
        return true;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        if (await TryCall("deletefile", new() { ["path"] = GetPath(path) }) == null)
            throw new FileNotFoundException($"File {path} not found", path);
        return true;
    }

    public async Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        return await TryCall("listfolder", new() { ["path"] = GetPath(path), ["nofiles"] = "1" }) != null;
    }

    public async Task<bool> FileExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var res = await TryCall("stat", new() { ["path"] = GetPath(path) });
        return res?["metadata"] is JObject metadata && !metadata.Value<bool>("isfolder");
    }

    public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null, DateTime? lastModified = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);

        if (!write) {
            var link = await TryCall("getfilelink", new() { ["path"] = GetPath(filePath) });
            if (link?["hosts"] is not JArray { Count: > 0 } hosts)
                throw new FileNotFoundException($"File {filePath} not found", filePath);
            var response = await _http.GetAsync($"https://{hosts[0]}{link.Value<string>("path")}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            return new FileSystemStream(this, await response.Content.ReadAsStreamAsync());
        }

        if (size == null)
            throw new ArgumentNullException(nameof(size));

        // Overwrite like a file system
        if (await FileExists(filePath))
            await DeleteFile(filePath);
        var parent = Files.GetParent(PathSeparator, GetPath(filePath), out var name);
        var parameters = new Dictionary<string, string?>()
        {
            ["path"] = parent.Length == 0 ? "/" : parent,
            ["filename"] = name,
            // A failed upload leaves no partial file
            ["nopartial"] = "1",
            ["mtime"] = lastModified == null ? null : new DateTimeOffset(DateTime.SpecifyKind(lastModified.Value, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        };
        var res = new UploadStream(size.Value, async (stream, token) =>
        {
            using var content = new MultipartFormDataContent();
            var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "file", name!);
            await Call("uploadfile", parameters, content, token);
        });
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

        var path = GetPath(filePath);
        var parent = Files.GetParent(PathSeparator, path, out _);
        var target = $"{parent}{PathSeparator}{newName}";
        if (await FileExists(target))
            throw new IOException($"Failed to rename file {filePath}: a file named {newName} already exists");
        if (await TryCall("renamefile", new() { ["path"] = path, ["topath"] = target }) == null)
            throw new FileNotFoundException($"File {filePath} not found", filePath);
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

    /// <summary>
    /// The date of an item: RFC 1123 with offset ("Sun, 16 Mar 2014 17:26:04 +0000"), returned as UTC
    /// </summary>
    public static DateTime? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ?
            date.UtcDateTime :
            null;
    }

    #region private operations
    /// <summary>
    /// Call an API method, failing with <see cref="ApiException"/> when the result is not 0
    /// </summary>
    private async Task<JObject> Call(string method, Dictionary<string, string?> parameters, HttpContent? content = null,
        CancellationToken token = default)
    {
        await Login();
        var query = string.Join("&", parameters.Where(p => p.Value != null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}")
            .Prepend($"access_token={Uri.EscapeDataString(_accessToken!)}"));
        var url = $"https://{_apiHost}/{method}?{query}";
        using var response = content == null ? await _http.GetAsync(url, token) : await _http.PostAsync(url, content, token);
        response.EnsureSuccessStatusCode();
        return CheckResult(JObject.Parse(await response.Content.ReadAsStringAsync(token)));
    }

    /// <summary>
    /// <see cref="Call"/>, null when the item does not exist
    /// </summary>
    private async Task<JObject?> TryCall(string method, Dictionary<string, string?> parameters)
    {
        try {
            return await Call(method, parameters);
        } catch (ApiException ex) when (ex.IsNotFound) {
            return null;
        }
    }

    private static JObject CheckResult(JObject json)
    {
        var result = json.Value<int?>("result") ?? 0;
        if (result != 0)
            throw new ApiException(result, json.Value<string>("error"));
        return json;
    }

    /// <summary>
    /// Read the saved token, or log in with the browser (OAuth code flow) the first time
    /// </summary>
    private async Task Login()
    {
        if (_accessToken != null)
            return;
        if (RedirectUrl == null)
            throw new Exception($"{nameof(RedirectUrl)} cannot be empty");
        if (string.IsNullOrEmpty(AppId))
            throw new Exception($"{nameof(AppId)} cannot be empty");
        if (string.IsNullOrEmpty(AppSecret))
            throw new Exception($"{nameof(AppSecret)} cannot be empty");

        var cache = new OAuthTokenCache(_tokenCache);
        var saved = await cache.GetToken(_account);
        if (saved?.AccessToken != null && ApiHosts.Contains(saved.ApiHost)) {
            _accessToken = saved.AccessToken;
            _apiHost = saved.ApiHost!;
            return;
        }

        var state = Guid.NewGuid().ToString("N");
        var authorizeUri = new Uri($"{AuthorizeUrl}?client_id={Uri.EscapeDataString(AppId)}&response_type=code" +
                                   $"&redirect_uri={Uri.EscapeDataString(RedirectUrl.ToString())}&state={state}");
        var redirect = await OAuthBrowserLogin.GetRedirectUri(authorizeUri, RedirectUrl);
        if (OAuthBrowserLogin.GetParameter(redirect, "state") != state)
            throw new Exception("Invalid OAuth response (state)");
        var code = OAuthBrowserLogin.GetParameter(redirect, "code") ??
                   throw new Exception($"pCloud login failed: {OAuthBrowserLogin.GetParameter(redirect, "error") ?? "no code"}");
        var apiHost = GetApiHost(OAuthBrowserLogin.GetParameter(redirect, "hostname"), OAuthBrowserLogin.GetParameter(redirect, "locationid"));

        using (var response = await _http.GetAsync($"https://{apiHost}/oauth2_token?client_id={Uri.EscapeDataString(AppId)}" +
                                                   $"&client_secret={Uri.EscapeDataString(AppSecret)}&code={Uri.EscapeDataString(code)}")) {
            response.EnsureSuccessStatusCode();
            var json = CheckResult(JObject.Parse(await response.Content.ReadAsStringAsync()));
            _accessToken = json.Value<string>("access_token") ?? throw new Exception("pCloud login failed: no access token");
        }
        _apiHost = apiHost;

        // The account given in the path must be the one that logged in
        var email = (await Call("userinfo", new())).Value<string>("email");
        if (!string.Equals(email, _account, StringComparison.OrdinalIgnoreCase)) {
            _accessToken = null;
            throw new Exception($"Wrong account: logged in as {email}, the path is for {_account}");
        }

        await cache.SaveToken(_account, _accessToken!, null, null, _apiHost);
    }

    /// <summary>
    /// The API server of the account from the login redirect: its host name, else its location (1 US, 2 Europe).
    /// Only the known servers are accepted.
    /// </summary>
    public static string GetApiHost(string? hostname, string? locationId)
    {
        if (!string.IsNullOrEmpty(hostname))
            return ApiHosts.Contains(hostname) ? hostname : throw new Exception($"Unknown pCloud API server {hostname}");
        return locationId == "2" ? ApiHosts[1] : ApiHosts[0];
    }

    private File ToFile(JObject item, string path)
    {
        var name = item.Value<string>("name")!;
        var isFolder = item.Value<bool>("isfolder");
        return new File()
        {
            FullPath = path,
            Name = name,
            Type = isFolder ? File.FileTyes.Directory : File.FileTyes.File,
            Size = isFolder ? 0 : item.Value<long>("size"),
            Created = ParseDate(item.Value<string>("created")),
            LastModified = ParseDate(item.Value<string>("modified")),
            Hidden = name.StartsWith('.')
        };
    }

    /// <summary>
    /// Path for the API: "/" for the root, "/a/b" otherwise (no trailing separator)
    /// </summary>
    private string GetPath(string path)
    {
        var segments = Files.GetPath(PathSeparator, Scheme, path).Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return $"{PathSeparator}{string.Join(PathSeparator, segments)}";
    }
    #endregion
}
