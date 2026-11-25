using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using Newtonsoft.Json;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.Jobs;

/// <summary>
/// A job to restore files from a backup
/// </summary>
public class RestoreJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        ReadDestination,
        CalculateFilesToRestore,
        DeleteFiles,
        RestoreFiles
    }

    #region classes
    private class ProgressData
    {
        public EtaEstimator? Eta { get; set; }
        public int DeleteTotalFiles { get; set; }
        public int DeleteFileIndex { get; set; }
        public long DeleteTotalBytes { get; set; }
        public long DeleteBytes { get; set; }

        public int RestoreTotalFiles { get; set; }
        public int RestoreFileIndex { get; set; }
        public long RestoreTotalBytes { get; set; }
        public long RestoreBytes { get; set; }
    }
    #endregion

    #region private members
    private const string TempFileSuffix = ".mpbkpu-tmp";
    private CancellationToken? _cancellationToken;
    private ProgressData? _progressData;
    private IFileManager? _destinationFm;
    private string? _destinationPath;
    #endregion

    #region delegates
    public delegate void StepHandler(object sender, Steps step);
    public event StepHandler? Step;

    public class ConfirmArgs : EventArgs
    {
        /// <summary>
        /// Number of files that will be deleted from the destination
        /// </summary>
        public int FilesToDelete { get; set; }
        /// <summary>
        /// Number of bytes that will be deleted from the destination
        /// </summary>
        public long BytesToDelete { get; set; }
        /// <summary>
        /// Number of files that will be restored to the destination
        /// </summary>
        public int FilesToRestore { get; set; }
        /// <summary>
        /// Number of bytes that will be restored to the destination
        /// </summary>
        public long BytesToRestore { get; set; }
    }

    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmRestore;

    public delegate void ProgressHandler(object sender, ProgressHandlerArgs args);
    public event ProgressHandler? DeleteProgress;
    public event ProgressHandler? RestoreProgress;

    public delegate void FileProgressHandler(object sender, string filePath, long bytes, long totalBytes);
    /// <summary>
    /// Event fire for file progress
    /// </summary>
    /// <remarks>For directories this event fires with bytes and totalBytes equal to zero</remarks>
    public event FileProgressHandler? FileProgress;
    #endregion

    public RestoreJob(RestoreJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new RestoreJobSettings Settings
    {
        get { return (base.Settings as RestoreJobSettings)!; }
    }

    #endregion

    #region public operations
    public override async Task<BaseJobResult> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        var started = DateTime.UtcNow;
        LogInfo("Starting with settings:");
        LogInfo(SerializeSettings());

        _destinationFm = FileManagerFactory.GetFileManager(Settings.Destination, out _destinationPath, Settings.DestinationUser, Settings.DestinationPassword, Settings.DestinationOauthSettings);

        LogInfo("Opening database");
        Step?.Invoke(this, Steps.OpenDatabase);
        await CheckDestination();
        bool success;
        using (var openRes = await OpenBackupDestinations(Settings.BackupDestinations, false, _cancellationToken)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            try {
                success = await RunPrimitive(backupFile);
            } catch (Exception ex) {
                LogError(ex);
                throw;
            }
        }

        return new RestoreJobResult()
        {
            Success = success,
            Started = started,
            Bytes = _progressData?.RestoreTotalBytes,
            Completed = DateTime.UtcNow
        };
    }

    public override void Dispose()
    {
        _destinationFm?.Dispose();
        base.Dispose();
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
        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null) {
            LogError($"Backup with GUID {Settings.BackupGuid} not found");
            return false;
        }
        if (bkp.Result is not (Backup.Results.Completed or Backup.Results.CompletedWithWarnings)) {
            LogError($"Cannot restore a backup with {bkp.Result} result");
            return false;
        }

        var backupSettings = JsonConvert.DeserializeObject<BackupJobSettings>(bkp.Settings!);
        if (backupSettings == null)
            throw new Exception("Cannot read backup settings");
        if (backupSettings.Source != Settings.Destination) {
            LogWarning($"Restoring to a different destination (was {backupSettings.Source})");
        }

        // Read destination
        Step?.Invoke(this, Steps.ReadDestination);
        LogInfo($"Reading destination {Settings.Destination}");
        if (!await ReadPathContent(backupFile, _destinationFm, _destinationPath, true, File.FileActions.Delete, _cancellationToken)) {
            LogError("Failed to read destination");
            return false;
        }
        if (_cancellationToken?.IsCancellationRequested == true) {
            LogInfo("Cancelled by user");
            return false;
        }
        await backupFile.SaveChangesAsync();

        Step?.Invoke(this, Steps.CalculateFilesToRestore);
        // Compute files to restore
        await foreach (var item in backupFile.GetBackupItems(bkp.Id)) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                return false;
            }

            var file = await backupFile.Files
                .Where(f => f.RelativePath == item.OriginalRelativePath)
                .FirstOrDefaultAsync();
            if (file == null) {
                file = new File()
                {
                    BackupItemId = item.Id,
                    Type = item.Type,
                    FileAction = File.FileActions.Restore,
                    FullPath = item.OriginalPath,
                    RelativePath = item.OriginalRelativePath,
                    Name = item.OriginalName,
                    Size = item.OriginalSize
                };
                backupFile.Files.Add(file);
            } else if (file.Type == File.FileTyes.File) {
                if (Settings.SkipHashCheck) {
                    if (file.Size != item.OriginalSize || !Files.SameModificationTime(file.LastModified, item.OriginalLastModified)) {
                        file.BackupItemId = item.Id;
                        file.FileAction = File.FileActions.Restore;
                    } else {
                        file.FileAction = File.FileActions.None;
                    }
                } else {
                    var hash = await Files.GetSha256(_destinationFm, file.FullPath, null, _cancellationToken);
                    if (hash != item.OriginalSha256) {
                        file.BackupItemId = item.Id;
                        file.FileAction = File.FileActions.Restore;
                    } else {
                        file.FileAction = File.FileActions.None;
                    }
                }
            } else {
                file.FileAction = File.FileActions.None;
            }
        }
        await backupFile.SaveChangesAsync();

        var cArgs = new ConfirmArgs()
        {
            FilesToRestore = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Restore)
                .CountAsync(),
            BytesToRestore = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Restore)
                .Select(f => f.Size)
                .SumAsync(),
            FilesToDelete = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Delete)
                .CountAsync(),
            BytesToDelete = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Delete)
                .Select(f => f.Size)
                .SumAsync(),
        };
        if (cArgs.FilesToRestore == 0 && cArgs.FilesToDelete == 0) {
            LogInfo("No files to restore");
            return true;
        }

        if (ConfirmRestore != null) {
            if (!await ConfirmRestore.Invoke(this, cArgs)) {
                LogInfo("Cancelled by user");
                return false;
            }
        }

        _progressData = new ProgressData()
        {
            Eta = StartEta(0, cArgs.FilesToDelete, _destinationFm),
            DeleteTotalBytes = cArgs.BytesToDelete,
            DeleteTotalFiles = cArgs.FilesToDelete,

            RestoreTotalBytes = cArgs.BytesToRestore,
            RestoreTotalFiles = cArgs.FilesToRestore
        };

        EncryptDecrypt? encryptor = null;
        if (!string.IsNullOrEmpty(Settings.EncryptPassword)) {
            encryptor = new EncryptDecrypt(Settings.EncryptPassword);
        }

        if (cArgs.FilesToDelete > 0) {
            LogInfo("Deleting files");
            Step?.Invoke(this, Steps.DeleteFiles);
            var filesToDelete = backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Delete)
                .OrderBy(f => f.RelativePath)
                .ThenBy(f => f.Type);
            foreach (var fileToDelete in filesToDelete) {
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    return false;
                }

                var tryCount = 0;
                while (++tryCount <= Settings.RetryOnError) {
                    try {
                        await DeleteFile(fileToDelete);

                        _progressData.DeleteBytes += fileToDelete.Size;
                        _progressData.DeleteFileIndex++;
                        DeleteProgress?.Invoke(this, new ProgressHandlerArgs(_progressData.DeleteFileIndex, _progressData.DeleteTotalFiles,
                            _progressData.DeleteBytes, _progressData.DeleteTotalBytes, _progressData.Eta!.Update(0, _progressData.DeleteFileIndex)));
                        break;
                    } catch (Exception ex) {
                        if (tryCount == Settings.RetryOnError)
                            throw;

                        LogWarning($"Failed to delete {fileToDelete.FullPath}: {ex.Message}, retrying");
                    }
                }
            }
        }

        _progressData.Eta = StartEta(_progressData.RestoreTotalBytes, cArgs.FilesToRestore,
            BackupDestinations!.Entries.Select(e => e.Fm).Append(_destinationFm).ToArray());
        LogInfo("Restoring files");
        Step?.Invoke(this, Steps.RestoreFiles);
        var filesToRestore = backupFile.Files
            .Include(f => f.BackupItem)
            .ThenInclude(i => i!.Volume)
            .Include(f => f.BackupItem)
            .ThenInclude(i => i!.Backup)
            .AsNoTracking()
            .Where(f => f.FileAction == File.FileActions.Restore)
            .OrderBy(f => f.RelativePath)
            .ThenByDescending(f => f.Type);
        foreach (var fileToRestore in filesToRestore) {
            var tryCount = 0;
            while (++tryCount <= Settings.RetryOnError) {
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    return false;
                }
                try {
                    await RestoreFile(backupSettings, fileToRestore, encryptor);

                    _progressData.RestoreBytes += fileToRestore.Size;
                    _progressData.RestoreFileIndex++;
                    RestoreProgress?.Invoke(this, new ProgressHandlerArgs(_progressData.RestoreFileIndex, cArgs.FilesToRestore, _progressData.RestoreBytes, _progressData.RestoreTotalBytes, _progressData.Eta!.Update(_progressData.RestoreBytes, _progressData.RestoreFileIndex)));
                    break;
                } catch (Exception ex) when (ex is not MissingDestinationException) {
                    // A missing destination does not appear by retrying
                    if (tryCount == Settings.RetryOnError)
                        throw;

                    LogWarning($"Failed to restore {fileToRestore.FullPath}: {ex.Message}, retrying");
                }
            }
        }

        LogInfo("Completed");
        return true;
    }

    /// <summary>
    /// Validate the <see cref="RestoreJobSettings"/>
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    protected override void ValidateSettings()
    {
        DestinationSet.Validate(Settings.BackupDestinations, nameof(Settings.BackupDestinations));
        if (string.IsNullOrEmpty(Settings.BackupGuid))
            throw new ArgumentNullException(nameof(Settings.BackupGuid), "Backup GUID cannot be empty");
        if (string.IsNullOrEmpty(Settings.Destination))
            throw new ArgumentNullException(nameof(Settings.Destination), "Destination path cannot be empty");
        if (Settings.RetryOnError <= 0)
            throw new ArgumentException("RetryOnError must be greater than zero");
    }

    /// <summary>
    /// Check the destination directory.
    /// </summary>
    private async Task CheckDestination()
    {
        if (_destinationFm == null)
            throw new Exception("Destination file manager not initialized");

        if (!await _destinationFm.DirectoryExists(_destinationPath))
            await _destinationFm.CreateDirectory(_destinationPath);
    }

    private async Task RestoreFile(BackupJobSettings backupSettings, File? fileToRestore, EncryptDecrypt? encryptor)
    {
        if (_destinationFm == null)
            throw new Exception("Destination file manager not initialized");
        if (fileToRestore == null)
            throw new ArgumentNullException(nameof(fileToRestore));

        var destFilePath = _destinationFm.CombinePath(_destinationPath!, fileToRestore.RelativePath!);
        LogDebug($"Restoring {fileToRestore.FullPath} to {destFilePath}");
        switch (fileToRestore.Type) {
            case File.FileTyes.Directory:
                FileProgress?.Invoke(this, fileToRestore.RelativePath!, 0, 9);
                if (!await _destinationFm.DirectoryExists(destFilePath))
                    await _destinationFm.CreateDirectory(destFilePath);

                // Restore last modified date (only on local file system)
                if (_destinationFm is BaseLocalFileSystem && fileToRestore.BackupItem!.OriginalLastModified.HasValue) {
                    Directory.SetLastWriteTimeUtc(destFilePath, fileToRestore.BackupItem.OriginalLastModified.Value);
                }
                FileProgress?.Invoke(this, fileToRestore.RelativePath!, 9, 9);
                break;
            case File.FileTyes.File:
                DateTime? lastFileProgress = null;
                // Write to a temporary file: the existing file is replaced only after the hash has been verified
                var tempFilePath = $"{destFilePath}{TempFileSuffix}";
                try {
                    var backupItem = GetBackupItemLocation(fileToRestore.BackupItem);
                    using (var backupStream = await backupItem.Fm.OpenFile(backupItem.Path)) {
                        using (var destStream = await _destinationFm.OpenFile(tempFilePath, true)) {
                            var progressAction = new Action<long>((copied) =>
                            {
                                if (lastFileProgress == null || copied == fileToRestore.Size ||
                                    (DateTime.UtcNow - lastFileProgress.Value).TotalMilliseconds >= 500) {
                                    FileProgress?.Invoke(this, fileToRestore.RelativePath!, copied, fileToRestore.Size);
                                    RestoreProgress?.Invoke(this, new ProgressHandlerArgs(_progressData!.RestoreFileIndex, _progressData.RestoreTotalFiles, _progressData.RestoreBytes + copied, _progressData.RestoreTotalBytes, TimeSpan.Zero));
                                    lastFileProgress = DateTime.UtcNow;
                                }
                            });

                            string? hash;
                            if (encryptor != null)
                                hash = await encryptor.Decrypt(backupStream, destStream, backupSettings.CompressionLevel != CompressionLevel.NoCompression,
                                    progressAction, _cancellationToken);
                            else if (backupSettings.CompressionLevel != CompressionLevel.NoCompression)
                                hash = await RestoreFileFromSourceDecompress(backupStream, destStream, progressAction);
                            else
                                hash = await RestoreFileFromSourceCopy(backupStream, destStream, progressAction);

                            if (hash != fileToRestore.BackupItem!.OriginalSha256)
                                throw new Exception($"Hash mismatch for file {fileToRestore.BackupItem.OriginalRelativePath}");
                        }
                    }
                } catch {
                    if (await _destinationFm.FileExists(tempFilePath))
                        await _destinationFm.DeleteFile(tempFilePath);
                    throw;
                }

                if (await _destinationFm.FileExists(destFilePath))
                    await _destinationFm.DeleteFile(destFilePath);
                await _destinationFm.RenameFile(tempFilePath, fileToRestore.Name);

                // Restore last modified date (only on local file system)
                if (_destinationFm is BaseLocalFileSystem && fileToRestore.BackupItem.OriginalLastModified.HasValue) {
                    System.IO.File.SetLastWriteTimeUtc(destFilePath, fileToRestore.BackupItem.OriginalLastModified.Value);
                }
                break;
        }
    }

    private async Task<string> RestoreFileFromSourceDecompress(Stream sourceStream, Stream destStream, Action<long>? progressAction = null)
    {
        using (var hasher = SHA256.Create()) {
            using (CryptoStream hashStream = new CryptoStream(destStream, hasher, CryptoStreamMode.Write, true)) {
                using (var decompressorStream = new GZipStream(sourceStream, CompressionMode.Decompress, true)) {
                    long copied = 0;
                    int read;
                    byte[] buffer = new byte[32768];
                    while ((read = await decompressorStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                        _cancellationToken?.ThrowIfCancellationRequested();
                        await hashStream.WriteAsync(buffer, 0, read);
                        copied += read;
                        progressAction?.Invoke(copied);
                    }
                }
            }
            return BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
        }
    }

    private async Task<string> RestoreFileFromSourceCopy(Stream sourceStream, Stream destStream, Action<long>? progressAction = null)
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
            return BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
        }
    }

    private async Task DeleteFile(File? fileToRestore)
    {
        if (_destinationFm == null)
            throw new Exception("Destination file manager not initialized");
        if (fileToRestore == null)
            throw new ArgumentNullException(nameof(fileToRestore));

        var fullPath = _destinationFm.CombinePath(_destinationPath!, fileToRestore.RelativePath!);
        if (fileToRestore.Type == File.FileTyes.Directory) {
            if (await _destinationFm.DirectoryExists(fullPath))
                await _destinationFm.DeleteDirectory(fullPath);
        } else {
            if (await _destinationFm.FileExists(fullPath))
                await _destinationFm.DeleteFile(fullPath);
        }
    }

    #endregion
}