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
            LogFile = pArgs.LogFile
        };

        Console.WriteLine($"Type      : { pArgs.BackupType }");
        Console.Write("Compress  : ");
        WriteEnabled(pArgs.Compress);
        Console.Write("Encryption: ");
        WriteEnabled(!string.IsNullOrEmpty(pArgs.Password));
        Console.WriteLine();

        var job = new BackupJob(settings);
        job.Step += OnStep;
        job.ConfirmBackup += OnConfirm;
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

        return true;
    }

    #region private operations
    private void OnStep(object sender, BackupJob.Steps step)
    {
        Console.ForegroundColor = InfoColor;
        switch (step) {
            case BackupJob.Steps.OpenDatabase:
                Console.WriteLine("Opening database");
                break;
            case BackupJob.Steps.ReadSource:
                Console.WriteLine("Reading source files");
                break;
            case BackupJob.Steps.CalculateFilesToBackup:
                Console.WriteLine("Calculating files to backup");
                break;
        }

        Console.ForegroundColor = DefaultColor;
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
        WriteLog(level, message);
    }
    #endregion
}