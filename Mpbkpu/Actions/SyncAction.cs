using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class SyncAction : BaseAction
{
    private Arguments? _pArgs;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var destination = pArgs.GetSingleDestination();
        var settings = WithCommonSettings(new SyncDirectoryJobSettings()
        {
            Source = pArgs.SourcePath,
            SourceUser = pArgs.SourceUser,
            SourcePassword = pArgs.SourcePassword,
            SourceOauthSettings = pArgs.GetSourceOauthSettings(),

            Destination = destination.Path,
            DestinationUser = destination.User,
            DestinationPassword = destination.Password,
            DestinationOauthSettings = destination.OauthSettings,

            IncludeHiddenFiles = true,
            SkipHashCheck = pArgs.SkipHashCheck,

            Exclude = pArgs.Exclude,
            RetryOnError = pArgs.RetryOnError ?? 3,
        }, pArgs);

        Console.Write("Hash check : ");
        WriteEnabled(!pArgs.SkipHashCheck);
        Console.WriteLine();

        using var job = new SyncDirectoryJob(settings);
        job.Step += OnStep;
        job.CopyBackup += OnCopyBackup;
        job.ConfirmBackup += OnConfirm;
        job.DeleteProgress += OnDeleteProgress;
        job.FileProgress += OnFileProgress;
        job.Progress += OnProgress;
        return Succeeded(await RunJob(job));
    }

    #region private operations
    private void OnStep(object sender, SyncDirectoryJob.Steps step)
    {
        if (step == SyncDirectoryJob.Steps.ReadSource)
            CanCancel = true;
        WriteStep(step switch
        {
            SyncDirectoryJob.Steps.ReadSource => "Reading source files",
            SyncDirectoryJob.Steps.ReadDestination => "Reading destination files",
            SyncDirectoryJob.Steps.CalculateFilesToBackup => "Calculating files to backup",
            SyncDirectoryJob.Steps.BackupFiles => "Backing up files",
            _ => step.ToString()
        });
    }

    private Task<bool> OnConfirm(object sender, SyncDirectoryJob.ConfirmArgs e)
    {
        var msg = e.FilesToDelete > 0 ?
            $"Delete {e.FilesToDelete} ({MpbkpuLib.Utility.String.BytesToString(e.BytesToDelete)}), backup {e.FilesToBackup} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToBackup)})?" :
            $"Backup {e.FilesToBackup} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToBackup)})?";
        return Task.FromResult(_pArgs!.AlwaysYes || Confirm($"{msg}{SpaceWarning(e)}", !e.NotEnoughSpace));
    }
    #endregion
}
