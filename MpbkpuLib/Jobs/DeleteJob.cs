using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

public class DeleteJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        DeleteBackups,
        CopyDatabase
    }

    #region classes
    private class DeletedBackup
    {
        public DeletedBackup(string guid)
        {
            Guid = guid;
        }

        public string Guid { get; }
        public List<(string Path, long Size)> Files { get; } = new();
    }
    #endregion

    #region private members
    private CancellationToken? _cancellationToken;
    private bool _skipDatabaseCopy;
    private readonly List<DeletedBackup> _deletedBackups = new();
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
        LogInfo(SerializeSettings());


        LogInfo("Opening database");
        BackupFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out BackupPath, Settings.BackupPathUser, Settings.BackupPathPassword, Settings.BackupOauthSettings);
        Step?.Invoke(this, Steps.OpenDatabase);
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword, progressAction: OnOpenBackup, token: _cancellationToken)) {
            var backupFile = openRes.BackupFile;
            try {
                await backupFile.EmptyFiles();
                await using (var transaction = backupFile.BeginTransaction()) {
                    try {
                        await RunPrimitive(backupFile);
                        await transaction.CommitAsync();
                    } catch {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }

                await backupFile.Vacuum();
                backupFile.Close();

                // Copy temporary database file to destination
                if (!_skipDatabaseCopy && !string.IsNullOrEmpty(openRes.TempFilePath)) {
                    LogInfo("Copying database");
                    Step?.Invoke(this, Steps.CopyDatabase);
                    await CopyDatabaseToDestination(openRes, Settings.EncryptPassword, OnCopyBackup);
                }

                // Delete the files only now that the database no longer references them:
                // a failure from here on leaves orphan files, never backups with missing files
                foreach (var deleted in _deletedBackups) {
                    await DeleteBackupFiles(deleted);
                }
            } catch (Exception ex) {
                LogError(ex);
                throw;
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

    /// <summary>
    /// Delete a backup from the database, saving the list of its files to delete them after the commit
    /// </summary>
    private async Task DeleteBackup(BackupFile backupFile, int backupId)
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Id == backupId)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with Id { backupId } not found");

        var deleted = new DeletedBackup(bkp.Guid!);
        var items = backupFile.BackupItems
            .Include(i => i.Volume)
            .Include(i => i.Backup)
            .AsNoTracking()
            .Where(i => i.BackupId == backupId);
        foreach (var item in items) {
            deleted.Files.Add((GetBackupItemPath(BackupFm, BackupPath, item), item.Size));
        }

        await backupFile.DeleteBackup(backupId);
        _deletedBackups.Add(deleted);
    }

    /// <summary>
    /// Delete the files of a backup already removed from the database
    /// </summary>
    private async Task DeleteBackupFiles(DeletedBackup deleted)
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        Delete?.Invoke(this, deleted.Guid);
        var bkpPath = BackupFm.CombinePath(BackupPath!, deleted.Guid);
        if (!await BackupFm.DirectoryExists(bkpPath))
            return;

        var eta = StartEta(0, deleted.Files.Count, BackupFm);
        var totalSize = deleted.Files.Sum(f => f.Size);
        var fileIdx = 0;
        long bytes = 0;
        foreach (var (filePath, size) in deleted.Files) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogWarning($"Cancelled by user: the remaining files in {bkpPath} have not been deleted");
                return;
            }

            if (!await BackupFm.FileExists(filePath))
                LogWarning($"File {filePath} not found");
            else
                await BackupFm.DeleteFile(filePath);
            fileIdx++;
            bytes += size;
            Progress?.Invoke(this,
                new ProgressHandlerArgs(fileIdx, deleted.Files.Count, bytes, totalSize, eta.Update(0, fileIdx)));
        }
        // Delete backup directory
        await DeleteDirectoryRecursive(BackupFm, bkpPath);
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