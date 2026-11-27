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
        var settings = WithCommonSettings(new ListJobSettings()
        {
            BackupDestinations = pArgs.GetBackupDestinations(),
            EncryptPassword = pArgs.Password
        }, pArgs);

        using var job = new ListJob(settings);
        job.Step += OnStep;
        job.ListBackup += OnListBackup;
        var res = await RunJob(job);
        // Listing has no duration or size to report
        Started = Completed = null;
        Bytes = null;
        return Succeeded(res);
    }

    #region private operations
    private void OnStep(object sender, ListJob.Steps step)
    {
        switch (step) {
            case ListJob.Steps.OpenDatabase:
                WriteStep("Opening backup");
                break;
            case ListJob.Steps.ListBackups:
                CanCancel = true;
                WriteStep("Listing backups");
                Console.WriteLine();
                Console.ForegroundColor = Utility.Console.HighlightColor;
                Console.WriteLine($"{"Guid",-32} {"Type",-12} {"Result",-20} {"Started",-19} {"Completed",-19} Size");
                Console.ForegroundColor = Utility.Console.DefaultColor;
                break;
        }
    }

    private void OnListBackup(object sender, Backup backup, int files, long bytes)
    {
        Console.Write($"{backup.Guid,-32} {backup.Type,-12}");
        WriteResult(backup.Result);
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
