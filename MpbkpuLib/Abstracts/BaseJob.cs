using System.IO.Compression;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.Abstracts;

public abstract class BaseJob : IDisposable
{
    #region region classes
    public class ProgressHandlerArgs : EventArgs
    {
        public ProgressHandlerArgs(int file, int totalFiles, long bytes, long totalBytes, TimeSpan? eta)
        {
            File = file;
            TotalFiles = totalFiles;
            Bytes = bytes;
            TotalBytes = totalBytes;
            Eta = eta;
        }
        public int File { get; set; }
        public int TotalFiles { get; set; }
        public long Bytes { get; set; }
        public long TotalBytes { get; set; }
        /// <summary>
        /// Time left, null while it cannot be estimated
        /// </summary>
        public TimeSpan? Eta { get; set; }

        /// <summary>
        /// Percentage done: by bytes, or by files when there are no bytes (empty files, directories)
        /// </summary>
        public double Percentage =>
            TotalBytes > 0 ? Bytes * 100.0 / TotalBytes :
            TotalFiles > 0 ? File * 100.0 / TotalFiles : 100;
    }
    #endregion

    #region private members
    private readonly StreamWriter? _logWriter;
    protected IFileManager? BackupFm;
    protected string? BackupPath;
    /// <summary>
    /// Relative paths of the directories that could not be read by <see cref="ReadPathContent"/>
    /// </summary>
    protected readonly List<string> UnreadableDirectories = new();
    #endregion

    #region delegates

    public delegate void LogHandler(object sender, BaseJobSettings.LogLevels level, string message);

    /// <summary>
    /// Emitted when a log message is added to the log
    /// </summary>
    public event LogHandler? LogEmitted;

    public delegate void SpaceHandler(object sender, StorageSpace space);
    /// <summary>
    /// Emitted with the space of the destination: read at the start and at the end of the job, estimated in between
    /// </summary>
    public event SpaceHandler? SpaceChanged;

    public delegate void OpenBackupHandler(object sender, long bytes, long totalBytes);
    public event OpenBackupHandler? OpenBackup;
    public event OpenBackupHandler? CopyBackup;
    #endregion

    protected BaseJob(BaseJobSettings? settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        // Open the log file
        if (!string.IsNullOrEmpty(Settings.LogFile)) {
            _logWriter = new StreamWriter(Settings.LogFile, false, System.Text.Encoding.UTF8);
        }
    }

    #region properties
    public BaseJobSettings Settings { get; private set; }
    #endregion

    private readonly SpaceEstimate _space = new();

    /// <summary>
    /// Read the space of the destination (<see cref="BackupFm"/>). Never fails the job.
    /// </summary>
    protected async Task ReadDestinationSpace()
    {
        if (BackupFm == null || string.IsNullOrEmpty(BackupPath))
            return;
        await ReadSpace(_space, BackupFm, BackupPath);
        OnSpaceChanged(_space.Current);
    }

    /// <summary>
    /// Read the space of a destination into <paramref name="space"/>. Never fails the job.
    /// </summary>
    protected async Task ReadSpace(SpaceEstimate space, IFileManager fm, string path)
    {
        try {
            await space.Read(fm, path);
        } catch (Exception ex) {
            LogWarning($"Cannot read the space of the destination: {ex.Message}");
        }
    }

    /// <summary>
    /// Raise <see cref="SpaceChanged"/> when the space is known
    /// </summary>
    protected void OnSpaceChanged(StorageSpace? space)
    {
        if (space != null)
            SpaceChanged?.Invoke(this, space);
    }

    /// <summary>
    /// Update the estimated space of the destination after writing (or deleting, with negative bytes)
    /// </summary>
    protected void DestinationSpaceUsed(long bytes)
    {
        _space.Used(bytes);
        OnSpaceChanged(_space.Current);
    }

    public virtual void Dispose()
    {
        BackupFm?.Dispose();
    }

    /// <summary>
    /// Run the job
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/></param>
    /// <returns>True on success</returns>
    public abstract Task<BaseJobResult> Run(CancellationToken cancellationToken);

    /// <summary>
    /// Validate the job settings
    /// </summary>
    protected abstract void ValidateSettings();

    /// <summary>
    /// Get the full path of a backup item
    /// </summary>
    /// <param name="fm">The <see cref="IFileManager"/></param>
    /// <param name="backupPath">The backup path</param>
    /// <param name="item">The <see cref="BackupItem"/></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    protected string GetBackupItemPath(IFileManager? fm, string? backupPath, BackupItem? item)
    {
        if (fm == null)
            throw new ArgumentNullException(nameof(fm));
        if (string.IsNullOrEmpty(backupPath))
            throw new ArgumentNullException(nameof(backupPath));
        if (item == null)
            throw new ArgumentNullException(nameof(item));
        if (item.Backup == null)
            throw new ArgumentException("The item must contain Backup information", nameof(item));
        if (item.Volume == null)
            throw new ArgumentException("The item must contain Volume information", nameof(item));

        return fm.CombinePath(backupPath, item.Backup.Guid, item.Volume.Name!, item.Name!);
    }

