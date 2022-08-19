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
            SkipHashCheck = pArgs.SkipHashCheck ?? false,

            RetryOnError = pArgs.RetryOnError ?? 3,
            LogToConsole = false,
            LogFile = pArgs.LogFile
        };

        var job = new RestoreJob(settings);
        job.Step += OnStep;
        job.ConfirmRestore += OnConfirm;
        job.LogEmitted += OnLog;
        job.FileProgress += OnFileProgress;
        job.DeleteProgress += OnDeleteProgress;
        job.RestoreProgress += OnRestoreProgress;

        var cts = new CancellationTokenSource();
        BaseJobResult? jobRes = null;
        try {
            Console.CursorVisible = false;
            var task = Task.Run(() => job.Run(cts.Token));
            jobRes = await task;
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

        Started = jobRes?.Started;
        Completed = jobRes?.Completed;

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
            case RestoreJob.Steps.ReadDestination:
                Console.WriteLine("Reading destination files");
                break;
            case RestoreJob.Steps.CalculateFilesToRestore:
                Console.WriteLine("Calculating files to restore");
                break;
            case RestoreJob.Steps.DeleteFiles:
                Console.WriteLine("Deleting files");
                break;
            case RestoreJob.Steps.RestoreFiles:
                Console.WriteLine("Restoring files");
                break;
        }

        Console.ForegroundColor = DefaultColor;
    }

    private Task<bool> OnConfirm(object sender, RestoreJob.ConfirmArgs e)
    {
        if (_pArgs!.AlwaysYes == true)
            return Task.FromResult(true);
        Console.CursorVisible = true;
        var res = Utility.Console.Confirm($"Delete {e.FilesToDelete} ({MpbkpuLib.Utility.String.BytesToString(e.BytesToDelete)}), restore {e.FilesToRestore} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToRestore)})?", true, true);
        Console.CursorVisible = false;
        return Task.FromResult(res);
    }

    private void OnDeleteProgress(object sender, int file, int totalFiles, long bytes, long totalBytes)
    {
        Console.CursorLeft = 0;
        Console.Write($"[{(file / (double)totalFiles * 100.0),5:0.0}%]");
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

    private void OnRestoreProgress(object sender, int file, int totalFiles, long bytes, long totalBytes)
    {
        _totalPerc = file / (double)totalFiles * 100.0;
        Console.CursorLeft = 0;
        Console.Write($"[{_totalPerc,5:0.0}%]");
    }

    private void OnLog(object sender, BaseJobSettings.LogLevels level, string message)
    {
        Utility.Console.ClearLine();
        WriteLog(level, message);
    }
    #endregion
}