using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class VerifyAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        var settings = new VerifyJobSettings()
        {
            LogLevel = pArgs.LogLevel,
            BackupPath = pArgs.BackupPath,
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password
        };

        var job = new VerifyJob(settings);
        job.Step += OnStep;
        job.LogEmitted += OnLog;
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
    private void OnStep(object sender, VerifyJob.Steps step)
    {
        Console.ForegroundColor = InfoColor;
        switch (step) {
            case VerifyJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case VerifyJob.Steps.VerifyFiles:
                Console.WriteLine("Verifying files");
                break;
        }

        Console.ForegroundColor = DefaultColor;
    }

    private void OnLog(object sender, BaseSettings.LogLevels level, string message)
    {
        Utility.Console.ClearLine();
        WriteLog(level, message);
    }

    private void OnProgress(object sender, int file, int totalFiles)
    {
        var perc = file / (double)totalFiles * 100.0;
        var progress = $"[{perc.ToString("0.0").PadLeft(5)}%]";

        Console.CursorLeft = 0;
        Console.Write(progress.PadRight(Console.WindowWidth - 1));
    }
    #endregion
}