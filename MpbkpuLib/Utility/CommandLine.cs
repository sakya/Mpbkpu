using System.Globalization;
using System.IO.Compression;
using System.Text;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;

namespace MpbkpuLib.Utility;

/// <summary>
/// Builds the command line of the CLI (mpbkpu) that runs the same job, in PowerShell syntax. Secrets are never
/// written: the encryption password is asked by the CLI (-i), the other passwords and secrets are
/// <see cref="PasswordPlaceholder"/>. The option names must match Mpbkpu/Arguments.cs (checked by Tests/CommandLine.cs).
/// </summary>
public static class CommandLine
{
    public const string PasswordPlaceholder = "<password>";

    /// <param name="Command">The command line</param>
    /// <param name="HasPlaceholders">True when some secret has been replaced by <see cref="PasswordPlaceholder"/></param>
    public record Result(string Command, bool HasPlaceholders);

    /// <param name="executable">The CLI executable</param>
    /// <param name="encrypted">True when the backup is encrypted (the password may be unknown to the caller)</param>
    public static Result ForBackup(string executable, BackupJobSettings settings, bool encrypted)
    {
        var builder = new Builder(executable, "backup");
        builder.Add("-t", settings.BackupType.ToString());
        builder.AddPositions("source", "-s", [Source(settings.Source, settings.SourceUser, settings.SourcePassword, settings.SourceOauthSettings)]);
        builder.AddPositions("destination", "-d", settings.Destinations);
        builder.AddEach("-e", settings.Exclude);
        if (settings.CompressionLevel != CompressionLevel.NoCompression)
            builder.Add("-c", settings.CompressionLevel.ToString());
        builder.AddNumber("--retry", settings.RetryOnError, 3);
        builder.AddNumber("--fullEvery", settings.FullBackupEveryDays, 0);
        builder.AddNumber("--parallelFiles", settings.ParallelFiles, 1);
        builder.AddNumber("--keepChains", settings.KeepChains, 0);
        builder.AddFlag("--skipHash", settings.SkipHashCheck);
        builder.AddFlag("-i", encrypted);
        builder.AddFlag("-y", true);
        return builder.ToResult();
    }

    /// <param name="executable">The CLI executable</param>
    public static Result ForSync(string executable, SyncDirectoryJobSettings settings)
    {
        var builder = new Builder(executable, "syncDirectory");
        builder.AddPositions("source", "-s", [Source(settings.Source, settings.SourceUser, settings.SourcePassword, settings.SourceOauthSettings)]);
        builder.AddPositions("destination", "-d",
            [Source(settings.Destination, settings.DestinationUser, settings.DestinationPassword, settings.DestinationOauthSettings)]);
        builder.AddEach("-e", settings.Exclude);
        builder.AddNumber("--retry", settings.RetryOnError, 3);
        builder.AddFlag("--skipHash", settings.SkipHashCheck);
        builder.AddFlag("-y", true);
        return builder.ToResult();
    }

    private static BackupDestination Source(string? path, string? user, string? password, OAuth2Settings? oauth) =>
        new() { Path = path, User = user, Password = password, OauthSettings = oauth };

    private class Builder
    {
        private readonly StringBuilder _command = new();
        private bool _hasPlaceholders;

        public Builder(string executable, string verb)
        {
            // A quoted executable needs the call operator in PowerShell
            var quoted = Quote(executable);
            _command.Append(quoted == executable ? executable : $"& {quoted}");
            _command.Append(' ').Append(verb);
        }

        public void Add(string option, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                _command.Append(' ').Append(option).Append(' ').Append(Quote(value));
        }

        /// <summary>
        /// The option repeated for every value (the parser drops repeated equal values: callers pass distinct ones)
        /// </summary>
        public void AddEach(string option, IEnumerable<string>? values)
        {
            foreach (var value in values ?? [])
                Add(option, value);
        }

        public void AddNumber(string option, int value, int defaultValue)
        {
            if (value != defaultValue)
                Add(option, value.ToString(CultureInfo.InvariantCulture));
        }

        public void AddFlag(string option, bool value)
        {
            if (value)
                _command.Append(' ').Append(option);
        }

        /// <summary>
        /// Paths with their credentials: with more paths every credential value is "N=value" for the N-th path
        /// (Mpbkpu Arguments: the parser drops repeated values of an option)
        /// </summary>
        /// <param name="prefix">Prefix of the credential options: "source" or "destination"</param>
        public void AddPositions(string prefix, string pathOption, IReadOnlyList<BackupDestination> positions)
        {
            foreach (var position in positions)
                Add(pathOption, position.Path);

            OAuth2Settings? OAuth(BackupDestination p) => p.OauthSettings is { IsEmpty: false } ? p.OauthSettings : null;
            var indexed = positions.Count > 1;
            AddValues($"--{prefix}User", positions.Select(p => p.User).ToList(), false, indexed);
            AddValues($"--{prefix}Password", positions.Select(p => p.Password).ToList(), true, indexed);
            AddValues($"--{prefix}OauthServer", positions.Select(p => OAuth(p)?.ServerUrl?.ToString()).ToList(), false, indexed);
            AddValues($"--{prefix}OauthRedirect", positions.Select(p => OAuth(p)?.RedirectUrl?.ToString()).ToList(), false, indexed);
            AddValues($"--{prefix}OauthApp", positions.Select(p => OAuth(p)?.AppName).ToList(), false, indexed);
            AddValues($"--{prefix}OauthAppId", positions.Select(p => OAuth(p)?.AppId).ToList(), false, indexed);
            AddValues($"--{prefix}OauthSecret", positions.Select(p => OAuth(p)?.AppSecret).ToList(), true, indexed);
        }

        private void AddValues(string option, IReadOnlyList<string?> values, bool secret, bool indexed)
        {
            for (var i = 0; i < values.Count; i++) {
                if (string.IsNullOrEmpty(values[i]))
                    continue;
                var value = secret ? PasswordPlaceholder : values[i];
                Add(option, indexed ? $"{i + 1}={value}" : value);
                _hasPlaceholders |= secret;
            }
        }

        public Result ToResult() => new(_command.ToString(), _hasPlaceholders);
    }

    /// <summary>
    /// A PowerShell argument: single quoted (quotes doubled) unless it has only safe characters
    /// </summary>
    private static string Quote(string value)
    {
        return value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '\\' or '/') ?
            value :
            $"'{value.Replace("'", "''")}'";
    }
}
