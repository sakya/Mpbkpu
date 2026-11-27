using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class VerifyAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        var settings = WithCommonSettings(new VerifyJobSettings()
        {
            BackupDestinations = pArgs.GetBackupDestinations(),
            BackupGuid = pArgs.BackupGuid,
            EncryptPassword = pArgs.Password
        }, pArgs);

        using var job = new VerifyJob(settings);
        job.Step += OnStep;
        job.Progress += OnProgress;
        job.FileProgress += OnFileProgress;
        var res = await RunJob(job);
        // Verify reports the bytes it read, not a backup size
        Bytes = null;
        return Succeeded(res);
    }

    #region private operations
    private void OnStep(object sender, VerifyJob.Steps step)
    {
        if (step == VerifyJob.Steps.VerifyFiles)
            CanCancel = true;
        WriteStep(step switch
        {
            VerifyJob.Steps.OpenDatabase => "Opening backup",
            VerifyJob.Steps.VerifyFiles => "Verifying files",
            _ => step.ToString()
        });
    }
    #endregion
}
