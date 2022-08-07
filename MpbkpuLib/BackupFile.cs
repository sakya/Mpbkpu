using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;
using MpbkpuLib.Utility;

namespace MpbkpuLib;

/// <summary>
/// Backup file (database with information about a backup)
/// </summary>
public class BackupFile : IDisposable
{
    public const string DefaultDatabaseFileName = "mpbkpu.db";

    #region classes
    public class OpenResult : IDisposable
    {
        public OpenResult(BackupFile backup, string? backupFilePath, string? tempFilePath = null)
        {
            BackupFile = backup;
            BackupFilePath = backupFilePath;
            TempFilePath = tempFilePath;
        }

        public BackupFile BackupFile { get; private set; }
        public string? BackupFilePath { get; private set; }
        public string? TempFilePath { get; private set; }
        public void Dispose()
        {
            BackupFile.Dispose();
            if (!string.IsNullOrEmpty(TempFilePath)) {
                var fm = new FileSystem();
                fm.DeleteFile(TempFilePath).Wait();
            }
        }
    }
    #endregion

    private Context.BackupContext? _context;

    public DbSet<Backup> Backups
    {
        get
        {
            EnsureDbOpen();
            return _context!.Backups;
        }
    }

    public DbSet<DataModel.File> Files
    {
        get
        {
            EnsureDbOpen();
            return _context!.Files;
        }
    }

    public DbSet<BackupItem> BackupItems
    {
        get
        {
            EnsureDbOpen();
            return _context!.BackupItems;
        }
    }

    public DbSet<BackupVolume> BackupVolumes
    {
        get
        {
            EnsureDbOpen();
            return _context!.BackupVolumes;
        }
    }

    public DbSet<BackupWarning> BackupWarnings
    {
        get
        {
            EnsureDbOpen();
            return _context!.BackupWarnings;
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
    }

    public static async Task<OpenResult> OpenBackupFile(string backupPath, string? password = null, bool create = false)
    {
        using var sourceFm = FileManagerFactory.GetFileManager(backupPath, out var newPath);
        if (!await sourceFm.DirectoryExists(newPath))
            throw new Exception($"Directory {newPath} does not exists");

        var backupDbPath = sourceFm.CombinePath(newPath, DefaultDatabaseFileName);
        string? tempDbPath = null;
        if (await sourceFm.FileExists(backupDbPath)) {
            if (!string.IsNullOrEmpty(password) || !sourceFm.CanOpenDatabase()) {
                // Copy database to temp file
                tempDbPath = TempFile.GetTempFileName();
                using var tempFm = FileManagerFactory.GetFileManager(tempDbPath, out _);
                await using var sourceStream = await sourceFm.OpenFile(backupDbPath);
                await using var tempStream = await tempFm.OpenFile(tempDbPath, true);
                if (string.IsNullOrEmpty(password))
                    await Utility.Files.Copy(sourceStream, tempStream);
                else {
                    var decrypt = new EncryptDecrypt(password);
                    await decrypt.Decrypt(sourceStream, tempStream, true);
                }
            }
        } else {
            if (!create)
                throw new Exception($"Database file not found in {backupPath}");

            if (!string.IsNullOrEmpty(password) || !sourceFm.CanOpenDatabase())
                tempDbPath = TempFile.GetTempFileName();
        }

        var backupFile = new BackupFile();
        try {
            await backupFile.Open(string.IsNullOrEmpty(tempDbPath) ? backupDbPath : tempDbPath);
        } catch (SqliteException) {
            if (!string.IsNullOrEmpty(tempDbPath))
                System.IO.File.Delete(tempDbPath);
            throw new Exception("Failed to open the database (wrong password?)");
        }
        return new OpenResult(backupFile, backupDbPath, tempDbPath);
    }

    private async Task Open(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));

