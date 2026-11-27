using MpbkpuLib.DataModel;
using MpbkpuLib.Jobs;
using File = MpbkpuLib.DataModel.File;

namespace Mpbkpu.Actions;

public class ListContentAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        var settings = WithCommonSettings(new ListContentJobSettings()
        {
            BackupDestinations = pArgs.GetBackupDestinations(),
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password,
            Filter = pArgs.Filter,
        }, pArgs);

        using var job = new ListContentJob(settings);
        job.Step += OnStep;
        job.Content += OnContent;
        var res = await RunJob(job);
        if (res is ListContentJobResult content && !UserCancelled()) {
            Console.WriteLine();
            Console.WriteLine($"Directories: {content.DirectoriesCount}");
            Console.WriteLine($"Files      : {content.FilesCount}");
        }
        // Listing has no duration or size to report
        Started = Completed = null;
        Bytes = null;
        return Succeeded(res);
    }

    #region private operations
    private void OnStep(object sender, ListContentJob.Steps step)
    {
        switch (step) {
            case ListContentJob.Steps.OpenDatabase:
                WriteStep("Opening backup");
                break;
            case ListContentJob.Steps.ListContent:
                CanCancel = true;
                WriteStep("Listing content");
                Console.WriteLine();
                Console.ForegroundColor = Utility.Console.HighlightColor;
                Console.WriteLine($"{"Type",-4} {"Size",-12} Name");
                Console.ForegroundColor = Utility.Console.DefaultColor;
                break;
        }
    }

    private void OnContent(object sender, BackupItem item)
    {
        Console.WriteLine($"{(item.Type == File.FileTyes.Directory ? "Dir" : "File"), -4} {MpbkpuLib.Utility.String.BytesToString(item.Size), -12} {item.OriginalRelativePath}");
    }
    #endregion
}
