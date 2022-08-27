using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class SyncAction : BaseAction
{
    private Arguments? _pArgs;
    private double _totalPerc;
    private TimeSpan _eta = TimeSpan.Zero;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var settings = new SyncDirectoryJobSettings()
        {
            LogLevel = pArgs.LogLevel,

            Source = pArgs.SourcePath,
            SourceUser = pArgs.SourceUser,
            SourcePassword = pArgs.SourcePassword,
            SourceOauthSettings = new OAuth2Settings()
            {
                ServerUrl = pArgs.SourceOauthServerUrl,
                RedirectUrl = pArgs.SourceOauthRedirectUrl,
                AppName = pArgs.SourceOauthAppName,
                AppId = pArgs.SourceOauthAppId,
                AppSecret = pArgs.SourceOauthAppSecret,
            },

            Destination = pArgs.DestinationPath,
            DestinationUser = pArgs.DestinationUser,
            DestinationPassword = pArgs.DestinationPassword,
            DestinationOauthSettings = new OAuth2Settings()
            {
                ServerUrl = pArgs.DestinationOauthServerUrl,
                RedirectUrl = pArgs.DestinationOauthRedirectUrl,
                AppName = pArgs.DestinationOauthAppName,
                AppId = pArgs.DestinationOauthAppId,
                AppSecret = pArgs.DestinationOauthAppSecret,
            },

            IncludeHiddenFiles = true,
            SkipHashCheck = pArgs.SkipHashCheck,

            Exclude = pArgs.Exclude,
            RetryOnError = pArgs.RetryOnError ?? 3,
            LogToConsole = false,
            LogFile = pArgs.LogFile
        };

        Console.Write("Hash check : ");
        WriteEnabled(!pArgs.SkipHashCheck);
        Console.WriteLine();

        var job = new SyncDirectoryJob(settings);
        job.Step += OnStep;
        job.OpenBackup += OnCopyBackup;
        job.CopyBackup += OnCopyBackup;
        job.ConfirmBackup += OnConfirm;
        job.LogEmitted += OnLog;
        job.DeleteProgress += OnDeleteProgress;
        job.FileProgress += OnFileProgress;
        job.Progress += OnProgress;

        BaseJobResult? jobRes = null;
        using (CancellationTokenSource = new CancellationTokenSource()) {
            try {
                Console.CursorVisible = false;
                var task = Task.Run(() => job.Run(CancellationTokenSource.Token));
                jobRes = await task;
                Utility.Console.ClearLine();
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
        Bytes = jobRes?.Bytes;

        return true;
    }

    #region private operations
    private void OnStep(object sender, SyncDirectoryJob.Steps step)
    {
        Utility.Console.ClearLine();
        Console.ForegroundColor = Utility.Console.InfoColor;
        switch (step) {
            case SyncDirectoryJob.Steps.ReadSource:
                CanCancel = true;
                Console.WriteLine("Reading source files");
                break;
            case SyncDirectoryJob.Steps.ReadDestination:
                Console.WriteLine("Reading destination files");
                break;
            case SyncDirectoryJob.Steps.CalculateFilesToBackup:
                Console.WriteLine("Calculating files to backup");
                break;
            case SyncDirectoryJob.Steps.BackupFiles:
                Console.WriteLine("Backing up files");
                break;
        }

        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    private Task<bool> OnConfirm(object sender, SyncDirectoryJob.ConfirmArgs e)
    {
        if (_pArgs!.AlwaysYes)
            return Task.FromResult(true);
        Console.CursorVisible = true;
        var msg = e.FilesToDelete > 0 ?
            $"Delete {e.FilesToDelete} ({MpbkpuLib.Utility.String.BytesToString(e.BytesToDelete)}), backup {e.FilesToBackup} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToBackup)})?" :
            $"Backup {e.FilesToBackup} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToBackup)})?";
        var res = Utility.Console.Confirm(msg, true, true);
        Console.CursorVisible = false;
        return Task.FromResult(res);
    }

    private void OnDeleteProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        Console.CursorLeft = 0;
        Console.Write($"[{(args.File / (double)args.TotalFiles * 100.0),5:0.0}%]");
    }

    private void OnFileProgress(object sender, string filePath, long bytes, long totalBytes)
    {
        var perc = GetPercentage(bytes, totalBytes);
        var progress = $"[{_totalPerc,5:0.0}% {MpbkpuLib.Utility.String.TimeSpanToString(_eta)}][{perc,5:0.0}%]";
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
        Console.Write($"[{_totalPerc,5:0.0}% {MpbkpuLib.Utility.String.TimeSpanToString(_eta)}]");
    }
    #endregion
}