    protected void OnOpenBackup(long bytes, long totalBytes)
    {
        OpenBackup?.Invoke(this, bytes, totalBytes);
    }

    protected void OnCopyBackup(long bytes, long totalBytes)
    {
        CopyBackup?.Invoke(this, bytes, totalBytes);
    }

    /// <summary>
    /// Read content of a directory
    /// </summary>
    /// <param name="bkpFile">The <see cref="BackupFile"/> the information are saved</param>
    /// <param name="fileManager">The <see cref="IFileManager"/> to use to access <see cref="rootPath"/></param>
    /// <param name="rootPath">The path to read content</param>
    /// <param name="includeHiddenFiles">If set to true also hidden files are included</param>
    /// <param name="defaultAction">The <see cref="DataModel.File"/> default action</param>
    /// <param name="token">The <see cref="CancellationToken"/></param>
    /// <param name="exclude">Files and directories to skip (see <see cref="IsExcluded"/>)</param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    protected Task<bool> ReadPathContent(BackupFile? bkpFile, IFileManager? fileManager, string? rootPath,
        bool includeHiddenFiles,
        File.FileActions defaultAction,
        CancellationToken? token,
        IReadOnlyCollection<string>? exclude = null)
    {
        // Local file systems return absolute paths: the root must be absolute too to compute relative paths
        if (fileManager is BaseLocalFileSystem && !string.IsNullOrEmpty(rootPath))
            rootPath = Path.GetFullPath(rootPath);
        return ReadPathContent(bkpFile, fileManager, rootPath, rootPath, includeHiddenFiles, defaultAction, token, exclude);
    }

    /// <summary>
    /// Check if a relative path matches one of the exclude patterns.
    /// A pattern (wildcards * and ?, case-insensitive, '/' or '' as separator) is matched against the relative path
    /// and against the file name: "bin", "*.tmp" and "docs/old" are all valid.
    /// </summary>
    protected static bool IsExcluded(string? relativePath, char pathSeparator, IReadOnlyCollection<string>? exclude)
    {
        if (string.IsNullOrEmpty(relativePath) || exclude == null || exclude.Count == 0)
            return false;

        var path = ToPortablePath(relativePath, pathSeparator);
        var name = path[(path.LastIndexOf('/') + 1)..];
        return exclude
            .Select(e => e.Replace('\\', '/').Trim('/'))
            .Where(e => e.Length > 0)
            .Any(e => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(e, path) ||
                      System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(e, name));
    }

    /// <summary>
    /// Convert a relative path to use '/' as separator, to compare paths of different file managers
    /// </summary>
    protected static string ToPortablePath(string relativePath, char pathSeparator)
    {
        return relativePath.Replace(pathSeparator, '/').Trim('/');
    }

    private static readonly System.Text.RegularExpressions.Regex UrlCredentials = new(
        @"(\b\w+://[^:/@\s""]+:)[^@\s""]+@|(\b(?:mega|mediafire)://[^:/\s""]+:)[^/\s""]+",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Hide the passwords written in paths like ftp://user:password@host or mega://user:password/path
    /// </summary>
    public static string MaskCredentials(string text)
    {
        return UrlCredentials.Replace(text, m => m.Groups[1].Success ? $"{m.Groups[1].Value}***@" : $"{m.Groups[2].Value}***");
    }

    /// <summary>
    /// The job settings as JSON, safe to log or save: passwords are never included
    /// </summary>
    protected string SerializeSettings()
    {
        return MaskCredentials(Newtonsoft.Json.JsonConvert.SerializeObject(Settings, Newtonsoft.Json.Formatting.Indented));
    }

    protected void LogDebug(string message)
    {
        Log(BaseJobSettings.LogLevels.Debug, message);
    }

    protected  void LogInfo(string message)
    {
        Log(BaseJobSettings.LogLevels.Info, message);
    }

    protected  void LogWarning(string message)
    {
        Log(BaseJobSettings.LogLevels.Warning, message);
    }

    protected  void LogError(string message)
    {
        Log(BaseJobSettings.LogLevels.Error, message);
    }

    protected void LogError(Exception exception)
    {
        var ex = exception;
        while (ex != null) {
            LogError(ex.Message);
            if (!string.IsNullOrEmpty(ex.StackTrace))
                LogError(ex.StackTrace);
            ex = ex.InnerException;
        }
    }

    /// <summary>
    /// Log a message
    /// </summary>
    /// <param name="level">The <see cref="BaseJobSettings.LogLevels"/></param>
    /// <param name="message">The message to log</param>
    private void Log(BaseJobSettings.LogLevels level, string message)
    {
        if (level < Settings.LogLevel)
            return;

        var levelStr = string.Empty;
        var color = ConsoleColor.Gray;

        switch (level) {
            case BaseJobSettings.LogLevels.Debug:
                levelStr = "DBG";
                color = ConsoleColor.DarkGray;
                break;
            case BaseJobSettings.LogLevels.Info:
                levelStr = "INF";
                color = ConsoleColor.White;
                break;
            case BaseJobSettings.LogLevels.Warning:
                levelStr = "WRN";
                color = ConsoleColor.Yellow;
                break;
            case BaseJobSettings.LogLevels.Error:
                levelStr = "ERR";
                color = ConsoleColor.Red;
                break;
        }
        var logLine = $"[{DateTime.Now.ToString("yyyyMMdd HH:mm:ss.fff")}][{levelStr}] {message}";

        if (_logWriter != null) {
            _logWriter.WriteLine(logLine);
            _logWriter.Flush();
        }

        if (Settings.LogToConsole) {
            Console.ForegroundColor = color;
            Console.WriteLine(logLine);
            Console.ResetColor();
        }

        LogEmitted?.Invoke(this, level, message);
    }

    private async Task<bool> ReadPathContent(BackupFile? bkpFile, IFileManager? fileManager, string? rootPath,
        string? path, bool includeHiddenFiles,
        File.FileActions defaultAction,
        CancellationToken? token,
        IReadOnlyCollection<string>? exclude)
    {
        if (bkpFile == null)
            throw new ArgumentNullException(nameof(bkpFile));
        if (fileManager == null)
            throw new ArgumentNullException(nameof(fileManager));
        if (string.IsNullOrEmpty(rootPath))
            throw new ArgumentNullException(nameof(rootPath));
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));

        if (token?.IsCancellationRequested == true)
            return true;

        if (!rootPath.EndsWith(fileManager.PathSeparator))
            rootPath = $"{rootPath}{fileManager.PathSeparator}";
        LogDebug($"  {path}");
        await foreach (var file in fileManager.GetDirectoryContent(path, includeHiddenFiles)) {
            if (token?.IsCancellationRequested == true)
                return true;

            if (file.Type == File.FileTyes.Directory)
                file.Size = 0;
            file.FileAction = defaultAction;
            file.RootPath = rootPath;
            file.RelativePath = file.FullPath?.Substring(file.RootPath.Length);
            if (IsExcluded(file.RelativePath, fileManager.PathSeparator, exclude)) {
                LogDebug($"  Excluded {file.FullPath}");
                continue;
            }
            bkpFile.Files.Add(file);
            if (file.Type == File.FileTyes.Directory) {
                try {
                    await ReadPathContent(bkpFile, fileManager, rootPath, file.FullPath, includeHiddenFiles, defaultAction, token, exclude);
                } catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException) {
                    // A single unreadable directory must not abort the whole job
                    LogWarning($"Cannot read directory {file.FullPath}: {ex.Message}");
                    UnreadableDirectories.Add(file.RelativePath!);
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Check if a relative path is inside a directory that could not be read
    /// </summary>
    protected bool IsInUnreadableDirectory(string? relativePath, char pathSeparator)
    {
        return relativePath != null &&
               UnreadableDirectories.Any(d => relativePath.StartsWith($"{d}{pathSeparator}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Delete a directory and its content (not all the file managers delete non empty directories)
    /// </summary>
    protected static async Task DeleteDirectoryRecursive(IFileManager fm, string path)
    {
        var children = new List<File>();
        await foreach (var child in fm.GetDirectoryContent(path)) {
            children.Add(child);
        }
        foreach (var child in children) {
            if (child.Type == File.FileTyes.Directory)
                await DeleteDirectoryRecursive(fm, child.FullPath!);
            else
                await fm.DeleteFile(child.FullPath);
        }
        await fm.DeleteDirectory(path);
    }

    /// <summary>
    /// Delete a file if it exists, logging a warning on failure
    /// </summary>
    protected async Task TryDeleteFile(IFileManager fm, string path)
    {
        try {
            if (await fm.FileExists(path))
                await fm.DeleteFile(path);
        } catch (Exception ex) {
            LogWarning($"Cannot delete {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Start the time-left estimate of a job phase. Deletions pass 0 bytes: their cost does not depend on the size.
    /// </summary>
    /// <param name="totalBytes">Total bytes of the phase</param>
    /// <param name="totalItems">Total items of the phase</param>
    /// <param name="fileManagers">The file managers used by the phase: remote ones have a higher cost per item</param>
    protected static EtaEstimator StartEta(long totalBytes, long totalItems, params IFileManager?[] fileManagers)
    {
        var remote = fileManagers.Any(fm => fm is not null and not BaseLocalFileSystem);
        return new EtaEstimator(totalBytes, totalItems, remote ? EtaEstimator.RemoteItemCost : EtaEstimator.LocalItemCost);
    }
}