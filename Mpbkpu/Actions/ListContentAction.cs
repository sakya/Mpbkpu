using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using File = MpbkpuLib.DataModel.File;

namespace Mpbkpu.Actions;

public class ListContentAction : BaseAction
{
    private int _filesCount = 0;
    private int _directoriesCount = 0;

    public override async Task<bool> Run(Arguments pArgs)
    {
        var settings = new ListContentJobSettings()
        {
            LogLevel = pArgs.LogLevel,
            BackupPath = pArgs.BackupPath,
            BackupPathUser = pArgs.BackupUser,
            BackupPathPassword = pArgs.BackupPassword,
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password,
            Filter = pArgs.Filter,
        };

        var job = new ListContentJob(settings);
        job.Step += OnStep;
        job.LogEmitted += OnLog;
        job.Content += OnContent;

        CancellationTokenSource = new CancellationTokenSource();
        try {
            Console.CursorVisible = false;
            var task = Task.Run(() => job.Run(CancellationTokenSource.Token));
            await task;

            if (!CancellationTokenSource.Token.IsCancellationRequested) {
                Console.WriteLine();
                Console.WriteLine($"Directories: {_directoriesCount}");
                Console.WriteLine($"Files      : {_filesCount}");
            }
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
    private void OnStep(object sender, ListContentJob.Steps step)
    {
        Console.ForegroundColor = Utility.Console.InfoColor;
        switch (step) {
            case ListContentJob.Steps.OpenDatabase:
                Console.WriteLine("Opening backup");
                break;
            case ListContentJob.Steps.ListContent:
                Console.WriteLine("Listing content");
                Console.WriteLine();
                Console.ForegroundColor = Utility.Console.HighlightColor;
                Console.WriteLine($"{"Type",-4} {"Size",-12} Name");
                break;
        }

        Console.ForegroundColor = Utility.Console.DefaultColor;
    }

    private void OnContent(object sender, BackupItem item)
    {
        switch (item.Type) {
            case File.FileTyes.File:
                _filesCount++;
                break;
            case File.FileTyes.Directory:
                _directoriesCount++;
                break;
        }

        Console.WriteLine($"{(item.Type == File.FileTyes.Directory ? "Dir" : "File"), -4} {MpbkpuLib.Utility.String.BytesToString(item.Size), -12} {item.OriginalRelativePath}");
    }
    #endregion
}