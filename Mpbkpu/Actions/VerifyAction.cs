using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class VerifyAction : BaseAction
{
    private double _totalPerc;
    private TimeSpan? _eta;

    public override async Task<bool> Run(Arguments pArgs)
    {
        var settings = new VerifyJobSettings()
        {
            LogLevel = pArgs.LogLevel,
            BackupDestinations = pArgs.GetBackupDestinations(),
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password
        };

        var job = new VerifyJob(settings);
        job.Step += OnStep;
        job.OpenBackup += OnCopyBackup;
        job.LogEmitted += OnLog;
        job.Progress += OnProgress;
        job.FileProgress += OnFileProgress;

        BaseJobResult? jobRes = null;
        using (CancellationTokenSource = new CancellationTokenSource()) {
            try {
                Console.CursorVisible = false;
                var task = Task.Run(() => job.Run(CancellationTokenSource.Token));
                jobRes = await task;
                Console.WriteLine();
            } catch (Exception ex) {
                Console.ForegroundColor = Utility.Console.ErrorColor;
                Console.WriteLine();
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.ForegroundColor = Utility.Console.DefaultColor;
            } finally {
                Console.CursorVisible = true;
            }
        }

        Started = jobRes?.Started;
        Completed = jobRes?.Completed;

        return true;
    }

    #region private operations
    private void OnStep(object sender, VerifyJob.Steps step)
    {
        Utility.Console.ClearLine();
        Console.ForegroundColor = Utility.Console.InfoColor;
        switch (step) {
            case VerifyJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case VerifyJob.Steps.VerifyFiles:
                CanCancel = true;
                Console.WriteLine("Verifying files");
                break;
        }

        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    private void OnFileProgress(object sender, string filePath, long bytes, long totalBytes)
    {
        var perc = GetPercentage(bytes, totalBytes);
        if (bytes == 0 && totalBytes == 0)
            perc = 100;
        var progress = $"[{_totalPerc,5:0.0}% {MpbkpuLib.Utility.String.EtaToString(_eta)}][{perc,5:0.0}%]";
        if (filePath.Length > Console.WindowWidth - progress.Length - 1)
            filePath = $"...{ filePath.Substring(filePath.Length - (Console.WindowWidth - progress.Length) + 4) }";
        progress = $"{progress}{filePath}";

        Console.CursorLeft = 0;
        Console.Write(progress.PadRight(Console.WindowWidth - 1));
    }

    private void OnProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        _totalPerc = GetPercentage(args.Bytes, args.TotalBytes);
        _eta = args.Eta;
        Console.CursorLeft = 0;
        Console.Write($"[{_totalPerc,5:0.0}% {MpbkpuLib.Utility.String.EtaToString(_eta)}]");
    }
    #endregion
}