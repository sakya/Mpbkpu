using MpbkpuLib;
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
            BackupGuid = pArgs.BackupGuid
        };
        var job = new DeleteJob(settings);
        job.Step += OnStep;
        job.ConfirmDelete += OnConfirm;
        job.LogEmitted += OnLog;

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
    private void OnStep(object sender, DeleteJob.Steps step)
    {
        Console.ForegroundColor = InfoColor;
        switch (step) {
            case DeleteJob.Steps.OpenDatabase:
                Console.WriteLine("Opening database");
                break;
            case DeleteJob.Steps.DeleteFiles:
                Console.WriteLine("Deleting files");
                break;
        }

        Console.ForegroundColor = DefaultColor;
    }

    private Task<bool> OnConfirm(object sender, DeleteJob.ConfirmArgs e)
    {
        if (_pArgs!.AlwaysYes == true)
            return Task.FromResult(true);
        Console.CursorVisible = true;
        var res = Utility.Console.Confirm($"Delete backup with GUID {e.Guid} containing {e.FilesToDelete} files ({Utility.String.BytesToString(e.BytesToDelete)})?", false);
        Console.CursorVisible = false;
        return Task.FromResult(res);
    }

    private void OnLog(object sender, BaseSettings.LogLevels level, string message)
    {
        WriteLog(level, message);
    }
    #endregion
}