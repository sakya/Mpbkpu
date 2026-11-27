using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class RestoreAction : BaseAction
{
    private Arguments? _pArgs;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var destination = pArgs.GetSingleDestination();
        var settings = WithCommonSettings(new RestoreJobSettings()
        {
            BackupDestinations = pArgs.GetBackupDestinations(),
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password,

            Destination = destination.Path,
            DestinationUser = destination.User,
            DestinationPassword = destination.Password,
            DestinationOauthSettings = destination.OauthSettings,

            SkipHashCheck = pArgs.SkipHashCheck,
            RetryOnError = pArgs.RetryOnError ?? 3,
        }, pArgs);

        using var job = new RestoreJob(settings);
        job.Step += OnStep;
        job.ConfirmRestore += OnConfirm;
        job.FileProgress += OnFileProgress;
        job.DeleteProgress += OnDeleteProgress;
        job.RestoreProgress += OnProgress;
        return Succeeded(await RunJob(job));
    }

    #region private operations
    private void OnStep(object sender, RestoreJob.Steps step)
    {
        if (step == RestoreJob.Steps.ReadDestination)
            CanCancel = true;
        WriteStep(step switch
        {
            RestoreJob.Steps.OpenDatabase => "Opening backup",
            RestoreJob.Steps.ReadDestination => "Reading destination files",
            RestoreJob.Steps.CalculateFilesToRestore => "Calculating files to restore",
            RestoreJob.Steps.DeleteFiles => "Deleting files",
            RestoreJob.Steps.RestoreFiles => "Restoring files",
            _ => step.ToString()
        });
    }

    private Task<bool> OnConfirm(object sender, RestoreJob.ConfirmArgs e)
    {
        var msg = e.FilesToDelete > 0 ?
            $"Delete {e.FilesToDelete} ({MpbkpuLib.Utility.String.BytesToString(e.BytesToDelete)}), restore {e.FilesToRestore} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToRestore)})?" :
            $"Restore {e.FilesToRestore} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToRestore)})?";
        return Task.FromResult(_pArgs!.AlwaysYes || Confirm(msg, true));
    }
    #endregion
}
