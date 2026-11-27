using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MpbkpuGUI.Utility;

/// <summary>
/// Checks the latest release published on GitHub and installs it with its setup program (Windows installations only)
/// </summary>
public static class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/sakya/Mpbkpu/releases/latest";
    private const string DownloadHost = "github.com";

    /// <param name="Version">The version of the release</param>
    /// <param name="PageUrl">The release page</param>
    /// <param name="InstallerUrl">The setup program, null when the release has none</param>
    /// <param name="InstallerName">The file name of the setup program</param>
    /// <param name="InstallerSha256">SHA-256 of the setup program published by GitHub (hex), null when unknown</param>
    public record Release(Version Version, string PageUrl, string? InstallerUrl, string? InstallerName, string? InstallerSha256);

    private static readonly HttpClient Client = CreateClient();

    /// <summary>
    /// The release found by the last check, when newer than the running version
    /// </summary>
    public static Release? Available { get; private set; }

    /// <summary>
    /// Raised (on any thread) when a check finds a newer release
    /// </summary>
    public static event EventHandler? UpdateAvailable;

    public static Version CurrentVersion => Normalize(Assembly.GetEntryAssembly()!.GetName().Version ?? new Version(0, 0));

    /// <summary>
    /// True when the program was installed by the setup program, which can then update it
    /// </summary>
    public static bool CanInstall =>
        OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub requires a user agent
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Mpbkpu/{CurrentVersion}");
        return client;
    }

    /// <summary>
    /// Read the latest release (drafts and pre-releases excluded)
    /// </summary>
    /// <returns>The release when it is newer than the running version, else null</returns>
    public static async Task<Release?> Check(CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(token));

        var tag = json.Value<string>("tag_name")?.TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var version))
            throw new InvalidDataException($"Invalid release version: {tag}");
        version = Normalize(version);

        var installer = (json["assets"] as JArray)?
            .OfType<JObject>()
            .FirstOrDefault(a => a.Value<string>("name") is { } n &&
                                 n.StartsWith("Mpbkpu-", StringComparison.OrdinalIgnoreCase) &&
                                 n.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));
        var digest = installer?.Value<string>("digest");
        var release = new Release(version,
            json.Value<string>("html_url") ?? "https://github.com/sakya/Mpbkpu/releases",
            installer?.Value<string>("browser_download_url"),
            installer?.Value<string>("name"),
            digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null);

        if (version <= CurrentVersion)
            return null;
        Available = release;
        UpdateAvailable?.Invoke(null, EventArgs.Empty);
        return release;
    }

    /// <summary>
    /// Download the setup program of a release into a temporary folder and check its SHA-256
    /// </summary>
    /// <param name="progress">Percentage of the download</param>
    /// <returns>The path of the setup program</returns>
    public static async Task<string> DownloadInstaller(Release release, IProgress<double>? progress, CancellationToken token)
    {
        if (release.InstallerUrl == null || release.InstallerName == null)
            throw new InvalidOperationException("The release has no setup program");
        // Never run a file from elsewhere, nor one whose content cannot be checked
        var uri = new Uri(release.InstallerUrl);
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, DownloadHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unexpected download address: {uri}");
        if (string.IsNullOrEmpty(release.InstallerSha256))
            throw new InvalidDataException("The release does not publish the SHA-256 of the setup program");

        var folder = Path.Combine(Path.GetTempPath(), "MpbkpuUpdate");
        Directory.CreateDirectory(folder);
        // Setup programs of earlier updates (one still running cannot be deleted)
        foreach (var old in Directory.GetFiles(folder)) {
            try {
                File.Delete(old);
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }
        var path = Path.Combine(folder, Path.GetFileName(release.InstallerName));

        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        try {
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var file = File.Create(path)) {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0) {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    if (total > 0)
                        progress?.Report(done * 100.0 / total.Value);
                }
            }

            await using (var file = File.OpenRead(path)) {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!string.Equals(hash, release.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded setup program is damaged (SHA-256 mismatch)");
            }
        } catch {
            File.Delete(path);
            throw;
        }
        return path;
    }

    /// <summary>
    /// Start the setup program to update this installation; the caller must then close the program.
    /// The setup closes the programs still running, keeps the installation folder and mode and starts the GUI again.
    /// </summary>
    public static void StartInstaller(string path)
    {
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /update=1")
        {
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Missing parts count as 0, so 0.2.0 equals 0.2.0.0
    /// </summary>
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
