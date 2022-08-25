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
                Console.ForegroundColor = ConsoleColor.DarkGray;
                message = $"[DBG]{message}";
                break;
            case BaseJobSettings.LogLevels.Info:
                Console.ForegroundColor = ConsoleColor.White;
                message = $"[INF]{message}";
                break;
            case BaseJobSettings.LogLevels.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                message = $"[WRN]{message}";
                break;
            case BaseJobSettings.LogLevels.Error:
                Console.ForegroundColor = ConsoleColor.Red;
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


    protected void OnOpenBackup(object sender, long bytes, long totalBytes)
    {
        var perc = totalBytes >= 0 ? bytes / (double)totalBytes * 100.0 : 0;
        Console.CursorLeft = 0;
        Console.Write($"[{perc,5:0.0}%]Copying database");
    }

    protected void OnLog(object sender, BaseJobSettings.LogLevels level, string message)
    {
        Utility.Console.ClearLine();
        WriteLog(level, message);
    }
}