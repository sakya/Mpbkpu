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
        RestoreFiles,
        CopyDatabase
    }

    #region classes
    private class ProgressData
    {
        public DateTime? Started { get; set; }
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
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        BackupFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out BackupPath, Settings.BackupPathUser, Settings.BackupPathPassword, Settings.BackupOauthSettings);
        _destinationFm = FileManagerFactory.GetFileManager(Settings.Destination, out _destinationPath, Settings.DestinationUser, Settings.DestinationPassword, Settings.DestinationOauthSettings);

        Step?.Invoke(this, Steps.OpenDatabase);
        await CheckDestination();
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword, progressAction: OnOpenBackup, token: _cancellationToken)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            try {
                await RunPrimitive(backupFile);
            } catch (Exception ex) {
                LogError(ex);
                throw;
            }
        }

        return new RestoreJobResult()
        {
            Success = true,
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
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null) {
            LogError($"Backup with GUID {Settings.BackupGuid} not found");
            return false;
        }
        if (bkp.Result != Backup.Results.Completed) {
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
                    if (file.Size != item.OriginalSize || file.LastModified != item.OriginalLastModified) {
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
            return false;
        }

        if (ConfirmRestore != null) {
            if (!await ConfirmRestore.Invoke(this, cArgs)) {
                LogInfo("Cancelled by user");
                return false;
            }
        }

        _progressData = new ProgressData()
        {
            Started = DateTime.UtcNow,
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
            Step?.Invoke(this, Steps.DeleteFiles);
            var filesToDelete = backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Restore)
                .OrderByDescending(f => f.Type);
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
                            _progressData.DeleteBytes, _progressData.DeleteTotalBytes, GetEta(_progressData.Started, _progressData.DeleteBytes, _progressData.DeleteTotalBytes)));
                        break;
                    } catch (Exception ex) {
                        if (tryCount == Settings.RetryOnError)
                            throw;

                        LogWarning($"Failed to delete {fileToDelete.FullPath}: {ex.Message}, retrying");
                    }
                }
            }
        }

        _progressData.Started = DateTime.UtcNow;
        Step?.Invoke(this, Steps.RestoreFiles);
        var filesToRestore = backupFile.Files
            .Include(f => f.BackupItem)
            .ThenInclude(i => i!.Volume)
            .Include(f => f.BackupItem)
            .ThenInclude(i => i!.Backup)
            .AsNoTracking()
            .Where(f => f.FileAction == File.FileActions.Restore)
            .OrderByDescending(f => f.Type);
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
                    RestoreProgress?.Invoke(this, new ProgressHandlerArgs(_progressData.RestoreFileIndex, cArgs.FilesToRestore, _progressData.RestoreBytes, _progressData.RestoreTotalBytes, GetEta(_progressData.Started, _progressData.RestoreBytes, _progressData.RestoreTotalBytes)));
                    break;
                } catch (Exception ex) {
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
        if (string.IsNullOrEmpty(Settings.BackupPath))
            throw new ArgumentNullException(nameof(Settings.BackupPath), "Backup path cannot be empty");
        if (string.IsNullOrEmpty(Settings.BackupGuid))
            throw new ArgumentNullException(nameof(Settings.BackupGuid), "Backup GUID cannot be empty");
        if (string.IsNullOrEmpty(Settings.Destination))
            throw new ArgumentNullException(nameof(Settings.BackupPath), "Destination path cannot be empty");
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
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");
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
                if (_destinationFm is FileSystem && fileToRestore.BackupItem!.OriginalLastModified.HasValue) {
                    Directory.SetLastWriteTime(destFilePath, fileToRestore.BackupItem.OriginalLastModified.Value);
                }
                FileProgress?.Invoke(this, fileToRestore.RelativePath!, 9, 9);
                break;
            case File.FileTyes.File:
                DateTime? lastFileProgress = null;
                using (var backupStream = await BackupFm.OpenFile(GetBackupItemPath(BackupFm, BackupPath, fileToRestore.BackupItem))) {
                    using (var destStream = await _destinationFm.OpenFile(destFilePath, true)) {
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
                            throw new Exception($"Hash mismatch for file {fileToRestore.BackupItem.OriginalPath}");
                    }
                }

                // Restore last modified date (only on local file system)
                if (_destinationFm is FileSystem && fileToRestore.BackupItem.OriginalLastModified.HasValue) {
                    System.IO.File.SetLastWriteTime(destFilePath, fileToRestore.BackupItem.OriginalLastModified.Value);
                }
                break;
        }
    }

    private async Task<string> RestoreFileFromSourceDecompress(Stream sourceStream, Stream destStream, Action<long>? progressAction = null)
    {
        using (var hasher = SHA256.Create()) {
            using (CryptoStream hashStream = new CryptoStream(destStream, hasher, CryptoStreamMode.Write, true)) {
                using (var compressorStream = new GZipStream(hashStream, CompressionMode.Decompress, true)) {
                    long copied = 0;
                    int read;
                    byte[] buffer = new byte[32768];
                    while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                        _cancellationToken?.ThrowIfCancellationRequested();
                        await compressorStream.WriteAsync(buffer, 0, read);
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