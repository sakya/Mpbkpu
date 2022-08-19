using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using Newtonsoft.Json;

namespace MpbkpuLib.Jobs;

public class DeleteJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        DeleteFiles,
        CopyDatabase
    }

    #region private members
    private CancellationToken? _cancellationToken;
    #endregion

    #region delegates
    public class ConfirmArgs : EventArgs
    {
        public string? Guid { get; set; }
        public int FilesToDelete { get; set; }
        public long BytesToDelete { get; set; }
    }
    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmDelete;

    public delegate void StepHandler(object sender, Steps step);

    public event StepHandler? Step;
    #endregion

    public DeleteJob(DeleteJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new DeleteJobSettings Settings
    {
        get { return (base.Settings as DeleteJobSettings)!; }
    }
    #endregion

    #region public operations
    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/></param>
    /// <returns>True on success</returns>
    public override async Task<BaseJobResult> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        Started = DateTime.UtcNow;
        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        BackupFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out BackupPath, Settings.BackupPathUser, Settings.BackupPathPassword);
        Step?.Invoke(this, Steps.OpenDatabase);
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            var transaction = backupFile.BeginTransaction();

            try {
                await RunPrimitive(backupFile);
                await transaction.CommitAsync();

                await backupFile.Vacuum();
                backupFile.Close();

                // Copy temporary database file to destination
                if (!string.IsNullOrEmpty(openRes.TempFilePath)) {
                    Step?.Invoke(this, Steps.CopyDatabase);
                    await CopyDatabaseToDestination(openRes, Settings.BackupPath, Settings.EncryptPassword);
                }
            } catch (Exception ex) {
                LogError(ex);
                await transaction.RollbackAsync();
                throw;
            } finally {
                Completed = DateTime.UtcNow;
                transaction.Dispose();
            }
        }

        return new DeleteJobResult()
        {
            Success = true
        };
    }
    #endregion

    #region private operations
    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to use</param>
    /// <returns>True on success</returns>
    private async Task RunPrimitive(BackupFile backupFile)
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with GUID { Settings.BackupGuid } not found");

        var cArgs = new ConfirmArgs()
        {
            Guid = Settings.BackupGuid,
            FilesToDelete = await backupFile.BackupItems.AsNoTracking()
                .Where(i => i.BackupId == bkp.Id)
                .CountAsync(),
            BytesToDelete = await backupFile.BackupItems.AsNoTracking()
                .Where(i => i.BackupId == bkp.Id)
                .Select(i => i.OriginalSize)
                .SumAsync(),
        };
        if (ConfirmDelete != null) {
            if (!await ConfirmDelete.Invoke(this, cArgs)) {
                LogInfo("Cancelled by user");
                return;
            }
        }

        // Delete files
        Step?.Invoke(this, Steps.DeleteFiles);

        var items = backupFile.BackupItems
            .Include(i => i.Volume)
            .Include(i => i.Backup)
            .AsNoTracking()
            .Where(i => i.BackupId == bkp.Id);
        foreach (var item in items) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                return;
            }

            await BackupFm.DeleteFile(GetBackupItemPath(BackupFm, BackupPath, item));
        }

        // Delete data
        await backupFile.DeleteBackup(bkp.Id);

        // Delete backup directory
        var bkpPath = BackupFm.CombinePath(BackupPath!, bkp.Guid);
        if (await BackupFm.DirectoryExists(bkpPath))
            await BackupFm.DeleteDirectory(bkpPath);
    }

    /// <summary>
    /// Validate the <see cref="DeleteJobSettings"/>
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    protected override void ValidateSettings()
    {
        if (string.IsNullOrEmpty(Settings.BackupPath))
            throw new ArgumentNullException(nameof(Settings.BackupPath), "Backup path cannot be empty");

        if (string.IsNullOrEmpty(Settings.BackupGuid))
            throw new ArgumentNullException(nameof(Settings.BackupGuid), "Backup GUID cannot be empty");
    }

    #endregion
}