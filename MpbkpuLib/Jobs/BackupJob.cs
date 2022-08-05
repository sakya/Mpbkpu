using System.IO.Compression;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.Jobs;
/// <summary>
/// A job to backup files
/// </summary>
public class BackupJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        ReadSource,
        CalculateFilesToBackup,
        BackupFiles,
        CopyDatabase
    }

    #region classes

    public class LastUsedVolume
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
        public int? ItemsCount { get; set; }
    }
    #endregion
    #region private members
    private Backup? _backup;
    private LastUsedVolume? _lastUsedVolume;
    private CancellationToken? _cancellationToken;
    #endregion

    #region delegates
    public class ConfirmArgs : EventArgs
    {
        public int FilesToBackup { get; set; }
        public long BytesToBackup { get; set; }
    }
    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmBackup;

    public delegate void StepHandler(object sender, Steps step);

    public event StepHandler? Step;

    public delegate void ProgressHandler(object sender, int file, int totalFiles, long bytes, long totalBytes);

    public event ProgressHandler? Progress;

    public delegate void FileProgressHandler(object sender, string filePath, long bytes, long totalBytes);

    public event FileProgressHandler? FileProgress;
    #endregion

    public BackupJob(BackupJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new BackupJobSettings Settings
    {
        get { return (base.Settings as BackupJobSettings)!; }
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
        await CheckDestination();
        using (var openRes = await BackupFile.OpenBackupFile(Settings.Destination!, Settings.EncryptPassword, true)) {
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
                    await CopyDatabaseToDestination(openRes, Settings.Destination, Settings.EncryptPassword);
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
    private async Task<bool> RunPrimitive(BackupFile backupFile)
    {
        _lastUsedVolume = null;
        Backup? lastBackup = await backupFile.LastBackup();
        // If there is no previous backup this is a full backup
        if (lastBackup == null && Settings.BackupType != BackupJobSettings.BackupTypes.Full) {
            LogWarning("No previous backup found, switching to Full backup");
            Settings.BackupType = BackupJobSettings.BackupTypes.Full;
        } else if (lastBackup != null && (lastBackup.Type == BackupJobSettings.BackupTypes.Differential || lastBackup.Type == BackupJobSettings.BackupTypes.Incremental) && lastBackup.Type != Settings.BackupType) {
            // We cannot change backup type
            throw new Exception($"Cannot change backup type. Backup to a different destination");
        }

        if (lastBackup != null) {
            var lastBackupSettings = JsonConvert.DeserializeObject<BackupJobSettings>(lastBackup.Settings!);
            if (lastBackupSettings?.Source != Settings.Source) {
                LogWarning($"Changed backup source from {lastBackupSettings!.Source} to {Settings.Source}");
            }
        }

        _backup = new Backup()
        {
            Name = Settings.Name,
            Type = Settings.BackupType,
            Settings = JsonConvert.SerializeObject(Settings, Formatting.Indented),
            StartedAt = DateTime.UtcNow,
        };

        backupFile.Backups.Add(_backup);
        await backupFile.SaveChangesAsync();
        backupFile.Detach(_backup);

        // Read source
        Step?.Invoke(this, Steps.ReadSource);
        LogInfo($"Reading source {Settings.Source}");
        if (!ReadPathContent(backupFile, Settings.Source, Settings.Source).Result) {
            LogError("Failed to read source");
            if (!await SetBackupResult(backupFile, Backup.Results.Error))
                LogWarning("Cannot set backup result");
            return false;
        }
        if (_cancellationToken?.IsCancellationRequested == true) {
            LogInfo("Cancelled by user");
            if (!await SetBackupResult(backupFile, Backup.Results.Cancelled))
                LogWarning("Cannot set backup result");
            return false;
        }
        await backupFile.SaveChangesAsync();

        // Compute files to backup (only if not a full backup)
        // Read last backup items
        LogInfo("Checking files to backup");
        Step?.Invoke(this, Steps.CalculateFilesToBackup);
        if (lastBackup != null && _backup.Type != BackupJobSettings.BackupTypes.Full) {
            await foreach (var item in backupFile.GetBackupItems(lastBackup.Id)) {
                if (item.Action == BackupItem.Actions.Deleted)
                    continue;

                // Check if the source file has changed
                var sourceFile = await backupFile.Files.Where(f => f.FullPath == item.OriginalPath)
                    .FirstOrDefaultAsync();
                if (sourceFile != null) {
                    if (sourceFile.Size != item.OriginalSize || sourceFile.LastModified != item.OriginalLastModified) {
                        sourceFile.FileAction = File.FileActions.Backup;
                        sourceFile.Modified = true;
                    } else {
                        sourceFile.FileAction = File.FileActions.None;
                    }
                } else {
                    backupFile.Files.Add(new File()
                    {
                        FileAction = File.FileActions.Delete,
                        Created = item.OriginalCreated,
                        Extension = item.OriginalExtension,
                        FullPath = item.OriginalPath,
                        Hidden = item.OriginalHidden,
                        Name = item.OriginalName,
                        Size = item.OriginalSize,
                        Type = item.Type
                    });
                    await backupFile.SaveChangesAsync();
                }
            }

            await backupFile.SaveChangesAsync();
        }

        var cArgs = new ConfirmArgs()
        {
            FilesToBackup = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Backup)
                .CountAsync(),
            BytesToBackup = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Backup)
                .Select(f => f.Size)
                .SumAsync(),
        };
        if (ConfirmBackup != null) {
            if (!await ConfirmBackup.Invoke(this, cArgs)) {
                LogInfo("Cancelled by user");
                if (!await SetBackupResult(backupFile, Backup.Results.Cancelled))
                    LogWarning("Cannot set backup result");
                return false;
            }
        }

        // Create the backup folder
        using var destFm = FileManagerFactory.GetFileManager(Settings.Destination, out string destPath);
        var backupDirectory = destFm.CombinePath(destPath, _backup.Guid);
        if (await destFm.DirectoryExists(backupDirectory))
            throw new Exception($"The backup destination {backupDirectory} exist");
        await destFm.CreateDirectory(backupDirectory);

        // Backup files
        Step?.Invoke(this, Steps.BackupFiles);
        Utility.EncryptDecrypt? encryptor = null;
        if (!string.IsNullOrEmpty(Settings.EncryptPassword)) {
            encryptor = new Utility.EncryptDecrypt(Settings.EncryptPassword);
        }

        int fileIdx = 0;
        long bytes = 0;
        var filesToBackup = backupFile.Files.AsNoTracking()
                            .Where(f => f.FileAction == File.FileActions.Backup || f.FileAction == File.FileActions.Delete);
        foreach (var fileToBackup in filesToBackup) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                if (!await SetBackupResult(backupFile, Backup.Results.Cancelled))
                    LogWarning("Cannot set backup result");
                return false;
            }

            if (fileToBackup.FileAction == File.FileActions.Backup) {
                if (await BackupFileFromSource(backupFile, fileToBackup, encryptor)) {
                    bytes += fileToBackup.Size;
                    Progress?.Invoke(this, ++fileIdx, cArgs.FilesToBackup, bytes, cArgs.BytesToBackup);
                } else {
                    LogError($"Failed to backup {fileToBackup.FullPath}");
                }
            } else {
                await DeleteItem(backupFile, fileToBackup);
            }
        }
        if (!await SetBackupResult(backupFile, Backup.Results.Completed))
            LogWarning("Cannot set backup result");
        await backupFile.SaveChangesAsync();
        await backupFile.EmptyFiles();
        LogInfo("Completed");
        return true;
    }

    /// <summary>
    /// Validate the <see cref="BackupJobSettings"/>
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    protected  override void ValidateSettings()
    {
        if (string.IsNullOrEmpty(Settings.Source))
            throw new ArgumentNullException(nameof(Settings.Source), "Source cannot be empty");

        if (string.IsNullOrEmpty(Settings.Destination))
            throw new ArgumentNullException(nameof(Settings.Destination), "Destination cannot be empty");

        if (Settings.Source == Settings.Destination)
            throw new ArgumentException("Source and Destination cannot be the same");

        if (Settings.Destination.StartsWith(Settings.Source))
            throw new ArgumentException("Destination cannot be a subdirectory of Source");

        if (Settings.MaxFilesPerDirectory <= 0)
            throw new ArgumentException("MaxFilesPerDirectory must be greater than zero");
    }

    /// <summary>
    /// Check the destination directory.
    /// Search for an existing backup.
    /// </summary>
    /// <returns>The path of the database file to use for this backup</returns>
    private async Task<bool> CheckDestination()
    {
        using var fm = FileManagerFactory.GetFileManager(Settings.Destination, out _);
        if (!await fm.DirectoryExists(Settings.Destination))
            await fm.CreateDirectory(Settings.Destination);

        return true;
    }

    /// <summary>
    /// Backup a <see cref="DataModel.File"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/></param>
    /// <param name="sourceFile">The <see cref="DataModel.File"/> to backup</param>
    /// <param name="encryptor">The optional <see cref="Utility.EncryptDecrypt"/> to encrypt the file</param>
    /// <returns>True on success</returns>
    /// <exception cref="ArgumentNullException"></exception>
    private async Task<bool> BackupFileFromSource(BackupFile? backupFile, File? sourceFile, Utility.EncryptDecrypt? encryptor)
    {
        if (backupFile == null)
            throw new ArgumentNullException(nameof(backupFile));
        if (sourceFile == null)
            throw new ArgumentNullException(nameof(sourceFile));

        var backupItem = await GetNextBackupItem(backupFile);
        backupItem.BackupId = _backup!.Id;
        backupItem.Action = sourceFile.Modified ? BackupItem.Actions.Modified : BackupItem.Actions.Added;
        backupItem.Type = sourceFile.Type;
        backupItem.OriginalCreated = sourceFile.Created;
        backupItem.OriginalExtension = sourceFile.Extension;
        backupItem.OriginalHidden = sourceFile.Hidden;
        backupItem.OriginalLastModified = sourceFile.LastModified;
        backupItem.OriginalName = sourceFile.Name;
        backupItem.OriginalPath = sourceFile.FullPath;
        backupItem.OriginalSize = sourceFile.Size;
        backupFile.BackupItems.Add(backupItem);

        string? destPath;
        using var destFm = FileManagerFactory.GetFileManager(Settings.Destination, out destPath);
        var destFilePath = destFm.CombinePath(destPath, _backup.Guid, backupItem.Volume!.Name!, backupItem.Name!);
        switch (sourceFile.Type) {
            case File.FileTyes.Directory:
                // Create a zero byte file as a placeholder
                await using (var _ = await destFm.OpenFile(destFilePath, true)) {
                }
                break;
            case File.FileTyes.File:
                using (var sourceFm = FileManagerFactory.GetFileManager(sourceFile.FullPath, out _)) {
                    LogDebug($"Backing up {sourceFile.FullPath} to {destFilePath}");

                    DateTime? lastFileProgress = null;
                    using (var sourceStream = await sourceFm.OpenFile(sourceFile.FullPath)) {
                        using (var destStream = await destFm.OpenFile(destFilePath, true)) {
                            var progressAction = new Action<long>((copied) =>
                            {
                                if (lastFileProgress == null || copied == sourceFile.Size ||
                                    (DateTime.UtcNow - lastFileProgress.Value).TotalMilliseconds >= 500) {
                                    FileProgress?.Invoke(this, sourceFile.FullPath!, copied, sourceFile.Size);
                                    lastFileProgress = DateTime.UtcNow;
                                }
                            });

                            if (encryptor != null)
                                await encryptor.Encrypt(backupItem, sourceStream, destStream, Settings.Compress,
                                    progressAction);
                            else if (Settings.Compress)
                                await BackupFileFromSourceCompress(backupItem, sourceStream, destStream, progressAction);
                            else
                                await BackupFileFromSourceCopy(backupItem, sourceStream, destStream, progressAction);
                        }
                    }
                }
                break;
        }
        await backupFile.SaveChangesAsync();
        return true;
    }

    private async Task<bool> BackupFileFromSourceCopy(BackupItem backupItem, Stream sourceStream, Stream destStream, Action<long>? progressAction = null)
    {
        using (var hasher = MD5.Create()) {
            using (CryptoStream hashStream = new CryptoStream(destStream, hasher, CryptoStreamMode.Write, true)) {
                long copied = 0;
                int read;
                byte[] buffer = new byte[4096];
                while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    await hashStream.WriteAsync(buffer, 0, read);
                    copied += read;
                    progressAction?.Invoke(copied);
                }
            }

            backupItem.Size = destStream.Position;
            backupItem.MD5 = BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
        }

        return true;
    }

    private async Task<bool> BackupFileFromSourceCompress(BackupItem backupItem, Stream sourceStream, Stream destStream, Action<long>? progressAction = null)
    {
        using (var hasher = MD5.Create()) {
            using (CryptoStream hashStream = new CryptoStream(destStream, hasher, CryptoStreamMode.Write, true)) {
                using (var compressorStream = new GZipStream(hashStream, CompressionLevel.Optimal, true)) {
                    long copied = 0;
                    int read;
                    byte[] buffer = new byte[4096];
                    while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                        await compressorStream.WriteAsync(buffer, 0, read);
                        copied += read;
                        progressAction?.Invoke(copied);
                    }
                }
            }

            backupItem.Size = destStream.Position;
            backupItem.MD5 = BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
        }

        return true;
    }

    private async Task<bool> DeleteItem(BackupFile? backupFile, File? sourceFile)
    {
        if (backupFile == null)
            throw new ArgumentNullException(nameof(backupFile));
        if (sourceFile == null)
            throw new ArgumentNullException(nameof(sourceFile));

        var item = new BackupItem()
        {
            BackupId = _backup!.Id,
            Action = BackupItem.Actions.Deleted,
            Type = sourceFile.Type,
            OriginalCreated = sourceFile.Created,
            OriginalExtension = sourceFile.Extension,
            OriginalHidden = sourceFile.Hidden,
            OriginalLastModified = sourceFile.LastModified,
            OriginalName = sourceFile.Name,
            OriginalPath = sourceFile.FullPath,
            OriginalSize = sourceFile.Size,

        };
        backupFile.BackupItems.Add(item);
        await backupFile.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Get the next <see cref="DataModel.BackupItem"/>.
    /// Find the first free <see cref="BackupVolume"/> or create a new one for the file
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to backup</param>
    /// <returns>A <see cref="DataModel.BackupItem"/></returns>
    private async Task<BackupItem> GetNextBackupItem(BackupFile backupFile)
    {
        int? volumeId;
        string? volumeName;
        if (_lastUsedVolume?.ItemsCount < Settings.MaxFilesPerDirectory) {
            volumeId = _lastUsedVolume.Id;
            volumeName = _lastUsedVolume.Name;
            _lastUsedVolume.ItemsCount++;
        } else {
            var volume = await backupFile.BackupVolumes.AsNoTracking()
                .Where(v => v.BackupId == _backup!.Id && v.Items!.Count() < Settings.MaxFilesPerDirectory)
                .FirstOrDefaultAsync();
            if (volume == null) {
                volume = new BackupVolume()
                {
                    BackupId = _backup!.Id,
                    Name = Guid.NewGuid().ToString("N"),
                };
                backupFile.BackupVolumes.Add(volume);
                await backupFile.SaveChangesAsync();
            }

            volumeId = volume.Id;
            volumeName = volume.Name;
            _lastUsedVolume = new LastUsedVolume()
            {
                Id = volume.Id,
                Name = volume.Name,
                ItemsCount = volume.Items?.Count() ?? 1
            };
        }
        string? destPath;
        using var destFm = FileManagerFactory.GetFileManager(Settings.Destination, out destPath);

        var res = new BackupItem()
        {
            Name = Guid.NewGuid().ToString("N"),
            VolumeId = volumeId!.Value
        };
        var volumePath = destFm.CombinePath(destPath, _backup!.Guid, volumeName!);
        if (!await destFm.DirectoryExists(volumePath))
            await destFm.CreateDirectory(volumePath);
        return res;
    }

    private async Task<bool> ReadPathContent(BackupFile? bkpFile, string? rootPath, string? path)
    {
        if (bkpFile == null)
            throw new ArgumentNullException(nameof(bkpFile));
        if (string.IsNullOrEmpty(rootPath))
            throw new ArgumentNullException(nameof(rootPath));
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));

        if (_cancellationToken?.IsCancellationRequested == true)
            return true;

        LogDebug($"  {path}");
        using var fm = FileManagerFactory.GetFileManager(path, out string newPath);
        if (fm == null)
            throw new Exception($"Cannot create FileManager for path {path}");

        await foreach (var file in fm.GetDirectoryContent(newPath, Settings.IncludeHiddenFiles)) {
            if (_cancellationToken?.IsCancellationRequested == true)
                return true;

            file.FileAction = File.FileActions.Backup;
            file.RootPath = rootPath;
            file.RelativePath = file.FullPath?.Substring(file.RootPath.Length);
            bkpFile.Files.Add(file);
            if (file.Type == File.FileTyes.Directory)
                await ReadPathContent(bkpFile, rootPath, file.FullPath);
        }
        return true;
    }

    private async Task<bool> SetBackupResult(BackupFile bkpFile, Backup.Results result)
    {
        var bkp = await bkpFile.Backups.Where(b => b.Id == _backup!.Id).FirstOrDefaultAsync();
        if (bkp != null) {
            bkp.Result = result;
            bkp.CompletedAt = DateTime.UtcNow;

            await bkpFile.SaveChangesAsync();
            return true;
        }
        return false;
    }

   #endregion
}