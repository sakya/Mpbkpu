using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

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
    
    public async Task<DataModel.Backup?> LastBackup()
    {
        EnsureDbOpen();
        return await _context.Backups.AsNoTracking()
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
    
    #region private operations

    private void EnsureDbOpen()
    {
        if (_context == null)
            throw new Exception("The database il closed");        
    }
    #endregion
}