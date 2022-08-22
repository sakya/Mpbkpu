using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class DeleteAction : BaseAction
{
    private Arguments? _pArgs;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var settings = new DeleteJobSettings()
        {
            LogLevel = pArgs.LogLevel,
            BackupPath = pArgs.BackupPath,
            BackupPathUser = pArgs.BackupUser,
            BackupPathPassword = pArgs.BackupPassword,
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password
        };
        var job = new DeleteJob(settings);
        job.Step += OnStep;
        job.ConfirmDelete += OnConfirm;
        job.LogEmitted += OnLog;
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

        return true;
    }

    #region private operations
    private void OnStep(object sender, DeleteJob.Steps step)
    {
        Console.ForegroundColor = Utility.Console.InfoColor;
        switch (step) {
            case DeleteJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case DeleteJob.Steps.DeleteFiles:
                Console.WriteLine("Deleting files");
                break;
            case DeleteJob.Steps.CopyDatabase:
                Console.WriteLine("Copying database file");
                break;
        }

        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    private Task<bool> OnConfirm(object sender, DeleteJob.ConfirmArgs e)
    {
        if (_pArgs!.AlwaysYes == true)
            return Task.FromResult(true);
        Console.CursorVisible = true;
        var res = Utility.Console.Confirm($"Delete backup with GUID {e.Guid} containing {e.FilesToDelete} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToDelete)})?", false, true);
        Console.CursorVisible = false;
        return Task.FromResult(res);
    }

    private void OnProgress(object sender, BaseJob.ProgressHandlerArgs args)
    {
        var perc = args.Bytes / (double)args.TotalBytes * 100.0;
        var progress = $"[{perc,5:0.0}%]";

        Console.CursorLeft = 0;
        Console.Write(progress.PadRight(Console.WindowWidth - 1));
    }
    #endregion
}