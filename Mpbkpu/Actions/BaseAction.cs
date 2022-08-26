using MpbkpuLib.Abstracts;

namespace Mpbkpu.Actions;

public abstract class BaseAction : IAction
{
    protected CancellationTokenSource? CancellationTokenSource;
    protected bool CanCancel;

    public DateTime? Started { get; set; }
    public DateTime? Completed { get; set; }
    public long? Bytes { get; set; }

    public abstract Task<bool> Run(Arguments pArgs);

    public bool UserCancelled()
    {
        return CancellationTokenSource?.IsCancellationRequested == true;
    }

    public virtual bool Cancel()
    {
        if (CancellationTokenSource == null)
            throw new Exception("CancellationTokenSource not initialized");
        CancellationTokenSource.Cancel();
        return CanCancel;
    }

    private void WriteLog(BaseJobSettings.LogLevels level, string message)
    {
        switch (level) {
            case BaseJobSettings.LogLevels.Debug:
                Console.ForegroundColor = Utility.Console.DebugColor;
                message = $"[DBG]{message}";
                break;
            case BaseJobSettings.LogLevels.Info:
                Console.ForegroundColor = Utility.Console.InfoColor;
                message = $"[INF]{message}";
                break;
            case BaseJobSettings.LogLevels.Warning:
                Console.ForegroundColor = Utility.Console.WarningColor;
                message = $"[WRN]{message}";
                break;
            case BaseJobSettings.LogLevels.Error:
                Console.ForegroundColor = Utility.Console.ErrorColor;
                message = $"[ERR]{message}";
                break;
        }
        Console.WriteLine(message);
        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    protected void WriteEnabled(bool enabled)
    {
        if (enabled)
            Console.ForegroundColor = Utility.Console.SuccessColor;
        Console.WriteLine(enabled ? "Enabled" : "Disabled");
        Console.ForegroundColor = Utility.Console.DefaultColor;
    }


    protected static void OnCopyBackup(object sender, long bytes, long totalBytes)
    {
        var perc = GetPercentage(bytes, totalBytes);
        Console.CursorLeft = 0;
        Console.Write($"[{perc,5:0.0}%]Copying database");
    }

    protected void OnLog(object sender, BaseJobSettings.LogLevels level, string message)
    {
        Utility.Console.ClearLine();
        WriteLog(level, message);
    }

    /// <summary>
    /// Get the completed percentage with one decimal
    /// </summary>
    /// <param name="value">The value</param>
    /// <param name="total">The total value</param>
    /// <returns>The percentage</returns>
    protected static float GetPercentage(long value, long total)
    {
        if (value == 0 && total == 0)
            return 100;
        if (total == 0)
            return 0;
        var res = value / (float)total * 100f;
        return (float)Math.Floor(res * 10) / 10f;
    }
}