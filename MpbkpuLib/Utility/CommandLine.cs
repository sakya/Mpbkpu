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
    /// <param name="UnsupportedPaths">Destinations whose credentials the CLI cannot take (OAuth settings or a password
    /// that cannot be written in the path of a destination after the first one)</param>
    public record Result(string Command, bool HasPlaceholders, IReadOnlyList<string> UnsupportedPaths);

    /// <param name="executable">The CLI executable</param>
    /// <param name="encrypted">True when the backup is encrypted (the password may be unknown to the caller)</param>
    public static Result ForBackup(string executable, BackupJobSettings settings, bool encrypted)
    {
        var builder = new Builder(executable, "backup");
        builder.Add("-t", settings.BackupType.ToString());
        builder.AddPosition("source", "-s", settings.Source, settings.SourceUser, settings.SourcePassword, settings.SourceOauthSettings);
        for (var i = 0; i < settings.Destinations.Count; i++) {
            var destination = settings.Destinations[i];
            if (i == 0)
                builder.AddPosition("destination", "-d", destination.Path, destination.User, destination.Password, destination.OauthSettings);
            else
                builder.Add("-d", builder.PathWithCredentials(destination));
        }

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
        builder.AddPosition("source", "-s", settings.Source, settings.SourceUser, settings.SourcePassword, settings.SourceOauthSettings);
        builder.AddPosition("destination", "-d", settings.Destination, settings.DestinationUser, settings.DestinationPassword, settings.DestinationOauthSettings);
        builder.AddNumber("--retry", settings.RetryOnError, 3);
        builder.AddFlag("--skipHash", settings.SkipHashCheck);
        builder.AddFlag("-y", true);
        return builder.ToResult();
    }

    private class Builder
    {
        private readonly StringBuilder _command = new();
        private readonly List<string> _unsupportedPaths = new();
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

        private void AddSecret(string option, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            Add(option, PasswordPlaceholder);
            _hasPlaceholders = true;
        }

        /// <summary>
        /// A position with its own credential options (the source, the first destination)
        /// </summary>
        /// <param name="prefix">Prefix of the credential options: "source" or "destination"</param>
        public void AddPosition(string prefix, string pathOption, string? path, string? user, string? password, OAuth2Settings? oauth)
        {
            Add(pathOption, path);
            Add($"--{prefix}User", user);
            AddSecret($"--{prefix}Password", password);
            if (oauth is not { IsEmpty: false })
                return;
            Add($"--{prefix}OauthServer", oauth.ServerUrl?.ToString());
            Add($"--{prefix}OauthRedirect", oauth.RedirectUrl?.ToString());
            Add($"--{prefix}OauthApp", oauth.AppName);
            Add($"--{prefix}OauthAppId", oauth.AppId);
            AddSecret($"--{prefix}OauthSecret", oauth.AppSecret);
        }

        /// <summary>
        /// The path of a destination after the first one: the CLI takes its credentials only in the path
        /// (see <see cref="FileManagerFactory.GetFileManager"/>)
        /// </summary>
        public string? PathWithCredentials(BackupDestination destination)
        {
            var path = destination.Path;
            if (string.IsNullOrEmpty(path))
                return path;
            if (destination.OauthSettings is { IsEmpty: false }) {
                _unsupportedPaths.Add(path);
                return path;
            }
            if (string.IsNullOrEmpty(destination.User))
                return path;

            // scheme://user:password@host/position (FTP, SFTP, SMB) or scheme://user:password/position (MEGA, MediaFire)
            var hostSchemes = new[] { Ftp.Scheme, Sftp.Scheme, Smb.Scheme };
            var accountSchemes = new[] { Mega.Scheme, MediaFire.Scheme };
            var scheme = hostSchemes.Concat(accountSchemes)
                .FirstOrDefault(s => path.StartsWith(s, StringComparison.InvariantCultureIgnoreCase));
            if (scheme == null) {
                // The user is already in the path (OneDrive, Dropbox, Nextcloud): a password cannot be added there
                if (!string.IsNullOrEmpty(destination.Password))
                    _unsupportedPaths.Add(path);
                return path;
            }
            if (string.IsNullOrEmpty(destination.Password)) {
                _unsupportedPaths.Add(path);
                return path;
            }

            _hasPlaceholders = true;
            var rest = path[scheme.Length..].TrimStart('/');
            var credentials = $"{destination.User}:{PasswordPlaceholder}";
            return hostSchemes.Contains(scheme) ?
                $"{scheme}{credentials}@{rest}" :
                $"{scheme}{credentials}/{rest}";
        }

        public Result ToResult() => new(_command.ToString(), _hasPlaceholders, _unsupportedPaths);
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
