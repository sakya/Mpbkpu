using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;

namespace Mpbkpu.Actions;

public abstract class BaseAction : IAction
{
    protected CancellationTokenSource? CancellationTokenSource;
    protected bool CanCancel;
    private bool _cancelled;
    private double _totalPerc;
    private TimeSpan? _eta;

    public DateTime? Started { get; set; }
    public DateTime? Completed { get; set; }
    public long? Bytes { get; set; }

    public abstract Task<bool> Run(Arguments pArgs);

    public bool UserCancelled()
    {
        return _cancelled || CancellationTokenSource?.IsCancellationRequested == true;
    }

    public virtual bool Cancel()
    {
        if (CancellationTokenSource == null)
            throw new Exception("CancellationTokenSource not initialized");
        CancellationTokenSource.Cancel();
        return CanCancel;
    }

    /// <summary>
    /// The settings every action takes from the arguments: log level and log file
    /// </summary>
    protected static T WithCommonSettings<T>(T settings, Arguments pArgs) where T : BaseJobSettings
    {
        settings.LogLevel = pArgs.LogLevel;
        settings.LogFile = pArgs.LogFile;
        settings.LogToConsole = false;
        return settings;
    }

    /// <summary>
    /// Run a job, writing its log and its errors
    /// </summary>
    /// <returns>The result, null when the job failed with an exception</returns>
    protected async Task<BaseJobResult?> RunJob(BaseJob job)
    {
        job.LogEmitted += OnLog;
        job.OpenBackup += OnCopyBackup;

        BaseJobResult? res = null;
        using (CancellationTokenSource = new CancellationTokenSource()) {
            try {
                Utility.Console.SetCursorVisible(false);
                res = await Task.Run(() => job.Run(CancellationTokenSource.Token));
                Utility.Console.ClearLine();
            } catch (Exception ex) when (!(ex is OperationCanceledException && UserCancelled())) {
                WriteError(ex.Message);
            } catch (OperationCanceledException) {
                // Reported by the program as a cancellation
            } finally {
                Utility.Console.SetCursorVisible(true);
            }
        }

        Started = res?.Started;
        Completed = res?.Completed;
        Bytes = res?.Bytes;
        if (res?.Result == Backup.Results.Cancelled)
            _cancelled = true;
        if (res is { Success: false, ErrorMessage: { } message } && !UserCancelled())
            WriteError(message);
        return res;
    }

    /// <summary>
    /// True when a job result is a success (completed, also with warnings)
    /// </summary>
    protected static bool Succeeded(BaseJobResult? res)
    {
        return res is { Success: true, Result: not (Backup.Results.Error or Backup.Results.Cancelled) };
    }

    private static void WriteError(string message)
    {
        Utility.Console.ClearLine();
        Console.ForegroundColor = Utility.Console.ErrorColor;
        Console.WriteLine($"ERROR: {message}");
        Console.ForegroundColor = Utility.Console.DefaultColor;
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

    /// <summary>
    /// Write the result of a backup in its color (lists of backups)
    /// </summary>
    protected static void WriteResult(Backup.Results? result)
    {
        Console.ForegroundColor = result switch
        {
            Backup.Results.Completed => Utility.Console.SuccessColor,
            Backup.Results.CompletedWithWarnings or Backup.Results.Cancelled => Utility.Console.WarningColor,
            Backup.Results.Error => Utility.Console.ErrorColor,
            _ => Utility.Console.DefaultColor
        };
        Console.Write($" {result,-20}");
        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    protected void WriteEnabled(bool enabled)
    {
        if (enabled)
            Console.ForegroundColor = Utility.Console.SuccessColor;
        Console.WriteLine(enabled ? "Enabled" : "Disabled");
        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    /// <summary>
    /// Write a step of a job on its own line
    /// </summary>
    protected static void WriteStep(string text)
    {
        Utility.Console.ClearLine();
        Console.ForegroundColor = Utility.Console.InfoColor;
        Console.WriteLine(text);
        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    /// <summary>
    /// A warning to add to a confirmation when the destinations have not enough free space
    /// </summary>
    protected static string SpaceWarning(BaseJob.SpaceConfirmArgs args)
    {
        return args.NotEnoughSpace ?
            $" Warning: the destinations have only {MpbkpuLib.Utility.String.BytesToString(args.AvailableBytes!.Value)} free." :
            string.Empty;
    }

    /// <summary>
    /// Ask a confirmation, with the cursor visible
    /// </summary>
    protected static bool Confirm(string message, bool defaultValue)
    {
        Utility.Console.SetCursorVisible(true);
        var res = Utility.Console.Confirm(message, defaultValue, true);
        Utility.Console.SetCursorVisible(false);
        return res;
    }

    protected static void OnCopyBackup(object sender, long bytes, long totalBytes)
    {
        Utility.Console.WriteProgress($"[{GetPercentage(bytes, totalBytes),5:0.0}%]Copying database");
    }

    /// <summary>
    /// Progress of the whole job: kept for the file progress lines
    /// </summary>
    protected void OnProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        _totalPerc = GetPercentage(args.Bytes, args.TotalBytes);
        _eta = args.Eta;
        Utility.Console.WriteProgress(TotalProgressText());
    }

    protected void OnFileProgress(object sender, string filePath, long bytes, long totalBytes)
    {
        var progress = $"{TotalProgressText()}[{GetPercentage(bytes, totalBytes),5:0.0}%]";
        Utility.Console.WriteProgress($"{progress}{Utility.Console.FitPath(filePath, progress.Length)}");
    }

    protected static void OnDeleteProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        Utility.Console.WriteProgress($"[{GetPercentage(args.File, args.TotalFiles),5:0.0}%]");
    }

    private string TotalProgressText() => $"[{_totalPerc,5:0.0}% {MpbkpuLib.Utility.String.EtaToString(_eta)}]";

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
