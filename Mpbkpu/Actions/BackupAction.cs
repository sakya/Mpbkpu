using System.IO.Compression;
using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class BackupAction : BaseAction
{
    private Arguments? _pArgs;
    private double _totalPerc;
    private TimeSpan _eta = TimeSpan.Zero;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var settings = new BackupJobSettings()
        {
            BackupType = pArgs.BackupType!.Value,
            LogLevel = pArgs.LogLevel,

            Source = pArgs.SourcePath,
            SourceUser = pArgs.SourceUser,
            SourcePassword = pArgs.SourcePassword,

            Destination = pArgs.DestinationPath,
            DestinationUser = pArgs.DestinationUser,
            DestinationPassword = pArgs.DestinationPassword,
            SkipHashCheck = pArgs.SkipHashCheck,

            Exclude = pArgs.Exclude,
            RetryOnError = pArgs.RetryOnError ?? 3,
            EncryptPassword = pArgs.Password,
            CompressionLevel = pArgs.CompressionLevel,
            LogToConsole = false,
            LogFile = pArgs.LogFile
        };

        Console.WriteLine($"Type       : { pArgs.BackupType }");
        Console.Write("Compression: ");
        WriteEnabled(pArgs.CompressionLevel != CompressionLevel.NoCompression);
        Console.Write("Encryption : ");
        WriteEnabled(!string.IsNullOrEmpty(pArgs.Password));
        Console.WriteLine();

        var job = new BackupJob(settings);
        job.Step += OnStep;
        job.ConfirmBackup += OnConfirm;
        job.LogEmitted += OnLog;
        job.FileProgress += OnFileProgress;
        job.Progress += OnProgress;

        BaseJobResult? jobRes = null;
        CancellationTokenSource = new CancellationTokenSource();
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
            CancellationTokenSource.Dispose();
        }

        Started = jobRes?.Started;
        Completed = jobRes?.Completed;
        Bytes = jobRes?.Bytes;

        return true;
    }

    #region private operations
    private void OnStep(object sender, BackupJob.Steps step)
    {
        Utility.Console.ClearLine();
        Console.ForegroundColor = Utility.Console.InfoColor;
        switch (step) {
            case BackupJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case BackupJob.Steps.ReadSource:
                Console.WriteLine("Reading source files");
                break;
            case BackupJob.Steps.CalculateFilesToBackup:
                Console.WriteLine("Calculating files to backup");
                break;
            case BackupJob.Steps.BackupFiles:
                Console.WriteLine("Backing up files");
                break;
            case BackupJob.Steps.CopyDatabase:
                Console.WriteLine("Copying database file");
                break;
        }

        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    private Task<bool> OnConfirm(object sender, BackupJob.ConfirmArgs e)
    {
        if (_pArgs!.AlwaysYes)
            return Task.FromResult(true);
        Console.CursorVisible = true;
        var res = Utility.Console.Confirm($"Backup {e.FilesToBackup} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToBackup)})?", true, true);
        Console.CursorVisible = false;
        return Task.FromResult(res);
    }

    private void OnFileProgress(object sender, string filePath, long bytes, long totalBytes)
    {
        var perc = bytes / (double)totalBytes * 100.0;
        if (bytes == 0 && totalBytes == 0)
            perc = 100;
        var progress = $"[{MpbkpuLib.Utility.String.TimeSpanToString(_eta)}][{_totalPerc,5:0.0}%][{perc,5:0.0}%]";
        if (filePath.Length > Console.WindowWidth - progress.Length - 1)
            filePath = $"...{ filePath.Substring(filePath.Length - (Console.WindowWidth - progress.Length) + 4) }";
        progress = $"{progress}{filePath}";

        Console.CursorLeft = 0;
        Console.Write(progress.PadRight(Console.WindowWidth - 1));
    }

    private void OnProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        _totalPerc = args.Bytes / (double)args.TotalBytes * 100.0;
        _eta = args.Eta;
        Console.CursorLeft = 0;
        Console.Write($"[{MpbkpuLib.Utility.String.TimeSpanToString(_eta)}][{_totalPerc,5:0.0}%]");
    }
    #endregion
}