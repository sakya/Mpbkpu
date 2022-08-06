using Microsoft.EntityFrameworkCore;
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
    public override async Task<bool> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        Step?.Invoke(this, Steps.OpenDatabase);
        using (var openRes = await BackupFile.OpenBackupFile(Settings.BackupPath!, Settings.EncryptPassword)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            var transaction = backupFile.BeginTransaction();

            try {
                await RunPrimitive(backupFile);
                await transaction.CommitAsync();

                backupFile.Close();
                // Copy temporary database file to destination
                if (!string.IsNullOrEmpty(openRes.TempFilePath)) {
                    Step?.Invoke(this, Steps.CopyDatabase);
                    await CopyDatabaseToDestination(openRes, Settings.BackupPath, Settings.EncryptPassword);
                }
            } catch (Exception ex) {
                await transaction.RollbackAsync();
                LogError(ex);
                throw;
            }
        }

        return true;
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

        using var fm = FileManagerFactory.GetFileManager(Settings.BackupPath, out var newPath);
        var items = backupFile.BackupItems.AsNoTracking()
            .Include(i => i.Volume)
            .Where(i => i.BackupId == bkp.Id);
        foreach (var item in items) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                return;
            }

            var filePath = fm.CombinePath(newPath, bkp.Guid, item.Volume!.Name!, item.Name!);
            await fm.DeleteFile(filePath);
        }

        // Delete data
        await backupFile.DeleteBackup(bkp.Id);

        // Delete backup folder
        var bkpPath = fm.CombinePath(newPath, bkp.Guid);
        if (await fm.DirectoryExists(bkpPath))
            await fm.DeleteDirectory(bkpPath);
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