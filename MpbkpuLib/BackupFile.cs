using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib;

/// <summary>
/// Backup file (database with information about a backup)
/// </summary>
public class BackupFile : IDisposable
{
    public const string DefaultDatabaseFileName = "mpbkpu.db";
    public const string DatabaseBackupFileName = "mpbkpu.db.bak";
    public const string DatabaseTempFileName = "mpbkpu.db.tmp";

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

        /// <summary>
        /// The path where the backup file is
        /// </summary>
        public string? BackupFilePath { get; private set; }

        /// <summary>
        /// The temporary file where has been copied to open it
        /// </summary>
        public string? TempFilePath { get; private set; }

        public void Dispose()
        {
            BackupFile.Dispose();
            if (!string.IsNullOrEmpty(TempFilePath)) {
                using var fm = FileManagerFactory.GetFileManager(TempFilePath, out _);
                fm.DeleteFile(TempFilePath).Wait();
            }
        }
    }

    public class Directory
    {
        public Directory()
        {
            Children = new List<Directory>();
        }

        public string? Name { get; set; }
        public string? Path { get; set; }
        public List<Directory> Children { get; set; }
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

    public DbSet<File> Files
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

    public DbSet<Destination> Destinations
    {
        get
        {
            EnsureDbOpen();
            return _context!.Destinations;
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
    }

    public static async Task<OpenResult> OpenBackupFile(IFileManager backupFm, string backupPath, string? password = null,
        bool create = false, Action<long, long>? progressAction = null, CancellationToken? token = null, bool copyToTemp = false)
    {
        if (!await backupFm.DirectoryExists(backupPath))
            throw new Exception($"Directory {backupPath} does not exists");

        var backupDbPath = backupFm.CombinePath(backupPath, DefaultDatabaseFileName);
        var backupDbBackupPath = backupFm.CombinePath(backupPath, DatabaseBackupFileName);
        var exists = await backupFm.FileExists(backupDbPath);
        if (!exists && await backupFm.FileExists(backupDbBackupPath)) {
            backupDbPath = backupDbBackupPath;
            exists = true;
        }

        string? tempDbPath = null;
        bool encrypted = false;
        if (exists) {
            var info = await backupFm.GetFileInfo(backupDbPath);
            progressAction?.Invoke(0, info.Size);
            // Check header
            await using (var sourceStream = await backupFm.OpenFile(backupDbPath)) {
                var header = new byte[EncryptDecrypt.HeaderSize];
                var read = await sourceStream.ReadAsync(header);
                encrypted = read == header.Length && EncryptDecrypt.IsEncryptedHeader(Encoding.ASCII.GetString(header));
                switch (encrypted) {
                    case true when string.IsNullOrEmpty(password):
                        throw new Exception("This backup is encrypted");
                    case false when !string.IsNullOrEmpty(password):
                        throw new Exception("This backup is not encrypted");
                }
            }

            if (copyToTemp || !string.IsNullOrEmpty(password) || !backupFm.CanOpenDatabase) {
                var action = new Action<long>((copied) =>
                {
                    progressAction?.Invoke(copied, info.Size);
                });
                // Copy database to temp file
                tempDbPath = TempFile.GetTempFileName();
                try {
                    using var tempFm = FileManagerFactory.GetFileManager(tempDbPath, out _);
                    await using var sourceStream = await backupFm.OpenFile(backupDbPath);
                    await using var tempStream = await tempFm.OpenFile(tempDbPath, true);
                    if (string.IsNullOrEmpty(password))
                        await Utility.Files.Copy(sourceStream, tempStream, action, token);
                    else {
                        var decrypt = new EncryptDecrypt(password);
                        await decrypt.Decrypt(sourceStream, tempStream, backupFm.SupportCompression, action, token);
                    }
                } catch (System.Security.Cryptography.CryptographicException) {
                    System.IO.File.Delete(tempDbPath!);
                    throw new InvalidPasswordException("Failed to open the database (wrong password or corrupted file)");
                } catch {
                    System.IO.File.Delete(tempDbPath!);
                    throw;
                }
            }
        } else {
            if (!create)
                throw new Exception($"Database file not found in {backupPath}");

            if (copyToTemp || !string.IsNullOrEmpty(password) || !backupFm.CanOpenDatabase)
                tempDbPath = TempFile.GetTempFileName();
        }

        var backupFile = new BackupFile();
        try {
            await backupFile.Open(string.IsNullOrEmpty(tempDbPath) ? backupDbPath : tempDbPath);
        } catch (SqliteException) {
            if (!string.IsNullOrEmpty(tempDbPath))
                System.IO.File.Delete(tempDbPath!);
            if (encrypted)
                throw new InvalidPasswordException("Failed to open the database (wrong password)");
            throw new Exception("Failed to open the database");
        }
        return new OpenResult(backupFile, backupDbPath, tempDbPath);
    }

    /// <summary>
    /// Write a local database file to a backup destination, keeping the previous database as backup copy
    /// </summary>
    /// <param name="localDbPath">The local database file (see <see cref="OpenResult.TempFilePath"/>)</param>
    /// <param name="backupFm">The <see cref="IFileManager"/> of the destination</param>
    /// <param name="backupPath">The destination directory</param>
    /// <param name="password">The encryption password, if any</param>
    /// <param name="progressAction">Progress (bytes, total bytes)</param>
    public static async Task SaveBackupFile(string localDbPath, IFileManager backupFm, string backupPath, string? password,
        Action<long, long>? progressAction = null)
    {
        using var sourceFm = FileManagerFactory.GetFileManager(localDbPath, out _);
        var info = await sourceFm.GetFileInfo(localDbPath);
        progressAction?.Invoke(0, info.Size);

        var dbPath = backupFm.CombinePath(backupPath, DefaultDatabaseFileName);
        var backupDbPath = backupFm.CombinePath(backupPath, DatabaseBackupFileName);
        var tempDbPath = backupFm.CombinePath(backupPath, DatabaseTempFileName);

        // Write the new database to a temporary file: the current database is untouched until the copy is complete
        if (await backupFm.FileExists(tempDbPath))
            await backupFm.DeleteFile(tempDbPath);
        await using (var sourceStream = await sourceFm.OpenFile(localDbPath)) {
            var destFileSize = Utility.Files.GetDestinationFileSize(sourceStream.Length, backupFm.SupportCompression ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression,
                !string.IsNullOrEmpty(password));
            await using var destStream = await backupFm.OpenFile(tempDbPath, true, destFileSize);

            var action = new Action<long>((copied) =>
            {
                progressAction?.Invoke(copied, info.Size);
            });
            if (string.IsNullOrEmpty(password))
                await Utility.Files.Copy(sourceStream, destStream, action);
            else {
                var encrypt = new EncryptDecrypt(password);
                await encrypt.Encrypt(sourceStream, destStream, backupFm.SupportCompression ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression, action);
            }
        }

        // Keep the current database as backup, then move the new one in place.
        // If interrupted between the two renames the database is missing and OpenBackupFile falls back to the backup copy.
        if (await backupFm.FileExists(dbPath)) {
            if (await backupFm.FileExists(backupDbPath))
                await backupFm.DeleteFile(backupDbPath);
            await backupFm.RenameFile(dbPath, BackupFile.DatabaseBackupFileName);
        }
        await backupFm.RenameFile(tempDbPath, DefaultDatabaseFileName);
    }

    public async Task Open(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));

        // Pooling=false is required to delete the file after close
        var sqliteBuilder = new DbContextOptionsBuilder().UseSqlite($"Data Source={path};Pooling=false");
        _context = new Context.BackupContext(sqliteBuilder.Options);
        await ApplyMigrations();
    }

    public void Close()
    {
        _context?.Dispose();
        _context = null;
    }

    private async Task ApplyMigrations()
    {
        if (_context == null)
            throw new Exception("The database il closed");

        if ((await _context.Database.GetPendingMigrationsAsync()).Any()) {
            _context.Database.SetCommandTimeout(1800000);
            await _context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await _context.Database.MigrateAsync();
            await _context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
            _context.Database.SetCommandTimeout(60);
        }
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

    public async Task<Backup?> LastBackup(Backup.BackupTypes? type = null)
    {
        EnsureDbOpen();
        return await _context!.Backups.AsNoTracking()
            .Where(b => type == null || b.Type == type)
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
        await _context!.Database.ExecuteSqlRawAsync("DELETE FROM Backups where id={0}", id);
        return true;
    }

    /// <summary>
    /// Vacuum the database
    /// </summary>
    /// <returns>True on success</returns>
    public async Task<bool> Vacuum()
    {
        EnsureDbOpen();
        await _context!.Database.ExecuteSqlRawAsync("VACUUM");
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
    /// <param name="fileType">Optional filter on type</param>
    /// <returns>A <see cref="IAsyncEnumerable"/> of <see cref="BackupItem"/></returns>
    public async IAsyncEnumerable<BackupItem> GetBackupItems(int backupId, File.FileTyes? fileType = null)
    {
        EnsureDbOpen();
        var bkp = await _context!.Backups.AsNoTracking()
            .Where(b => b.Id == backupId)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with id {backupId} not found");

        if (bkp.Type == Backup.BackupTypes.Full) {
            var items = _context!.BackupItems
                .Include(i => i.Backup)
                .Include(i => i.Volume)
                .AsNoTracking()
                .OrderBy(i => i.OriginalRelativePath)
                .ThenByDescending(i => i.Type)
                .Where(i => i.BackupId == backupId);
            if (fileType.HasValue)
                items = items.Where(i => i.Type == fileType);
            foreach (var item in items) {
                yield return item;
            }
        } else if (bkp.Type == Backup.BackupTypes.Incremental) {
            var fullBkp = await _context!.Backups.AsNoTracking()
                .Where(b => b.Type == Backup.BackupTypes.Full && b.Id < bkp.Id)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();
            if (fullBkp == null)
                throw new Exception($"Full backup not found");

            // Get ids of the subsequents incremental backups
            var incBackupIds = new List<int>();
            var sBackups = _context.Backups
                .AsNoTracking()
                .Where(b => b.Id > fullBkp.Id && b.Id <= backupId)
                .OrderBy(b => b.Id);
            foreach (var sb in sBackups) {
                if (sb.Type == Backup.BackupTypes.Full)
                    break;
                if (sb.Type == Backup.BackupTypes.Incremental)
                    incBackupIds.Add(sb.Id);
            }

            // Items from the previous full backup
            var items = _context!.BackupItems
                .Include(i => i.Backup)
                .Include(i => i.Volume)
                .AsNoTracking()
                .OrderBy(i => i.OriginalRelativePath)
                .ThenByDescending(i => i.Type)
                .Where(i => i.BackupId == fullBkp.Id);
            if (fileType.HasValue)
                items = items.Where(i => i.Type == fileType);
            // Paths already resolved to their last state: a path can be added more times (e.g. deleted and created again)
            var resolvedPaths = new HashSet<string?>();
            foreach (var item in items) {
                resolvedPaths.Add(item.OriginalRelativePath);
                // Read the same item from subsequents incremental backups
                var itemLastBackup = await _context!.BackupItems
                    .Include(i => i.Backup)
                    .Include(i => i.Volume)
                    .AsNoTracking()
                    .Where(i => incBackupIds.Contains(i.BackupId) &&
                                i.OriginalRelativePath == item.OriginalRelativePath)
                    .OrderBy(i => i.Id)
                    .LastOrDefaultAsync();
                if (itemLastBackup != null) {
                    if (itemLastBackup.Action != BackupItem.Actions.Deleted)
                        yield return itemLastBackup;
                } else {
                    yield return item;
                }
            }

            // Items added in the subsequents incremental backups
            var addedItems = _context.BackupItems
                .Include(i => i.Backup)
                .Include(i => i.Volume)
                .AsNoTracking()
                .OrderBy(i => i.BackupId)
                .ThenBy(i => i.OriginalRelativePath)
                .ThenByDescending(i => i.Type)
                .Where(i => incBackupIds.Contains(i.BackupId) && i.Action == BackupItem.Actions.Added);
            if (fileType.HasValue)
                addedItems = addedItems.Where(i => i.Type == fileType);
            foreach (var item in addedItems) {
                if (!resolvedPaths.Add(item.OriginalRelativePath))
                    continue;
                // Read the same item from subsequents incremental backups
                var itemLastBackup = await _context!.BackupItems
                    .Include(i => i.Backup)
                    .Include(i => i.Volume)
                    .AsNoTracking()
                    .Where(i => i.BackupId > item.BackupId && incBackupIds.Contains(i.BackupId) &&
                                i.OriginalRelativePath == item.OriginalRelativePath)
                    .OrderBy(i => i.Id)
                    .LastOrDefaultAsync();
                if (itemLastBackup != null) {
                    if (itemLastBackup.Action != BackupItem.Actions.Deleted)
                        yield return itemLastBackup;
                } else {
                    yield return item;
                }
            }
        } else if (bkp.Type == Backup.BackupTypes.Differential) {
            var fullBkp = await _context!.Backups.AsNoTracking()
                .Where(b => b.Type == Backup.BackupTypes.Full && b.Id < bkp.Id)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();
            if (fullBkp == null)
                throw new Exception($"Full backup not found");

            var items = _context!.BackupItems
                .Include(i => i.Backup)
                .Include(i => i.Volume)
                .AsNoTracking()
                .OrderBy(i => i.OriginalRelativePath)
                .ThenByDescending(i => i.Type)
                .Where(i => i.BackupId == fullBkp.Id);
            if (fileType.HasValue)
                items = items.Where(i => i.Type == fileType);
            foreach (var item in items) {
                // Read the same item from the differential backup
                var itemDifferentialBackup = await _context!.BackupItems
                    .Include(i => i.Backup)
                    .Include(i => i.Volume)
                    .AsNoTracking()
                    .Where(i => i.BackupId == backupId &&
                                i.OriginalRelativePath == item.OriginalRelativePath)
                    .FirstOrDefaultAsync();
                if (itemDifferentialBackup != null) {
                    if (itemDifferentialBackup.Action != BackupItem.Actions.Deleted)
                        yield return itemDifferentialBackup;
                } else {
                    yield return item;
                }
            }

            // Items added in the differential backup (not present in the full backup)
            var addedItems = _context.BackupItems
                .Include(i => i.Backup)
                .Include(i => i.Volume)
                .AsNoTracking()
                .OrderBy(i => i.OriginalRelativePath)
                .ThenByDescending(i => i.Type)
                .Where(i => i.BackupId == backupId && i.Action == BackupItem.Actions.Added &&
                            !_context.BackupItems.Any(f => f.BackupId == fullBkp.Id && f.OriginalRelativePath == i.OriginalRelativePath));
            if (fileType.HasValue)
                addedItems = addedItems.Where(i => i.Type == fileType);
            foreach (var item in addedItems) {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Get the directory tree of a backup: a single root node (the backed up folder, path "") with its subdirectories
    /// </summary>
    /// <param name="backupId">The backup id</param>
    /// <returns></returns>
    public async Task<List<Directory>> GetDirectoryTree(int backupId)
    {
        IFileManager? fm = null;
        Dictionary<string, Directory> directories = new();
        // Files directly in the backed up folder have "" as parent path: they are shown under the root node
        var root = new Directory() { Path = string.Empty };
        directories[root.Path] = root;
        await foreach (var item in GetBackupItems(backupId)) {
            root.Name ??= GetRootName(item);
            if (item.Type != File.FileTyes.Directory)
                continue;

            var dir = new Directory()
            {
                Name = item.OriginalName,
                Path = item.OriginalRelativePath
            };

            fm ??= FileManagerFactory.GetFileManager(item.OriginalPath, out _);
            var parentPath = MpbkpuLib.Utility.Files.GetParent(fm.PathSeparator, item.OriginalRelativePath!, out _, addStartingSeparator: false);
            (directories.TryGetValue(parentPath, out var parent) ? parent : root).Children.Add(dir);
            directories[dir.Path!] = dir;
        }

        root.Name ??= "/";
        return new List<Directory> { root };
    }

    /// <summary>
    /// Name of the backed up folder: the original path of an item without its relative path
    /// </summary>
    private static string? GetRootName(BackupItem item)
    {
        if (string.IsNullOrEmpty(item.OriginalPath) || string.IsNullOrEmpty(item.OriginalRelativePath) ||
            !item.OriginalPath.EndsWith(item.OriginalRelativePath, StringComparison.Ordinal))
            return null;
        var rootPath = item.OriginalPath[..^item.OriginalRelativePath.Length].TrimEnd('/', '\\');
        var idx = rootPath.LastIndexOfAny(new[] { '/', '\\' });
        var name = idx >= 0 ? rootPath[(idx + 1)..] : rootPath;
        return string.IsNullOrEmpty(name) ? rootPath : name;
    }

    #region private operations
    private void EnsureDbOpen()
    {
        if (_context == null)
            throw new Exception("The database il closed");
    }
    #endregion
}