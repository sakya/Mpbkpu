using System.Reflection;
using MpbkpuLib.Abstracts;
using Sentry;

namespace MpbkpuLib.Utility;

/// <summary>
/// Sends unhandled exceptions and crashes to GlitchTip (Sentry protocol). No personal data: credentials written in
/// paths are masked, the user profile folder becomes "~", no breadcrumbs, user or machine name.
/// </summary>
public static class ErrorReporting
{
    private const string Dsn = "https://1e79fb74b82f483c8be9acd831c5d81d@glitchtip.pivault.org/7";

    /// <summary>
    /// Environment variable that turns the reports off when set to 0 (the CLI has no settings file)
    /// </summary>
    public const string EnvironmentVariable = "MPBKPU_ERROR_REPORTS";

    /// <summary>
    /// False to stop sending reports (the GUI setting); checked when an error is sent
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Start reporting the errors of the process
    /// </summary>
    /// <param name="app">The program sending the reports (tag "app")</param>
    /// <returns>Dispose it at exit to send the pending reports; null when the environment turns the reports off</returns>
    public static IDisposable? Init(string app)
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) == "0")
            return null;

        var res = SentrySdk.Init(o =>
        {
            o.Dsn = Dsn;
            o.Release = $"mpbkpu@{Assembly.GetEntryAssembly()?.GetName().Version}";
            o.SendDefaultPii = false;
            o.ServerName = string.Empty;
            o.MaxBreadcrumbs = 0;
            o.AutoSessionTracking = false;
            o.IsGlobalModeEnabled = true;
            o.SetBeforeSend(e => Enabled ? Scrub(e) : null);
        });
        SentrySdk.ConfigureScope(s => s.SetTag("app", app));
        return res;
    }

    /// <summary>
    /// Report an exception that was caught but ends the program (e.g. the main loop of the GUI)
    /// </summary>
    public static void Report(Exception ex)
    {
        SentrySdk.CaptureException(ex);
        SentrySdk.Flush(TimeSpan.FromSeconds(5));
    }

    private static SentryEvent Scrub(SentryEvent e)
    {
        foreach (var exception in e.SentryExceptions ?? [])
            exception.Value = Clean(exception.Value);
        if (e.Message != null) {
            e.Message.Message = Clean(e.Message.Message);
            e.Message.Formatted = Clean(e.Message.Formatted);
        }
        return e;
    }

    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string? Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        text = BaseJob.MaskCredentials(text);
        return string.IsNullOrEmpty(UserProfile) ? text : text.Replace(UserProfile, "~", StringComparison.OrdinalIgnoreCase);
    }
}
