using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
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
            EncryptPassword = pArgs.Password
        };

        var job = new ListJob(settings);
        job.Step += OnStep;
        job.LogEmitted += OnLog;
        job.ListBackup += OnListBackup;

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
    private void OnStep(object sender, ListJob.Steps step)
    {
        Console.ForegroundColor = InfoColor;
        switch (step) {
            case ListJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case ListJob.Steps.ListBackups:
                Console.WriteLine("Listing backups");
                Console.WriteLine();
                Console.ForegroundColor = HighlightColor;
                Console.WriteLine($"{"Guid",-32} {"Type",-12} {"Result",-20} {"Started",-19} {"Completed",-19} Size");
                break;
        }

        Console.ForegroundColor = DefaultColor;
    }

    private void OnLog(object sender, BaseSettings.LogLevels level, string message)
    {
        Utility.Console.ClearLine();
        WriteLog(level, message);
    }

    private void OnListBackup(object sender, Backup backup, int files, long bytes)
    {
        Console.Write($"{backup.Guid,-32} {backup.Type,-12}");

        switch (backup.Result) {
            case Backup.Results.Completed:
                Console.ForegroundColor = SuccessColor;
                break;
            case Backup.Results.Cancelled:
                Console.ForegroundColor = WarningColor;
                break;
            case Backup.Results.Error:
                Console.ForegroundColor = ErrorColor;
                break;
        }

        Console.Write($" {backup.Result,-20}");
        Console.ForegroundColor = DefaultColor;
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