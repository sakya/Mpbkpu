using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using Newtonsoft.Json;

namespace MpbkpuLib.Jobs;

public class DeleteJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        DeleteBackups,
        CopyDatabase
    }

    #region private members
    private CancellationToken? _cancellationToken;
    private bool _skipDatabaseCopy;
    #endregion

    #region delegates
    public class ConfirmArgs : EventArgs
    {
        public class BackupInfo
        {
            public string? Guid { get; set; }
            public Backup.BackupTypes Type { get; set; }
            public Backup.Results Result { get; set; }
            public int Files { get; set; }
            public long Bytes { get; set; }
        }

        public ConfirmArgs()
        {
            Backups = new List<BackupInfo>();
        }
        public List<BackupInfo> Backups { get; set; }

        public int FilesToDelete
        {
            get { return Backups.Sum(b => b.Files); }
        }

        public long BytesToDelete
        {
            get { return Backups.Sum(b => b.Bytes); }
        }
    }

    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmDelete;

    public delegate void StepHandler(object sender, Steps step);
    public event StepHandler? Step;

    public delegate void DeleteHandler(object sender, string guid);

    public event DeleteHandler? Delete;

    public delegate void ProgressHandler(object sender, ProgressHandlerArgs args);

    public event ProgressHandler? Progress;
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

        var started = DateTime.UtcNow;
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
                if (!_skipDatabaseCopy && !string.IsNullOrEmpty(openRes.TempFilePath)) {
                    Step?.Invoke(this, Steps.CopyDatabase);
                    await CopyDatabaseToDestination(openRes, Settings.EncryptPassword);
                }
            } catch (Exception ex) {
                LogError(ex);
                await transaction.RollbackAsync();
                throw;
            } finally {
                transaction.Dispose();
            }
        }

        return new DeleteJobResult()
        {
            Success = true,
            Started = started,
            Completed = DateTime.UtcNow
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

        var bkp = await backupFile.Backups
            .Include(b => b.Items)
            .AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with GUID { Settings.BackupGuid } not found");

        var cArgs = new ConfirmArgs();
        cArgs.Backups.Add(new ConfirmArgs.BackupInfo()
        {
            Guid = bkp.Guid,
            Type = bkp.Type,
            Result = bkp.Result,
            Files = bkp.Items.Count(),
            Bytes = bkp.Items.Sum(i => i.Size)
        });
        List<int> backupIds = new() { bkp.Id };
        if (bkp.Type is Backup.BackupTypes.Full or Backup.BackupTypes.Incremental) {
            // We must delete all subsequent backups until the next Full backup
            var backups = backupFile.Backups
                .Include(b => b.Items)
                .AsNoTracking()
                .Where(b => b.Id > bkp.Id)
                .OrderBy(b => b.Id);
            foreach (var sBkp in backups) {
                if (sBkp.Type == Backup.BackupTypes.Full)
                    break;

                cArgs.Backups.Add(new ConfirmArgs.BackupInfo()
                {
                    Guid = sBkp.Guid,
                    Type = sBkp.Type,
                    Result = sBkp.Result,
                    Files = sBkp.Items.Count(),
                    Bytes = sBkp.Items.Sum(i => i.Size)
                });
                backupIds.Add(sBkp.Id);
            }
        }

        if (ConfirmDelete != null) {
            if (!await ConfirmDelete.Invoke(this, cArgs)) {
                LogInfo("Cancelled by user");
                _skipDatabaseCopy = true;
                return;
            }
        }

        Step?.Invoke(this, Steps.DeleteBackups);
        backupIds.Reverse();
        foreach (var bkpId in backupIds) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                return;
            }
            await DeleteBackup(backupFile, bkpId);
        }
    }

    private async Task DeleteBackup(BackupFile backupFile, int backupId)
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Id == backupId)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with Id { backupId } not found");

        Delete?.Invoke(this, bkp.Guid);
        var bkpPath = BackupFm.CombinePath(BackupPath!, bkp.Guid);
        if (await BackupFm.DirectoryExists(bkpPath)) {
            // Delete files
            var items = backupFile.BackupItems
                .Include(i => i.Volume)
                .Include(i => i.Backup)
                .AsNoTracking()
                .Where(i => i.BackupId == backupId);

            var started = DateTime.UtcNow;
            var totalFiles = await items.CountAsync();
            var totalSize = await items.Select(i => i.Size)
                .SumAsync();
            var fileIdx = 0;
            long bytes = 0;
            foreach (var item in items) {
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    return;
                }

                var filePath = GetBackupItemPath(BackupFm, BackupPath, item);
                if (!await BackupFm.FileExists(filePath))
                    LogWarning($"File {filePath} not found");
                else
                    await BackupFm.DeleteFile(GetBackupItemPath(BackupFm, BackupPath, item));
                fileIdx++;
                bytes += item.Size;
                Progress?.Invoke(this,
                    new ProgressHandlerArgs(fileIdx, totalFiles, bytes, totalSize, GetEta(started, bytes, totalSize)));
            }
            // Delete backup directory
            await BackupFm.DeleteDirectory(bkpPath);
        }
        // Delete data
        await backupFile.DeleteBackup(backupId);
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