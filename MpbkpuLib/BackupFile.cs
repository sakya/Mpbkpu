using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    public DbSet<DataModel.Backup>? Backups
    {
        get { return _context?.Backups; }
    }

    public DbSet<DataModel.File>? Files
    {
        get { return _context?.Files; }
    }

    public DbSet<DataModel.BackupItem>? BackupItems
    {
        get { return _context?.BackupItems; }
    }
    
    public DbSet<DataModel.BackupVolume>? BackupVolumes
    {
        get { return _context?.BackupVolumes; }
    }
    
    public DbSet<DataModel.BackupWarning>? BackupWarnings
    {
        get { return _context?.BackupWarnings; }
    }
    
    public void Dispose()
    {
        _context?.Dispose();
    }
    
    public async Task<bool> Open(string? path)
    {
        if (string.IsNullOrEmpty((path)))
            throw new ArgumentNullException(nameof(path));
        
        var builder = new DbContextOptionsBuilder();
        var sqliteBuilder = builder.UseSqlite($"Data Source={path}");
        _context = new Context.BackupContext(builder.Options);
        await _context.Database.EnsureCreatedAsync();
        
        return true;
    }

    public IDbContextTransaction BeginTransaction()
    {
        EnsureDbOpen();
        return _context.Database.BeginTransaction();
    }
    
    public void Detach(object entity)
    {
        EnsureDbOpen();
        _context.Entry(entity).State = EntityState.Detached;
    }
    
    public async Task<DataModel.Backup?> LastBackup(BackupJobSettings.BackupTypes? type = null)
    {
        EnsureDbOpen();
        var filter = _context.Backups.AsNoTracking();
        if (type.HasValue)
            filter = filter.Where(b => b.Type == type.Value);
        return await filter
            .OrderByDescending(b => b.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> EmptyFiles()
    {
        EnsureDbOpen();
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Files");
        return true;
    }
    
    public async Task<int> SaveChangesAsync()
    {
        EnsureDbOpen();
        return await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Get the backup items for a backup.
    /// </summary>
    /// <returns></returns>
    public async IAsyncEnumerable<DataModel.BackupItem> GetBackupItems(int backupId)
    {
        var bkp = await _context.Backups.AsNoTracking()
            .Where(b => b.Id == backupId)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with id {backupId} not found");

        if (bkp.Type == BackupJobSettings.BackupTypes.Full) {
            var items = _context.BackupItems.AsNoTracking()
                .Where(i => i.BackupId == backupId);
            foreach (var item in items) {
                yield return item;
            }
        } else if (bkp.Type == BackupJobSettings.BackupTypes.Incremental) {
            // TODO: read the previous full backup and all subsequent Incremental
            var fullBkp = await _context.Backups.AsNoTracking()
                .Where(b => b.Type == BackupJobSettings.BackupTypes.Full && b.Id < bkp.Id)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();
            if (fullBkp == null)
                throw new Exception($"Full backup not found");

            var items = _context.BackupItems.AsNoTracking()
                .Where(i => i.BackupId == fullBkp.Id);
            foreach (var item in items) {
                // Read the same items from subsequent backups
                var nextBackups = _context.BackupItems.AsNoTracking()
                    .Where(i => i.Backup.Type == BackupJobSettings.BackupTypes.Incremental && i.BackupId <= backupId &&
                                i.BackupId >= fullBkp.Id && i.OriginalPath == item.OriginalPath)
                    .OrderBy(i => i.Id);
                foreach (var nextItem in nextBackups) {
                    
                }
            }            
        } else if (bkp.Type == BackupJobSettings.BackupTypes.Differential) {
            var fullBkp = await _context.Backups.AsNoTracking()
                .Where(b => b.Type == BackupJobSettings.BackupTypes.Full && b.Id < bkp.Id)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();
            if (fullBkp == null)
                throw new Exception($"Full backup not found");
                
            var items = _context.BackupItems.AsNoTracking()
                .Where(i => i.BackupId == fullBkp.Id);
            foreach (var item in items) {
                yield return item;
            }
        }
    }
    #region private operations

    private void EnsureDbOpen()
    {
        if (_context == null)
            throw new Exception("The database il closed");        
    }
    #endregion
}