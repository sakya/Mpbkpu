using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Sqlite.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using MpbkpuLib.DataModel;

namespace MpbkpuLib;

/// <summary>
/// Backup file (database with information about a backup)
/// </summary>
public class BackupFile : IDisposable
{
    public const string DefaultDatabaseFileName = "_backup.db";

    private Context.BackupContext? _context;
    public BackupFile()
    {
        
    }

    public DbSet<DataModel.Backup> Backups
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

    public DbSet<DataModel.BackupItem> BackupItems
    {
        get
        {
            EnsureDbOpen();
            return _context!.BackupItems;
        }
    }
    
    public DbSet<DataModel.BackupVolume> BackupVolumes
    {
        get
        {
            EnsureDbOpen();
            return _context!.BackupVolumes;
        }
    }
    
    public DbSet<DataModel.BackupWarning> BackupWarnings
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
    
    public async Task<bool> Open(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));
        
        var sqliteBuilder = new DbContextOptionsBuilder().UseSqlite($"Data Source={path}");
        _context = new Context.BackupContext(sqliteBuilder.Options);
        await _context!.Database.EnsureCreatedAsync();
        
        return true;
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
    
    public async Task<DataModel.Backup?> LastBackup()
    {
        EnsureDbOpen();
        return await _context!.Backups!.AsNoTracking()
            .OrderByDescending(b => b.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> EmptyFiles()
    {
        EnsureDbOpen();
        await _context!.Database.ExecuteSqlRawAsync("DELETE FROM Files");
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
    public async IAsyncEnumerable<DataModel.BackupItem> GetBackupItems(int backupId)
    {
        EnsureDbOpen();
        var bkp = await _context!.Backups!.AsNoTracking()
            .Where(b => b.Id == backupId)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with id {backupId} not found");

        if (bkp.Type == BackupJobSettings.BackupTypes.Full) {
            var items = _context!.BackupItems!.AsNoTracking()
                .Where(i => i.BackupId == backupId);
            foreach (var item in items) {
                yield return item;
            }
        } else if (bkp.Type == BackupJobSettings.BackupTypes.Incremental) {
            var fullBkp = await _context!.Backups!.AsNoTracking()
                .Where(b => b.Type == BackupJobSettings.BackupTypes.Full && b.Id < bkp.Id)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();
            if (fullBkp == null)
                throw new Exception($"Full backup not found");

            var items = _context!.BackupItems!.AsNoTracking()
                .Where(i => i.BackupId == fullBkp.Id);
            foreach (var item in items) {
                // Read the same items from subsequent backups
                var itemLastBackup = await _context!.BackupItems!.AsNoTracking()
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
            var fullBkp = await _context!.Backups!.AsNoTracking()
                .Where(b => b.Type == BackupJobSettings.BackupTypes.Full && b.Id < bkp.Id)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();
            if (fullBkp == null)
                throw new Exception($"Full backup not found");
                
            var items = _context!.BackupItems!.AsNoTracking()
                .Where(i => i.BackupId == fullBkp.Id);
            foreach (var item in items) {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Verify the database and checks backup files
    /// </summary>
    /// <remarks>This method can be very slow for backups with a lot of files</remarks>
    /// <returns>True on success</returns>
    public async Task<bool> Verify()
    {
        EnsureDbOpen();
        
        // Verify backup folders
        var backups = _context!.Backups!.AsNoTracking()
            .OrderBy(b => b.Id);
        foreach (var backup in backups) {
            // Verify backup items
            var items = _context!.BackupItems!.AsNoTracking()
                .Where(i => i.BackupId == backup.Id)
                .OrderBy(i => i.Id);
            foreach (var item in items) {
                
            }
        }
        return true;
    }

    #region private operations

    private void EnsureDbOpen()
    {
        if (_context == null)
            throw new Exception("The database il closed");        
    }
    #endregion
}