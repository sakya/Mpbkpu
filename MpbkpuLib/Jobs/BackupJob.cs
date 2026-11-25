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
        CopyDatabase,
        DeleteOldBackups
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
    private bool _hasWarnings;
    private Backup.Results? _result;
    private List<string> _chainsToDelete = new();
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

        LogInfo("Opening database");
        Step?.Invoke(this, Steps.OpenDatabase);
        await CheckDestination();
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword, true, OnOpenBackup, _cancellationToken)) {
            var backupFile = openRes.BackupFile;
            // True when the database in the destination references this backup
            var persisted = false;
            try {
                await backupFile.EmptyFiles();
                await using (var transaction = backupFile.BeginTransaction()) {
                    try {
                        await RunPrimitive(backupFile);
                        await transaction.CommitAsync();
                        persisted = string.IsNullOrEmpty(openRes.TempFilePath);
                    } catch {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }

                await backupFile.Vacuum();
                backupFile.Close();

                if (!_skipDatabaseCopy) {
                    // Copy temporary database file to destination
                    if (!string.IsNullOrEmpty(openRes.TempFilePath)) {
                        LogInfo("Copying database");
                        Step?.Invoke(this, Steps.CopyDatabase);
                        await CopyDatabaseToDestination(openRes, Settings.EncryptPassword, OnCopyBackup);
                        persisted = true;
                    }
                }
            } catch (Exception ex) {
                LogError(ex);
                if (!persisted)
                    await DeleteOrphanBackupDirectory();
                throw;
            }
        }

        if (_chainsToDelete.Count > 0)
            await DeleteOldChains();

        return new BackupJobJobResult()
        {
            Success = true,
            Started = started,
            Bytes = _progressData?.Bytes,
            Completed = DateTime.UtcNow,
            Guid = _backup!.Guid,
            Result = _result
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
        if (_sourceFm == null)
            throw new Exception("Source file manager not initialized");

        _lastUsedVolume = null;
        Backup? lastBackup = await backupFile.LastBackup();
        // A chain is a full backup followed by backups of the same type: start a new chain when needed
        if (Settings.BackupType != Backup.BackupTypes.Full) {
            var lastFull = await backupFile.LastBackup(Backup.BackupTypes.Full);
            string? newChainReason = null;
            if (lastFull == null)
                newChainReason = "No previous full backup found";
            else if (lastFull.Result is not (Backup.Results.Completed or Backup.Results.CompletedWithWarnings))
                newChainReason = $"The last full backup is {lastFull.Result}";
            else if (lastBackup!.Type != Backup.BackupTypes.Full && lastBackup.Type != Settings.BackupType)
                newChainReason = $"Backup type changed from {lastBackup.Type} to {Settings.BackupType}";
            else if (Settings.FullBackupEveryDays > 0 && lastFull.StartedAt <= DateTime.UtcNow.AddDays(-Settings.FullBackupEveryDays))
                newChainReason = $"The last full backup is older than {Settings.FullBackupEveryDays} days";

            if (newChainReason != null) {
                LogWarning($"{newChainReason}, switching to Full backup");
                Settings.BackupType = Backup.BackupTypes.Full;
            }
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
        await backupFile.EmptyFiles();
        if (!await ReadPathContent(backupFile, _sourceFm, _sourcePath, Settings.IncludeHiddenFiles, File.FileActions.Backup, _cancellationToken)) {
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
        foreach (var directory in UnreadableDirectories)
            await AddWarning(backupFile, $"Directory {directory} is not readable: its content has not been backed up");

        // Compute files to backup (only if not a full backup)
        // Read last backup items
        LogInfo("Checking files to backup");
        Step?.Invoke(this, Steps.CalculateFilesToBackup);
        // A differential backup contains all the changes since the last full backup
        var compareBackup = _backup.Type == Backup.BackupTypes.Differential ?
            await backupFile.LastBackup(Backup.BackupTypes.Full) :
            lastBackup;
        if (compareBackup != null && _backup.Type != Backup.BackupTypes.Full) {
            await foreach (var item in backupFile.GetBackupItems(compareBackup.Id)) {
                if (item.Action == BackupItem.Actions.Deleted)
                    continue;

                // Check if the source file has changed
                var sourceFile = await backupFile.Files.Where(f => f.RelativePath == item.OriginalRelativePath)
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
                            var hash = await Files.GetSha256(_sourceFm, sourceFile.FullPath, null, _cancellationToken);
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
                } else if (IsInUnreadableDirectory(item.OriginalRelativePath, _sourceFm.PathSeparator)) {
                    // Not readable now: don't record it as deleted
                    LogWarning($"Cannot check {item.OriginalPath}: its directory is not readable");
                } else {
                    backupFile.Files.Add(new File()
                    {
                        FileAction = File.FileActions.Delete,
                        Created = item.OriginalCreated,
                        Extension = item.OriginalExtension,
                        FullPath = item.OriginalPath,
                        RelativePath = item.OriginalRelativePath,
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
        LogInfo("Backing up files");
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
                            .Where(f => f.FileAction == File.FileActions.Backup || f.FileAction == File.FileActions.Delete)
                            .OrderBy(f => f.RelativePath);
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
                            fileToBackup.LastModified = fInfo.LastModified;
                        }
                    } catch (FileNotFoundException) {
                        LogWarning($"File {fileToBackup.FullPath} not found");

                        _progressData.Bytes += fileToBackup.Size;
                        _progressData.FileIndex++;
                        Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.FileIndex, cArgs.FilesToBackup, _progressData.Bytes, _progressData.TotalBytes, GetEta(_progressData.Started, _progressData.Bytes, _progressData.TotalBytes)));
                        continue;
                    }
                }

                var tryCount = 0;
                while (++tryCount <= Settings.RetryOnError) {
                    if (_cancellationToken?.IsCancellationRequested == true) {
                        LogInfo("Cancelled by user");
                        if (!await SetBackupResult(backupFile, Backup.Results.Cancelled))
                            LogWarning("Cannot set backup result");
                        return;
                    }
                    try {
                        // Compute file size of the backup file if possible
                        long? backupFileSize = Files.GetDestinationFileSize(fileToBackup.Size, Settings.CompressionLevel, encryptor != null);
                        // On the last try accept a file changed during the copy (with a warning)
                        await BackupFileFromSource(backupFile, fileToBackup, encryptor, backupFileSize, tryCount == Settings.RetryOnError);
                        _progressData.Bytes += fileToBackup.Size;
                        _progressData.FileIndex++;
                        Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.FileIndex, cArgs.FilesToBackup, _progressData.Bytes, _progressData.TotalBytes, GetEta(_progressData.Started, _progressData.Bytes, _progressData.TotalBytes)));
                        break;
                    } catch (Exception ex) when (tryCount == Settings.RetryOnError && IsSourceAccessError(ex)) {
                        // A locked or protected file must not abort the whole backup
                        await AddWarning(backupFile, $"File {fileToBackup.FullPath} has not been backed up: {ex.Message}");
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
        if (!await SetBackupResult(backupFile, _hasWarnings ? Backup.Results.CompletedWithWarnings : Backup.Results.Completed))
            LogWarning("Cannot set backup result");
        await backupFile.SaveChangesAsync();
        if (_backup.Type == Backup.BackupTypes.Full && Settings.KeepChains > 0)
            _chainsToDelete = await GetChainsToDelete(backupFile);
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

        if (Utility.Files.IsSameOrSubPath(Settings.Source, Settings.Destination)) {
            throw new ArgumentException(Utility.Files.IsSameOrSubPath(Settings.Destination, Settings.Source) ?
                "Source and Destination cannot be the same" :
                "Destination cannot be a subdirectory of Source");
        }

        if (Settings.MaxFilesPerDirectory <= 0)
            throw new ArgumentException("MaxFilesPerDirectory must be greater than zero");

        if (Settings.RetryOnError <= 0)
            throw new ArgumentException("RetryOnError must be greater than zero");

        if (!string.IsNullOrEmpty(Settings.EncryptPassword) && Settings.EncryptPassword.Length < 8)
            throw new ArgumentException("EncryptPassword must have at least 8 characters");

        if (Settings.FullBackupEveryDays < 0)
            throw new ArgumentException("FullBackupEveryDays cannot be negative");

        if (Settings.KeepChains < 0)
            throw new ArgumentException("KeepChains cannot be negative");
    }

    /// <summary>
    /// Check the destination directory.
    /// </summary>
    private async Task CheckDestination()
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        if (Settings.CompressionLevel != CompressionLevel.NoCompression && !BackupFm.SupportCompression)
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
    /// <param name="acceptChanges">If false a file changed during the copy throws an exception (to retry)</param>
    /// <exception cref="ArgumentNullException"></exception>
    private async Task BackupFileFromSource(BackupFile? backupFile,
        File? sourceFile,
        EncryptDecrypt? encryptor,
        long? backupFileSize,
        bool acceptChanges)
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
        try {
            switch (sourceFile.Type) {
                case File.FileTyes.Directory:
                    // Create a placeholder file
                    // We don't write a zero byte file because it would be not valid for some remote filesystems (like OneDrive)
                    if (!await _sourceFm.DirectoryExists(sourceFile.FullPath)) {
                        LogWarning($"Directory {sourceFile.FullPath} not found");
                        return;
                    }

                    var size = 9;
                    FileProgress?.Invoke(this, sourceFile.RelativePath!, 0, size);
                    using (var sourceStream = new MemoryStream(Encoding.ASCII.GetBytes("DIRECTORY"))) {
                        await using (var destStream = await BackupFm.OpenFile(destFilePath, true, size)) {
                            await sourceStream.CopyToAsync(destStream);
                        }
                    }

                    FileProgress?.Invoke(this, sourceFile.RelativePath!, size, size);
                    break;
                case File.FileTyes.File:
                    LogDebug($"Backing up {sourceFile.FullPath} to {destFilePath}");
                    FileProgress?.Invoke(this, sourceFile.RelativePath!, 0, sourceFile.Size);
                    DateTime? lastFileProgress = null;
                    using (var sourceStream = await _sourceFm.OpenFile(sourceFile.FullPath)) {
                        using (var destStream = await BackupFm.OpenFile(destFilePath, true, backupFileSize)) {
                            var progressAction = new Action<long>((copied) =>
                            {
                                if (lastFileProgress == null || copied == sourceFile.Size ||
                                    (DateTime.UtcNow - lastFileProgress.Value).TotalMilliseconds >= 500) {
                                    FileProgress?.Invoke(this, sourceFile.RelativePath!, copied, sourceFile.Size);
                                    Progress?.Invoke(this, new ProgressHandlerArgs(_progressData!.FileIndex, _progressData.TotalFiles, _progressData.Bytes + copied, _progressData.TotalBytes, GetEta(_progressData.Started, _progressData.Bytes + copied, _progressData.TotalBytes)));
                                    lastFileProgress = DateTime.UtcNow;
                                }
                            });

                            if (encryptor != null)
                                await encryptor.Encrypt(backupItem, sourceStream, destStream, Settings.CompressionLevel,
                                    progressAction, _cancellationToken);
                            else if (Settings.CompressionLevel != CompressionLevel.NoCompression)
                                await BackupFileFromSourceCompress(backupItem, sourceStream, destStream, Settings.CompressionLevel, progressAction);
                            else
                                await BackupFileFromSourceCopy(backupItem, sourceStream, destStream, progressAction);
                        }
                    }

                    // Check that the file has not been modified during the copy
                    var fileInfo = await _sourceFm.GetFileInfo(sourceFile.FullPath);
                    if (fileInfo.Size != sourceFile.Size || fileInfo.LastModified != sourceFile.LastModified) {
                        sourceFile.Size = fileInfo.Size;
                        sourceFile.LastModified = fileInfo.LastModified;
                        if (!acceptChanges)
                            throw new IOException($"File {sourceFile.FullPath} changed during the copy");
                        await AddWarning(backupFile, $"File {sourceFile.FullPath} changed during the copy: the backed up copy may be inconsistent");
                    }
                    break;
            }
        } catch {
            // Don't leave a partial file in the destination (retry, cancellation or error)
            await TryDeleteFile(BackupFm, destFilePath);
            throw;
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
                    _cancellationToken?.ThrowIfCancellationRequested();
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
                            _cancellationToken?.ThrowIfCancellationRequested();
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
            OriginalRelativePath = sourceFile.RelativePath,
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

    /// <summary>
    /// Get the GUIDs of the full backups of the chains older than the last <see cref="BackupJobSettings.KeepChains"/> completed full backups
    /// </summary>
    private async Task<List<string>> GetChainsToDelete(BackupFile backupFile)
    {
        var keptFullIds = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Type == Backup.BackupTypes.Full && (b.Result == Backup.Results.Completed || b.Result == Backup.Results.CompletedWithWarnings))
            .OrderByDescending(b => b.Id)
            .Select(b => b.Id)
            .Take(Settings.KeepChains)
            .ToListAsync();
        if (keptFullIds.Count < Settings.KeepChains)
            return new List<string>();

        var oldestKeptId = keptFullIds.Last();
        return await backupFile.Backups.AsNoTracking()
            .Where(b => b.Type == Backup.BackupTypes.Full && b.Id < oldestKeptId)
            .OrderBy(b => b.Id)
            .Select(b => b.Guid)
            .ToListAsync();
    }

    /// <summary>
    /// Delete the old chains: deleting a full backup deletes also its incremental or differential backups.
    /// The backup is already completed, so a failure is logged as a warning.
    /// </summary>
    private async Task DeleteOldChains()
    {
        Step?.Invoke(this, Steps.DeleteOldBackups);
        foreach (var guid in _chainsToDelete) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                return;
            }

            LogInfo($"Deleting old backup chain {guid}");
            try {
                using var job = new DeleteJob(new DeleteJobSettings()
                {
                    BackupPath = Settings.Destination,
                    BackupPathUser = Settings.DestinationUser,
                    BackupPathPassword = Settings.DestinationPassword,
                    BackupOauthSettings = Settings.DestinationOauthSettings,
                    BackupGuid = guid,
                    EncryptPassword = Settings.EncryptPassword,
                    LogLevel = BaseJobSettings.LogLevels.Warning
                });
                job.LogEmitted += (_, level, message) =>
                {
                    if (level == BaseJobSettings.LogLevels.Error)
                        LogError(message);
                    else
                        LogWarning(message);
                };
                await job.Run(_cancellationToken ?? CancellationToken.None);
            } catch (Exception ex) {
                LogWarning($"Failed to delete old backup chain {guid}: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>
    /// Delete the files of a backup not referenced by the database in the destination
    /// </summary>
    private async Task DeleteOrphanBackupDirectory()
    {
        if (_backup == null || BackupFm == null)
            return;

        var backupDirectory = BackupFm.CombinePath(BackupPath!, _backup.Guid);
        try {
            if (await BackupFm.DirectoryExists(backupDirectory)) {
                LogWarning($"Deleting the files of the failed backup {backupDirectory}");
                await DeleteDirectoryRecursive(BackupFm, backupDirectory);
            }
        } catch (Exception ex) {
            LogWarning($"Cannot delete {backupDirectory}: {ex.Message}");
        }
    }

    /// <summary>
    /// Log a warning and save it in the backup
    /// </summary>
    private async Task AddWarning(BackupFile backupFile, string message)
    {
        LogWarning(message);
        _hasWarnings = true;
        backupFile.BackupWarnings.Add(new BackupWarning()
        {
            BackupId = _backup!.Id,
            Message = message.Length > 2048 ? message[..2048] : message
        });
        await backupFile.SaveChangesAsync();
    }

    /// <summary>
    /// Errors accessing a source file: access denied, file locked by another process (sharing or lock violation)
    /// </summary>
    private static bool IsSourceAccessError(Exception ex)
    {
        const int sharingViolation = 32;
        const int lockViolation = 33;
        return ex is UnauthorizedAccessException ||
               (ex is IOException && (ex.HResult & 0xFFFF) is sharingViolation or lockViolation);
    }

    private async Task<bool> SetBackupResult(BackupFile bkpFile, Backup.Results result)
    {
        _result = result;
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