using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class RestoreAction : BaseAction
{
    private Arguments? _pArgs;
    private double _totalPerc;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var settings = new RestoreJobSettings()
        {
            LogLevel = pArgs.LogLevel,
            BackupPath = pArgs.BackupPath,
            BackupPathUser = pArgs.BackupUser,
            BackupPathPassword = pArgs.BackupPassword,
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password,

            Destination = pArgs.DestinationPath,
            DestinationUser = pArgs.DestinationUser,
            DestinationPassword = pArgs.DestinationPassword,

            LogToConsole = false,
            LogFile = pArgs.LogFile
        };

        var job = new RestoreJob(settings);
        job.Step += OnStep;
        job.ConfirmRestore += OnConfirm;
        job.LogEmitted += OnLog;
        job.FileProgress += OnFileProgress;
        job.Progress += OnProgress;

        var cts = new CancellationTokenSource();
        try {
            Console.CursorVisible = false;
            var task = Task.Run(() => job.Run(cts.Token));
            await task;
            Console.WriteLine();
        } catch (Exception ex) {
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine();
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.ForegroundColor = DefaultColor;
        } finally {
            Console.CursorVisible = true;
            cts.Dispose();
        }

        Started = job.Started;
        Completed = job.Completed;

        return true;
    }

    #region private operations
    private void OnStep(object sender, RestoreJob.Steps step)
    {
        Utility.Console.ClearLine();
        Console.ForegroundColor = InfoColor;
        switch (step) {
            case RestoreJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
        }

        Console.ForegroundColor = DefaultColor;
    }

    private Task<bool> OnConfirm(object sender, RestoreJob.ConfirmArgs e)
    {
        if (_pArgs!.AlwaysYes == true)
            return Task.FromResult(true);
        Console.CursorVisible = true;
        var res = Utility.Console.Confirm($"Restore {e.FilesToRestore} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToRestore)})?", true, true);
        Console.CursorVisible = false;
        return Task.FromResult(res);
    }

    private void OnFileProgress(object sender, string filePath, long bytes, long totalBytes)
    {
        var perc = bytes / (double)totalBytes * 100.0;
        if (bytes == 0 && totalBytes == 0)
            perc = 100;
        var progress = $"[{_totalPerc,5:0.0}%][{perc,5:0.0}%]";
        if (filePath.Length > Console.WindowWidth - progress.Length - 1)
            filePath = $"...{ filePath.Substring(filePath.Length - (Console.WindowWidth - progress.Length) + 4) }";
        progress = $"{progress}{filePath}";

        Console.CursorLeft = 0;
        Console.Write(progress.PadRight(Console.WindowWidth - 1));
    }

    private void OnProgress(object sender, int file, int totalFiles, long bytes, long totalBytes)
    {
        _totalPerc = file / (double)totalFiles * 100.0;
        Console.CursorLeft = 0;
        Console.Write($"[{_totalPerc,5:0.0}%]");
    }

    private void OnLog(object sender, BaseSettings.LogLevels level, string message)
    {
        Utility.Console.ClearLine();
        WriteLog(level, message);
    }
    #endregion
}