using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using Newtonsoft.Json;

namespace MpbkpuLib.Jobs;

public class ListJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        ListBackups
    }

    #region private members
    private CancellationToken? _cancellationToken;
    #endregion

    #region delegates
    public delegate void StepHandler(object sender, Steps step);
    public event StepHandler? Step;

    public delegate void ListBackupHandler(object sender, Backup backup, int files, long bytes);
    public event ListBackupHandler? ListBackup;
    #endregion

    public ListJob(ListJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new ListJobSettings Settings
    {
        get { return (base.Settings as ListJobSettings)!; }
    }
    #endregion

    public override async Task<BaseJobResult> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        var started = DateTime.UtcNow;
        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        BackupFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out BackupPath, Settings.BackupPathUser, Settings.BackupPathPassword);
        Step?.Invoke(this, Steps.OpenDatabase);
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword)) {
            var backupFile = openRes.BackupFile;
            try {
                await RunPrimitive(backupFile);
            } catch (Exception ex) {
                LogError(ex);
                throw;
            }
        }

        return new ListJobResult()
        {
            Success = true,
            Started = started,
            Completed = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Validate the <see cref="VerifyJobSettings"/>
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    protected override void ValidateSettings()
    {
        if (string.IsNullOrEmpty(Settings.BackupPath))
            throw new ArgumentNullException(nameof(Settings.BackupPath), "Backup path cannot be empty");
    }

    #region private operations
    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to use</param>
    /// <returns>True on success</returns>
    private Task<bool> RunPrimitive(BackupFile backupFile)
    {
        Step?.Invoke(this, Steps.ListBackups);
        var backups = backupFile.Backups.OrderBy(b => b.Id)
            .Select(b => new
            {
                Backup = b,
                Files = b.Items.Count(),
                Bytes = b.Items.Sum(i => i.OriginalSize)
            });
        foreach (var bkp in backups.OrderBy(b => b.Backup.Id)) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                Task.FromResult(true);
            }
            ListBackup?.Invoke(this, bkp.Backup, bkp.Files, bkp.Bytes);
        }

        return Task.FromResult(true);
    }
    #endregion
}