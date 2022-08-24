using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
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

    private class ProgressData
    {
        public DateTime? Started { get; set; }
        public int TotalFiles { get; set; }
        public int FileIndex { get; set; }
        public long TotalBytes { get; set; }
        public long Bytes { get; set; }
    }

    private class LastUsedVolume
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
    private IFileManager? _sourceFm;
    private string? _sourcePath;
    private bool _skipDatabaseCopy;
    private ProgressData? _progressData;
    #endregion

    #region delegates
    public class ConfirmArgs : EventArgs
    {
        /// <summary>
        /// Number of files that will be backed up
        /// </summary>
        public int FilesToBackup { get; set; }
        /// <summary>
        /// Number of bytes that will be backed up
        /// </summary>
        public long BytesToBackup { get; set; }
    }
    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmBackup;

    public delegate void StepHandler(object sender, Steps step);

    public event StepHandler? Step;

    public delegate void ProgressHandler(object sender, ProgressHandlerArgs args);

    public event ProgressHandler? Progress;

    public delegate void FileProgressHandler(object sender, string filePath, long bytes, long totalBytes);

    /// <summary>
    /// Event fire for file progress
    /// </summary>
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
    public override async Task<BaseJobResult> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        var started = DateTime.UtcNow;
        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        BackupFm = FileManagerFactory.GetFileManager(Settings.Destination, out BackupPath, Settings.DestinationUser, Settings.DestinationPassword, Settings.DestinationOauthSettings);
        _sourceFm = FileManagerFactory.GetFileManager(Settings.Source, out _sourcePath, Settings.SourceUser, Settings.SourcePassword, Settings.SourceOauthSettings);

        Step?.Invoke(this, Steps.OpenDatabase);
        await CheckDestination();
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword, true)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            var transaction = backupFile.BeginTransaction();

            try {
                await RunPrimitive(backupFile);
                await transaction.CommitAsync();

                await backupFile.Vacuum();
                backupFile.Close();

                if (!_skipDatabaseCopy) {
                    // Copy temporary database file to destination
                    if (!string.IsNullOrEmpty(openRes.TempFilePath)) {
                        Step?.Invoke(this, Steps.CopyDatabase);
                        await CopyDatabaseToDestination(openRes, Settings.EncryptPassword);
                    }
                }
            } catch (Exception ex) {
                LogError(ex);
                await transaction.RollbackAsync();
                throw;
            } finally {
                transaction.Dispose();
            }
        }

        return new BackupJobJobResult()
        {
            Success = true,
            Started = started,
            Bytes = _progressData?.TotalBytes,
            Completed = DateTime.UtcNow,
            Guid = _backup!.Guid
        };
    }

    public override void Dispose()
    {
        _sourceFm?.Dispose();
        base.Dispose();
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

        _lastUsedVolume = null;
        Backup? lastBackup = await backupFile.LastBackup();
        // If there is no previous backup this is a full backup
        if (lastBackup == null && Settings.BackupType != Backup.BackupTypes.Full) {
            LogWarning("No previous backup found, switching to Full backup");
            Settings.BackupType = Backup.BackupTypes.Full;
        } else if (lastBackup != null && (lastBackup.Type == Backup.BackupTypes.Differential || lastBackup.Type == Backup.BackupTypes.Incremental) && lastBackup.Type != Settings.BackupType) {
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
        if (!ReadPathContent(backupFile, _sourceFm, _sourcePath, Settings.IncludeHiddenFiles, File.FileActions.Backup, _cancellationToken).Result) {
            LogError("Failed to read source");
            if (!await SetBackupResult(backupFile, Backup.Results.Error))
                LogWarning("Cannot set backup result");
            return;
        }
        if (_cancellationToken?.IsCancellationRequested == true) {
            LogInfo("Cancelled by user");
            if (!await SetBackupResult(backupFile, Backup.Results.Cancelled))
                LogWarning("Cannot set backup result");
            return;
        }
        await backupFile.SaveChangesAsync();

        // Compute files to backup (only if not a full backup)
        // Read last backup items
        LogInfo("Checking files to backup");
        Step?.Invoke(this, Steps.CalculateFilesToBackup);
        if (lastBackup != null && _backup.Type != Backup.BackupTypes.Full) {
            await foreach (var item in backupFile.GetBackupItems(lastBackup.Id)) {
                if (item.Action == BackupItem.Actions.Deleted)
                    continue;

                // Check if the source file has changed
                var sourceFile = await backupFile.Files.Where(f => f.FullPath == item.OriginalPath)
                    .FirstOrDefaultAsync();
                if (sourceFile != null) {
                    if (item.Type == File.FileTyes.File) {
                        if (Settings.SkipHashCheck) {
                            if (sourceFile.Size != item.OriginalSize || sourceFile.LastModified != item.OriginalLastModified) {
                                sourceFile.FileAction = File.FileActions.Backup;
                                sourceFile.Modified = true;
                            } else {
                                sourceFile.FileAction = File.FileActions.None;
                            }
                        } else {
                            var hash = await Files.GetSha256(_sourceFm, sourceFile.FullPath);
                            if (hash != item.OriginalSha256) {
                                sourceFile.FileAction = File.FileActions.Backup;
                                sourceFile.Modified = true;
                            } else {
                                sourceFile.FileAction = File.FileActions.None;
                            }
                        }
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
        if (cArgs.FilesToBackup == 0) {
            LogInfo("No files to backup");
            _skipDatabaseCopy = true;
            return;
        }

        if (ConfirmBackup != null) {
            if (!await ConfirmBackup.Invoke(this, cArgs)) {
                _skipDatabaseCopy = true;
                LogInfo("Cancelled by user");
                if (!await SetBackupResult(backupFile, Backup.Results.Cancelled))
                    LogWarning("Cannot set backup result");
                return;
            }
        }

        // Create the backup directory
        var backupDirectory = BackupFm.CombinePath(BackupPath!, _backup.Guid);
        if (await BackupFm.DirectoryExists(backupDirectory))
            throw new Exception($"The backup destination {backupDirectory} exist");
        await BackupFm.CreateDirectory(backupDirectory);

        // Backup files
        Step?.Invoke(this, Steps.BackupFiles);
        EncryptDecrypt? encryptor = null;
        if (!string.IsNullOrEmpty(Settings.EncryptPassword)) {
            encryptor = new EncryptDecrypt(Settings.EncryptPassword);
        }

        _progressData = new ProgressData()
        {
            Started = DateTime.UtcNow,
            TotalBytes = cArgs.BytesToBackup,
            TotalFiles = cArgs.FilesToBackup
        };
        var filesToBackup = backupFile.Files.AsNoTracking()
                            .Where(f => f.FileAction == File.FileActions.Backup || f.FileAction == File.FileActions.Delete);
        foreach (var fileToBackup in filesToBackup) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                if (!await SetBackupResult(backupFile, Backup.Results.Cancelled))
                    LogWarning("Cannot set backup result");
                return;
            }

            if (fileToBackup.FileAction == File.FileActions.Backup) {
                if (fileToBackup.Type == File.FileTyes.File) {
                    try {
                        var fInfo = await _sourceFm!.GetFileInfo(fileToBackup.FullPath);
                        if (fInfo.Size != fileToBackup.Size || fInfo.LastModified != fileToBackup.LastModified) {
                            LogWarning($"File {fileToBackup.FullPath} changed after the backup started");
                            fileToBackup.Size = fInfo.Size;
                        }
                    } catch (FileNotFoundException) {
                        LogWarning($"File {fileToBackup.FullPath} not found");

                        _progressData.Bytes += fileToBackup.Size;
                        _progressData.FileIndex++;
                        Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.FileIndex, cArgs.FilesToBackup, _progressData.Bytes, _progressData.TotalBytes, GetEta(_progressData.Started, _progressData.Bytes, _progressData.TotalBytes)));
                        continue;
                    }
                }

                int tryCount = 0;
                while (++tryCount <= Settings.RetryOnError) {
                    try {
                        // Compute file size of the backup file if possible
                        long? backupFileSize = Files.GetDestinationFileSize(fileToBackup.Size, Settings.CompressionLevel, encryptor != null);
                        await BackupFileFromSource(backupFile, fileToBackup, encryptor, backupFileSize);
                        _progressData.Bytes += fileToBackup.Size;
                        _progressData.FileIndex++;
                        Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.FileIndex, cArgs.FilesToBackup, _progressData.Bytes, _progressData.TotalBytes, GetEta(_progressData.Started, _progressData.Bytes, _progressData.TotalBytes)));
                        break;
                    } catch (Exception ex) {
                        if (tryCount == Settings.RetryOnError)
                            throw;

                        LogWarning($"Failed to backup {fileToBackup.FullPath}: {ex.Message}, retrying");
                    }
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

        if (Settings.RetryOnError <= 0)
            throw new ArgumentException("RetryOnError must be greater than zero");
    }

    /// <summary>
    /// Check the destination directory.
    /// </summary>
    private async Task CheckDestination()
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        if (Settings.CompressionLevel != CompressionLevel.NoCompression && !BackupFm.SupportCompression())
            throw new Exception("Destination doesn't support compression");

        if (!await BackupFm.DirectoryExists(BackupPath))
            await BackupFm.CreateDirectory(BackupPath);
    }

    /// <summary>
    /// Backup a <see cref="DataModel.File"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/></param>
    /// <param name="sourceFile">The <see cref="DataModel.File"/> to backup</param>
    /// <param name="encryptor">The optional <see cref="Utility.EncryptDecrypt"/> to encrypt the file</param>
    /// <param name="backupFileSize">The size of the backup file, if known</param>
    /// <exception cref="ArgumentNullException"></exception>
    private async Task BackupFileFromSource(BackupFile? backupFile,
        File? sourceFile,
        EncryptDecrypt? encryptor,
        long? backupFileSize)
    {
        if (_sourceFm == null)
            throw new Exception("Source file manager not initialized");
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");
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
        backupItem.OriginalRelativePath = sourceFile.RelativePath;
        backupItem.OriginalSize = sourceFile.Size;

        var destFilePath = BackupFm.CombinePath(BackupPath!, _backup.Guid, _lastUsedVolume!.Name!, backupItem.Name!);
        switch (sourceFile.Type) {
            case File.FileTyes.Directory:
                // Create a placeholder file
                // We don't write a zero byte file because it would be not valid for some remote filesystems (like OneDrive)
                if (!await _sourceFm.DirectoryExists(sourceFile.FullPath)) {
                    LogWarning($"Directory {sourceFile.FullPath} not found");
                    return;
                }

                var size = 9;
                FileProgress?.Invoke(this, sourceFile.FullPath!, 0, size);
                using (var sourceStream = new MemoryStream(Encoding.ASCII.GetBytes("DIRECTORY"))) {
                    await using (var destStream = await BackupFm.OpenFile(destFilePath, true, size)) {
                        await sourceStream.CopyToAsync(destStream);
                    }
                }

                FileProgress?.Invoke(this, sourceFile.FullPath!, size, size);
                break;
            case File.FileTyes.File:
                LogDebug($"Backing up {sourceFile.FullPath} to {destFilePath}");
                FileProgress?.Invoke(this, sourceFile.FullPath!, 0, sourceFile.Size);
                DateTime? lastFileProgress = null;
                using (var sourceStream = await _sourceFm.OpenFile(sourceFile.FullPath)) {
                    using (var destStream = await BackupFm.OpenFile(destFilePath, true, backupFileSize)) {
                        var progressAction = new Action<long>((copied) =>
                        {
                            if (lastFileProgress == null || copied == sourceFile.Size ||
                                (DateTime.UtcNow - lastFileProgress.Value).TotalMilliseconds >= 500) {
                                FileProgress?.Invoke(this, sourceFile.FullPath!, copied, sourceFile.Size);
                                Progress?.Invoke(this, new ProgressHandlerArgs(_progressData!.FileIndex, _progressData.TotalFiles, _progressData.Bytes + copied, _progressData.TotalBytes, GetEta(_progressData.Started, _progressData.Bytes + copied, _progressData.TotalBytes)));
                                lastFileProgress = DateTime.UtcNow;
                            }
                        });

                        if (encryptor != null)
                            await encryptor.Encrypt(backupItem, sourceStream, destStream, Settings.CompressionLevel,
                                progressAction);
                        else if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                            await BackupFileFromSourceCompress(backupItem, sourceStream, destStream, Settings.CompressionLevel, progressAction);
                        else
                            await BackupFileFromSourceCopy(backupItem, sourceStream, destStream, progressAction);
                    }
                }
                break;
        }

        backupFile.BackupItems.Add(backupItem);
        await backupFile.SaveChangesAsync();
    }

    private async Task BackupFileFromSourceCopy(BackupItem backupItem, Stream sourceStream, Stream destStream, Action<long>? progressAction = null)
    {
        using (var hasher = SHA256.Create()) {
            using (CryptoStream hashStream = new CryptoStream(destStream, hasher, CryptoStreamMode.Write, true)) {
                long copied = 0;
                int read;
                byte[] buffer = new byte[32768];
                while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    await hashStream.WriteAsync(buffer, 0, read);
                    copied += read;
                    progressAction?.Invoke(copied);
                }
            }

            backupItem.Size = destStream.Position;
            backupItem.Sha256 = BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
            backupItem.OriginalSha256 = backupItem.Sha256;
        }
    }

    private async Task BackupFileFromSourceCompress(BackupItem backupItem, Stream sourceStream, Stream destStream, CompressionLevel compressionLevel, Action<long>? progressAction = null)
    {
        using (var sourceHasher = SHA256.Create()) {
            using (var destHasher = SHA256.Create()) {
                using (CryptoStream hashStream = new CryptoStream(destStream, destHasher, CryptoStreamMode.Write, true)) {
                    using (var compressorStream = new GZipStream(hashStream, compressionLevel, true)) {
                        long copied = 0;
                        int read;
                        byte[] buffer = new byte[32768];
                        while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                            sourceHasher.TransformBlock(buffer, 0, read, null, 0);
                            await compressorStream.WriteAsync(buffer, 0, read);
                            copied += read;
                            progressAction?.Invoke(copied);
                        }
                    }
                }

                backupItem.Size = destStream.Position;
                backupItem.Sha256 = BitConverter.ToString(destHasher.Hash!).Replace("-", "").ToLowerInvariant();
            }

            sourceHasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            backupItem.OriginalSha256 = BitConverter.ToString(sourceHasher.Hash!).Replace("-", "").ToLowerInvariant();
        }
    }

    private async Task DeleteItem(BackupFile? backupFile, File? sourceFile)
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
    }

    /// <summary>
    /// Get the next <see cref="DataModel.BackupItem"/>.
    /// Find the first free <see cref="BackupVolume"/> or create a new one for the file
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to backup</param>
    /// <returns>A <see cref="DataModel.BackupItem"/></returns>
    private async Task<BackupItem> GetNextBackupItem(BackupFile backupFile)
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

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

        var res = new BackupItem()
        {
            Name = Guid.NewGuid().ToString("N"),
            VolumeId = volumeId!.Value
        };
        var volumePath = BackupFm.CombinePath(BackupPath!, _backup!.Guid, volumeName!);
        if (!await BackupFm.DirectoryExists(volumePath))
            await BackupFm.CreateDirectory(volumePath);
        return res;
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