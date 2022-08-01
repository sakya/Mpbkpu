using MpbkpuLib;
using MpbkpuLib.Abstracts;

namespace Mpbkpu.Actions;

public class BackupAction : BaseAction
{
    private Arguments? _pArgs = null;
    private double _totalPerc = 0;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var settings = new BackupJobSettings()
        {
            BackupType = pArgs.BackupType!.Value,
            LogLevel = pArgs.LogLevel,
            Source = pArgs.SourcePath,
            Destination = pArgs.DestinationPath,
            EncryptPassword = pArgs.Password,
            Compress = pArgs.Compress,
            LogToConsole = false,
        };

        Console.WriteLine($"Type      : { pArgs.BackupType }");
        Console.WriteLine($"Compress  : { (pArgs.Compress ? "Enabled" : "Disabled") }");
        Console.WriteLine($"Encryption: { (!string.IsNullOrEmpty(pArgs.Password) ? "Enabled" : "Disabled") }");
        Console.WriteLine();

        var backupJob = new BackupJob(settings);
        backupJob.ConfirmBackup += OnConfirm;
        backupJob.LogEmitted += OnLog;
        backupJob.FileProgress += OnFileProgress;
        backupJob.Progress += OnProgress;

        var cts = new CancellationTokenSource();
        try {
            Console.CursorVisible = false;
            var backupTask = Task.Run(() => backupJob.Run(cts.Token));
            await backupTask;
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

        return true;
    }

    private Task<bool> OnConfirm(object sender, BackupJob.ConfirmArgs e)
    {
        if (_pArgs!.AlwaysYes == true)
            return Task.FromResult(true);
        Console.CursorVisible = true;
        var res = Utility.Console.Confirm($"Backup {e.FilesToBackup} files ({Utility.String.BytesToString(e.BytesToBackup)})?", true);
        Console.CursorVisible = false;
        return Task.FromResult(res);
    }

    private void OnFileProgress(object sender, string filePath, long bytes, long totalBytes)
    {
        var perc = (double)bytes / (double)totalBytes * 100.0;
        var progress = $"[{_totalPerc.ToString("0.0").PadLeft(5)}%][{perc.ToString("0.0").PadLeft(5)}%]";
        if (filePath.Length > Console.WindowWidth - progress.Length - 1)
            filePath = $"...{ filePath.Substring(filePath.Length - (Console.WindowWidth - progress.Length) + 4) }";
        progress = $"{progress}{filePath}";
        Console.CursorLeft = 0;
        Console.Write(progress.PadRight(Console.WindowWidth - 1));
    }

    private void OnProgress(object sender, int file, int totalFiles, long bytes, long totalBytes)
    {
        _totalPerc = (double)file / (double)totalFiles * 100.0;
    }

    private void OnLog(object sender, BaseSettings.LogLevels level, string message)
    {

    }
}