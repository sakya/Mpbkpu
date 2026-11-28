using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class DeleteAction : BaseAction
{
    private Arguments? _pArgs;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var settings = WithCommonSettings(new DeleteJobSettings()
        {
            BackupDestinations = pArgs.GetBackupDestinations(),
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password
        }, pArgs);

        using var job = new DeleteJob(settings);
        job.Step += OnStep;
        job.CopyBackup += OnCopyBackup;
        job.ConfirmDelete += OnConfirm;
        job.Delete += OnDelete;
        job.Progress += OnDeleteBytesProgress;
        var res = await RunJob(job);
        Bytes = null;
        return Succeeded(res);
    }

    #region private operations
    private void OnStep(object sender, DeleteJob.Steps step)
    {
        if (step == DeleteJob.Steps.DeleteBackups)
            CanCancel = true;
        WriteStep(step switch
        {
            DeleteJob.Steps.OpenDatabase => "Opening backup",
            DeleteJob.Steps.DeleteBackups => "Deleting backups",
            DeleteJob.Steps.CopyDatabase => "Copying database file",
            DeleteJob.Steps.DeleteFiles => "Deleting files",
            _ => step.ToString()
        });
    }

    private Task<bool> OnConfirm(object sender, DeleteJob.ConfirmArgs e)
    {
        if (_pArgs!.AlwaysYes)
            return Task.FromResult(true);
        Console.WriteLine("The following backups will be deleted:");
        Console.WriteLine();
        Console.ForegroundColor = Utility.Console.HighlightColor;
        Console.WriteLine($"{"Guid",-32} {"Type",-12} {"Result",-20} Size");
        Console.ForegroundColor = Utility.Console.DefaultColor;
        foreach (var bkp in e.Backups) {
            Console.Write($"{bkp.Guid,-32} {bkp.Type,-12}");
            WriteResult(bkp.Result);
            Console.WriteLine($" {MpbkpuLib.Utility.String.BytesToString(bkp.Bytes)}");
        }
        Console.WriteLine();
        return Task.FromResult(Confirm("Delete the backups?", false));
    }

    private void OnDelete(object sender, string guid)
    {
        Utility.Console.ClearLine();
        Console.WriteLine($"Deleting backup {guid}");
    }

    private static void OnDeleteBytesProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        Utility.Console.WriteProgress($"[{GetPercentage(args.Bytes, args.TotalBytes),5:0.0}% {MpbkpuLib.Utility.String.EtaToString(args.Eta)}]");
    }
    #endregion
}