        // Pooling=false is required to delete the file after close
        var sqliteBuilder = new DbContextOptionsBuilder().UseSqlite($"Data Source={path};Pooling=false");
        _context = new Context.BackupContext(sqliteBuilder.Options);
        await _context!.Database.EnsureCreatedAsync();
    }

    public void Close()
    {
        _context?.Dispose();
        _context = null;
    }

    public IDbContextTransaction BeginTransaction()
    {
        EnsureDbOpen();
        return _context!.Database.BeginTransaction();
    }

    public void Detach(object entity)
    {
        EnsureDbOpen();
        _context!.Entry(entity).State = EntityState.Detached;
    }

    public async Task<Backup?> LastBackup()
    {
        EnsureDbOpen();
        return await _context!.Backups.AsNoTracking()
            .OrderByDescending(b => b.Id)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Delete all data from the Files table
    /// </summary>
    /// <returns>True on success</returns>
    public async Task<bool> EmptyFiles()
    {
        EnsureDbOpen();
        await _context!.Database.ExecuteSqlRawAsync("DELETE FROM Files");
        return true;
    }

    /// <summary>
    /// Delete all data of a backup
    /// </summary>
    /// <param name="id">The backup id</param>
    /// <returns>True on success</returns>
    public async Task<bool> DeleteBackup(int id)
    {
        EnsureDbOpen();
        await _context!.Database.ExecuteSqlRawAsync($"DELETE FROM Backups where id={id}");
        return true;
    }

    public async Task<int> SaveChangesAsync()
    {
        EnsureDbOpen();
        return await _context!.SaveChangesAsync();
    }

    /// <summary>
    /// Get the backup items for a backup.
    /// </summary>
    /// <param name="backupId">The backup id</param>
    /// <returns>A <see cref="IAsyncEnumerable"/> of <see cref="BackupItem"/></returns>
    public async IAsyncEnumerable<BackupItem> GetBackupItems(int backupId)
    {
        EnsureDbOpen();
        var bkp = await _context!.Backups.AsNoTracking()
            .Where(b => b.Id == backupId)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with id {backupId} not found");

        if (bkp.Type == BackupJobSettings.BackupTypes.Full) {
            var items = _context!.BackupItems.AsNoTracking()
                .Where(i => i.BackupId == backupId);
            foreach (var item in items) {
                yield return item;
            }
        } else if (bkp.Type == BackupJobSettings.BackupTypes.Incremental) {
            var fullBkp = await _context!.Backups.AsNoTracking()
                .Where(b => b.Type == BackupJobSettings.BackupTypes.Full && b.Id < bkp.Id)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();
            if (fullBkp == null)
                throw new Exception($"Full backup not found");

            var items = _context!.BackupItems.AsNoTracking()
                .Where(i => i.BackupId == fullBkp.Id);
            foreach (var item in items) {
                // Read the same items from subsequent backups
                var itemLastBackup = await _context!.BackupItems.AsNoTracking()
                    .Where(i => i.Backup!.Type == BackupJobSettings.BackupTypes.Incremental && i.BackupId <= backupId &&
                                i.BackupId >= fullBkp.Id && i.OriginalPath == item.OriginalPath)
                    .OrderBy(i => i.Id)
                    .LastOrDefaultAsync();
                if (itemLastBackup != null) {
                    if (itemLastBackup.Action != BackupItem.Actions.Deleted)
                        yield return itemLastBackup;
                } else {
                    yield return item;
                }
            }
        } else if (bkp.Type == BackupJobSettings.BackupTypes.Differential) {
            var fullBkp = await _context!.Backups.AsNoTracking()
                .Where(b => b.Type == BackupJobSettings.BackupTypes.Full && b.Id < bkp.Id)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();
            if (fullBkp == null)
                throw new Exception($"Full backup not found");

            var items = _context!.BackupItems.AsNoTracking()
                .Where(i => i.BackupId == fullBkp.Id);
            foreach (var item in items) {
                yield return item;
            }
        } }

    #region private operations
    private void EnsureDbOpen()
    {
        if (_context == null)
            throw new Exception("The database il closed");
    }
    #endregion
}