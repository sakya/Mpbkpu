using MpbkpuLib;
using MpbkpuLib.Abstracts;

namespace Mpbkpu.Actions;

public class BackupAction : BaseAction
{
    private Arguments? _pArgs = null;
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

        var backupJob = new BackupJob(settings);
        backupJob.ConfirmBackup += OnConfirm;
        backupJob.LogEmitted += OnLog;
        backupJob.Progress += OnProgress;

        var cts = new CancellationTokenSource();
        try {
            var backupTask = Task.Run(() => backupJob.Run(cts.Token));
            await backupTask;
        } catch (Exception ex) {
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.ForegroundColor = DefaultColor;
        } finally {
            cts.Dispose();
        }

        return true;
    }

    private Task<bool> OnConfirm(object sender, BackupJob.ConfirmArgs e)
    {
        var res = Utility.Console.Confirm($"Backup {e.FilesToBackup} files ({e.BytesToBackup} bytes)?");
        return Task.FromResult(res);
    }

    private void OnProgress(object sender, int file, int totalFiles, long bytes, long totalBytes)
    {
        var perc = (double)bytes / (double)totalBytes * 100.0;
        Utility.Console.ShowProgressbar(20, perc);
    }

    private void OnLog(object sender, BaseSettings.LogLevels level, string message)
    {

    }
}