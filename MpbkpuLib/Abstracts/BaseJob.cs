using MpbkpuLib.FileManagers;
using MpbkpuLib.Utility;

namespace MpbkpuLib.Abstracts;

public abstract class BaseJob
{
    #region private members
    private readonly StreamWriter? _logWriter;
    #endregion
    #region delegates
    public delegate void LogHandler(object sender, BaseSettings.LogLevels level, string message);

    /// <summary>
    /// Emitted when a log message is added to the log
    /// </summary>
    public event LogHandler? LogEmitted;
    #endregion

    protected BaseJob(BaseSettings? settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        Settings = settings;
        // Open the log file
        if (!string.IsNullOrEmpty(Settings.LogFile)) {
            _logWriter = new StreamWriter(Settings.LogFile, false, System.Text.Encoding.UTF8);
        }
    }

    #region properties
    public BaseSettings Settings { get; private set; }

    public DateTime? Started { get; set; }
    public DateTime? Completed { get; set; }
    #endregion

    /// <summary>
    /// Run the job
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/></param>
    /// <returns>True on success</returns>
    public abstract Task<bool> Run(CancellationToken cancellationToken);

    /// <summary>
    /// Validate the job settings
    /// </summary>
    protected abstract void ValidateSettings();

    protected async Task<bool> CopyDatabaseToDestination(BackupFile.OpenResult openRes, string? destination, string? password)
    {
        if (string.IsNullOrEmpty(destination))
            throw new ArgumentNullException(nameof(destination));

        using var sourceFm = FileManagerFactory.GetFileManager(openRes.TempFilePath, out _);
        using var destFm = FileManagerFactory.GetFileManager(destination, out _);
        await using var sourceStream = await sourceFm.OpenFile(openRes.TempFilePath);
        await using var destStream = await destFm.OpenFile(openRes.BackupFilePath, true);

        if (string.IsNullOrEmpty(password))
            await Files.Copy(sourceStream, destStream);
        else {
            var encrypt = new EncryptDecrypt(password);
            await encrypt.Encrypt(sourceStream, destStream, destFm.SupportCompression());
        }

        return true;
    }

    protected void LogDebug(string message)
    {
        Log(BaseSettings.LogLevels.Debug, message);
    }

    protected  void LogInfo(string message)
    {
        Log(BaseSettings.LogLevels.Info, message);
    }

    protected  void LogWarning(string message)
    {
        Log(BaseSettings.LogLevels.Warning, message);
    }

    protected  void LogError(string message)
    {
        Log(BaseSettings.LogLevels.Error, message);
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
    /// <param name="level">The <see cref="BaseSettings.LogLevels"/></param>
    /// <param name="message">The message to log</param>
    protected  void Log(BaseSettings.LogLevels level, string message)
    {
        if (level < Settings.LogLevel)
            return;

        var levelStr = string.Empty;
        var color = ConsoleColor.Gray;

        switch (level) {
            case BaseSettings.LogLevels.Debug:
                levelStr = "DBG";
                color = ConsoleColor.DarkGray;
                break;
            case BaseSettings.LogLevels.Info:
                levelStr = "INF";
                color = ConsoleColor.White;
                break;
            case BaseSettings.LogLevels.Warning:
                levelStr = "WRN";
                color = ConsoleColor.Yellow;
                break;
            case BaseSettings.LogLevels.Error:
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
}