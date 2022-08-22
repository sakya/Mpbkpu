using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using Newtonsoft.Json;

namespace MpbkpuLib.Jobs;

public class ListContentJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        ListContent
    }

    #region private members
    private CancellationToken? _cancellationToken;
    #endregion

    #region delegates
    public delegate void StepHandler(object sender, Steps step);
    public event StepHandler? Step;

    public delegate void ContentHandler(object sender, BackupItem item);
    public event ContentHandler? Content;
    #endregion

    public ListContentJob(ListContentJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new ListContentJobSettings Settings
    {
        get { return (base.Settings as ListContentJobSettings)!; }
    }
    #endregion

    #region public operations
    public override async Task<BaseJobResult> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        var started = DateTime.UtcNow;
        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        BackupFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out BackupPath, Settings.BackupPathUser, Settings.BackupPathPassword);
        Step?.Invoke(this, Steps.OpenDatabase);
        ListContentJobResult? res;
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword)) {
            var backupFile = openRes.BackupFile;
            try {
                res = await RunPrimitive(backupFile);
            } catch (Exception ex) {
                LogError(ex);
                throw;
            }
        }

        res.Started = started;
        res.Completed = DateTime.UtcNow;
        return res;
    }
    #endregion

    #region private operations

    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to use</param>
    /// <returns>True on success</returns>
    private async Task<ListContentJobResult> RunPrimitive(BackupFile backupFile)
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null) {
            LogError($"Backup with GUID {Settings.BackupGuid} not found");
            return new ListContentJobResult()
            {
                Success = false,
                ErrorMessage = $"Backup with GUID {Settings.BackupGuid} not found"
            };
        }

        Step?.Invoke(this, Steps.ListContent);
        await foreach (var item in backupFile.GetBackupItems(bkp.Id)) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                return new ListContentJobResult()
                {
                    Success = false,
                    ErrorMessage = "Cancelled by user"
                };
            }
            Content?.Invoke(this, item);
        }

        return new ListContentJobResult()
        {
            Success = true
        };
    }

    protected override void ValidateSettings()
    {
        if (string.IsNullOrEmpty(Settings.BackupPath))
            throw new ArgumentNullException(nameof(Settings.BackupPath), "Backup path cannot be empty");
        if (string.IsNullOrEmpty(Settings.BackupGuid))
            throw new ArgumentNullException(nameof(Settings.BackupGuid), "Backup GUID cannot be empty");
    }
    #endregion
}