using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using File = MpbkpuLib.DataModel.File;

namespace Mpbkpu.Actions;

public class ListContentAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        var settings = new ListContentJobSettings()
        {
            LogLevel = pArgs.LogLevel,
            BackupPath = pArgs.BackupPath,
            BackupPathUser = pArgs.BackupUser,
            BackupPathPassword = pArgs.BackupPassword,
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password
        };

        var job = new ListContentJob(settings);
        job.Step += OnStep;
        job.LogEmitted += OnLog;
        job.Content += OnContent;

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
    private void OnStep(object sender, ListContentJob.Steps step)
    {
        Console.ForegroundColor = InfoColor;
        switch (step) {
            case ListContentJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case ListContentJob.Steps.ListContent:
                Console.WriteLine("Listing content");
                break;
        }

        Console.ForegroundColor = DefaultColor;
    }

    private void OnLog(object sender, BaseJobSettings.LogLevels level, string message)
    {
        Utility.Console.ClearLine();
        WriteLog(level, message);
    }

    private void OnContent(object sender, BackupItem item)
    {
        Console.Write(item.Type == File.FileTyes.Directory ? "[DIR ]" : "[FILE]");
        Console.WriteLine(item.OriginalRelativePath);
    }
    #endregion
}