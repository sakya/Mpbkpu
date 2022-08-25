using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

/// <summary>
/// List backups
/// </summary>
public class ListAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        var settings = new ListJobSettings()
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
            EncryptPassword = pArgs.Password
        };

        var job = new ListJob(settings);
        job.Step += OnStep;
        job.OpenBackup += OnOpenBackup;
        job.LogEmitted += OnLog;
        job.ListBackup += OnListBackup;

        CancellationTokenSource = new CancellationTokenSource();
        try {
            Console.CursorVisible = false;
            var task = Task.Run(() => job.Run(CancellationTokenSource.Token));
            await task;
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

        return true;
    }

    #region private operations
    private void OnStep(object sender, ListJob.Steps step)
    {
        Utility.Console.ClearLine();
        Console.ForegroundColor = Utility.Console.InfoColor;
        switch (step) {
            case ListJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case ListJob.Steps.ListBackups:
                CanCancel = true;
                Console.WriteLine("Listing backups");
                Console.WriteLine();
                Console.ForegroundColor = Utility.Console.HighlightColor;
                Console.WriteLine($"{"Guid",-32} {"Type",-12} {"Result",-20} {"Started",-19} {"Completed",-19} Size");
                break;
        }

        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    private void OnListBackup(object sender, Backup backup, int files, long bytes)
    {
        Console.Write($"{backup.Guid,-32} {backup.Type,-12}");

        switch (backup.Result) {
            case Backup.Results.Completed:
                Console.ForegroundColor = Utility.Console.SuccessColor;
                break;
            case Backup.Results.Cancelled:
                Console.ForegroundColor = Utility.Console.WarningColor;
                break;
            case Backup.Results.Error:
                Console.ForegroundColor = Utility.Console.ErrorColor;
                break;
        }

        Console.Write($" {backup.Result,-20}");
        Console.ForegroundColor = Utility.Console.DefaultColor;
        Console.Write(
            $" {$"{backup.StartedAt!.Value.ToLocalTime().ToShortDateString()} {backup.StartedAt!.Value.ToLocalTime().ToShortTimeString()}",-19}");
        if (backup.CompletedAt.HasValue)
            Console.Write(
                $" {$"{backup.CompletedAt!.Value.ToLocalTime().ToShortDateString()} {backup.CompletedAt!.Value.ToLocalTime().ToShortTimeString()}",-19}");
        else
            Console.Write($"{"",-19}");
        Console.Write($" {MpbkpuLib.Utility.String.BytesToString(bytes)}");
        Console.WriteLine();
    }
    #endregion
}