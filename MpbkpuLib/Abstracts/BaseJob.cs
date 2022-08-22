using System.IO.Compression;
using Dropbox.Api.Users;
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
        public ProgressHandlerArgs(int file, int totalFiles, long bytes, long totalBytes, TimeSpan eta)
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
        public TimeSpan Eta { get; set; }
    }
    #endregion
    #region private members

    private readonly StreamWriter? _logWriter;
    protected IFileManager? BackupFm;
    protected string? BackupPath;
    #endregion

    #region delegates

    public delegate void LogHandler(object sender, BaseJobSettings.LogLevels level, string message);

    /// <summary>
    /// Emitted when a log message is added to the log
    /// </summary>
    public event LogHandler? LogEmitted;

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

    protected async Task CopyDatabaseToDestination(BackupFile.OpenResult openRes, string? password)
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        using var sourceFm = FileManagerFactory.GetFileManager(openRes.TempFilePath, out _);
        await using var sourceStream = await sourceFm.OpenFile(openRes.TempFilePath);

        if (await BackupFm.FileExists(openRes.BackupFilePath))
            await BackupFm.DeleteFile(openRes.BackupFilePath);

        var destFileSize = Files.GetDestinationFileSize(sourceStream.Length, BackupFm.SupportCompression() ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression,
            !string.IsNullOrEmpty(password));
        await using var destStream = await BackupFm.OpenFile(openRes.BackupFilePath, true, destFileSize);

        if (string.IsNullOrEmpty(password))
            await Files.Copy(sourceStream, destStream);
        else {
            var encrypt = new EncryptDecrypt(password);
            await encrypt.Encrypt(sourceStream, destStream, BackupFm.SupportCompression() ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression);
        }
    }

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

    /// <summary>
    /// Read content of a directory
    /// </summary>
    /// <param name="bkpFile">The <see cref="BackupFile"/> the information are saved</param>
    /// <param name="fileManager">The <see cref="IFileManager"/> to use to access <see cref="rootPath"/></param>
    /// <param name="rootPath">The path to read content</param>
    /// <param name="includeHiddenFiles">If set to true also hidden files are included</param>
    /// <param name="defaultAction">The <see cref="DataModel.File"/> default action</param>
    /// <param name="token">The <see cref="CancellationToken"/></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    protected Task<bool> ReadPathContent(BackupFile? bkpFile, IFileManager? fileManager, string? rootPath,
        bool includeHiddenFiles,
        File.FileActions defaultAction,
        CancellationToken? token)
    {
        return ReadPathContent(bkpFile, fileManager, rootPath, rootPath, includeHiddenFiles, defaultAction, token);
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
        CancellationToken? token)
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

        if (!rootPath.EndsWith(fileManager.GetPathSeparator()))
            rootPath = $"{rootPath}{fileManager.GetPathSeparator()}";
        LogDebug($"  {path}");
        await foreach (var file in fileManager.GetDirectoryContent(path, includeHiddenFiles)) {
            if (token?.IsCancellationRequested == true)
                return true;

            if (file.Type == File.FileTyes.Directory)
                file.Size = 0;
            file.FileAction = defaultAction;
            file.RootPath = rootPath;
            file.RelativePath = file.FullPath?.Substring(file.RootPath.Length);
            bkpFile.Files.Add(file);
            if (file.Type == File.FileTyes.Directory)
                await ReadPathContent(bkpFile, fileManager, rootPath, file.FullPath, includeHiddenFiles, defaultAction, token);
        }
        return true;
    }

    protected TimeSpan GetEta(DateTime? started, long done, long total)
    {
        if (started == null)
            throw new ArgumentNullException(nameof(started));

        if (done == 0)
            return TimeSpan.Zero;

        var secs = (DateTime.UtcNow - started.Value).TotalSeconds;
        return TimeSpan.FromSeconds(secs / done * total - secs);
    }
}