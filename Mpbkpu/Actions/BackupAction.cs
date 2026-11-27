using System.IO.Compression;
using MpbkpuLib.Jobs;

namespace Mpbkpu.Actions;

public class BackupAction : BaseAction
{
    private Arguments? _pArgs;

    public override async Task<bool> Run(Arguments pArgs)
    {
        _pArgs = pArgs;
        var settings = WithCommonSettings(new BackupJobSettings()
        {
            BackupType = pArgs.BackupType!.Value,

            Source = pArgs.SourcePath,
            SourceUser = pArgs.SourceUser,
            SourcePassword = pArgs.SourcePassword,
            SourceOauthSettings = pArgs.GetSourceOauthSettings(),

            Destinations = pArgs.GetDestinations(),

            SkipHashCheck = pArgs.SkipHashCheck,

            IncludeHiddenFiles = true,
            Exclude = pArgs.Exclude,
            RetryOnError = pArgs.RetryOnError ?? 3,
            FullBackupEveryDays = pArgs.FullBackupEveryDays ?? 0,
            KeepChains = pArgs.KeepChains ?? 0,
            Verify = pArgs.Verify,
            ParallelFiles = pArgs.ParallelFiles ?? 1,
            EncryptPassword = pArgs.Password,
            CompressionLevel = pArgs.CompressionLevel,
        }, pArgs);

        Console.WriteLine($"Type       : { pArgs.BackupType }");
        Console.Write("Compression: ");
        WriteEnabled(pArgs.CompressionLevel != CompressionLevel.NoCompression);
        Console.Write("Encryption : ");
        WriteEnabled(!string.IsNullOrEmpty(pArgs.Password));
        Console.Write("Hash check : ");
        WriteEnabled(!pArgs.SkipHashCheck);
        Console.WriteLine($"Verify     : { pArgs.Verify }");
        Console.WriteLine();

        using var job = new BackupJob(settings);
        job.Step += OnStep;
        job.CopyBackup += OnCopyBackup;
        job.ConfirmBackup += OnConfirm;
        job.FileProgress += OnFileProgress;
        job.Progress += OnProgress;
        return Succeeded(await RunJob(job));
    }

    #region private operations
    private void OnStep(object sender, BackupJob.Steps step)
    {
        if (step == BackupJob.Steps.ReadSource)
            CanCancel = true;
        WriteStep(step switch
        {
            BackupJob.Steps.OpenDatabase => "Opening backup",
            BackupJob.Steps.ReadSource => "Reading source files",
            BackupJob.Steps.CalculateFilesToBackup => "Calculating files to backup",
            BackupJob.Steps.BackupFiles => "Backing up files",
            BackupJob.Steps.CopyDatabase => "Copying database file",
            BackupJob.Steps.DeleteOldBackups => "Deleting old backups",
            BackupJob.Steps.VerifyBackup => "Verifying backup",
            _ => step.ToString()
        });
    }

    private Task<bool> OnConfirm(object sender, BackupJob.ConfirmArgs e)
    {
        return Task.FromResult(_pArgs!.AlwaysYes ||
            Confirm($"Backup {e.FilesToBackup} files ({MpbkpuLib.Utility.String.BytesToString(e.BytesToBackup)})?{SpaceWarning(e)}",
                !e.NotEnoughSpace));
    }
    #endregion
}
