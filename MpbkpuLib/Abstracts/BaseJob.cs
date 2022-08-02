namespace MpbkpuLib.Abstracts;

public abstract class BaseJob
{
    #region private members
    private StreamWriter? _logWriter;
    #endregion
    #region delegates
    public delegate void LogHandler(object sender, BaseSettings.LogLevels level, string message);
    public event LogHandler? LogEmitted;
    #endregion

    public BaseJob(BaseSettings? settings)
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
    #endregion

    public abstract Task<bool> Run(CancellationToken cancellationToken);

    protected abstract void ValidateSettings();

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
        Log(BaseSettings.LogLevels.Warning, message);
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