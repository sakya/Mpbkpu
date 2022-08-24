using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
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
            BackupPathUser = pArgs.BackupUser,
            BackupPathPassword = pArgs.BackupPassword,
            BackupOauthSettings = new OAuth2Settings()
            {
                ServerUrl = pArgs.BackupOauthServerUrl,
                RedirectUrl = pArgs.BackupOauthRedirectUrl,
                AppName = pArgs.BackupOauthAppName,
                AppId = pArgs.BackupOauthAppId,
                AppSecret = pArgs.BackupOauthAppSecret,
            },
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password
        };

        var job = new VerifyJob(settings);
        job.Step += OnStep;
        job.LogEmitted += OnLog;
        job.Progress += OnProgress;

        CancellationTokenSource = new CancellationTokenSource();
        BaseJobResult? jobRes = null;
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

        return true;
    }

    #region private operations
    private void OnStep(object sender, VerifyJob.Steps step)
    {
        Console.ForegroundColor = Utility.Console.InfoColor;
        switch (step) {
            case VerifyJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case VerifyJob.Steps.VerifyFiles:
                Console.WriteLine("Verifying files");
                break;
        }

        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    private void OnProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        var perc = args.Bytes / (double)args.TotalBytes * 100.0;
        var progress = $"[{perc,5:0.0}% {MpbkpuLib.Utility.String.TimeSpanToString(args.Eta)}]";

        Console.CursorLeft = 0;
        Console.Write(progress.PadRight(Console.WindowWidth - 1));
    }
    #endregion
}