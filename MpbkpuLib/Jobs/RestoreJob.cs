using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
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

    public delegate void ProgressHandler(object sender, int file, int totalFiles, long bytes, long totalBytes);
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
    public override async Task<bool> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        Started = DateTime.UtcNow;
        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        BackupFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out BackupPath, Settings.BackupPathUser, Settings.BackupPathPassword);
        _destinationFm = FileManagerFactory.GetFileManager(Settings.Destination, out _destinationPath, Settings.DestinationUser, Settings.DestinationPassword);

        Step?.Invoke(this, Steps.OpenDatabase);
        await CheckDestination();
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword, true)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            try {
                return await RunPrimitive(backupFile);
            } catch (Exception ex) {
                LogError(ex);
                throw;
            } finally {
                Completed = DateTime.UtcNow;
            }
        }
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

        var backupSettings = JsonConvert.DeserializeObject<BackupJobSettings>(bkp.Settings!);
        if (backupSettings == null)
            throw new Exception("Cannot read backup settings");
        if (backupSettings.Source != Settings.Destination) {
            LogWarning($"Restoring to a different destination (was {backupSettings.Source})");
        }

        // Read destination
        Step?.Invoke(this, Steps.ReadDestination);
        LogInfo($"Reading destination {Settings.Destination}");
        if (!ReadPathContent(backupFile, _destinationFm, _destinationPath, _destinationPath, true, File.FileActions.Delete, _cancellationToken).Result) {
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
                if (Settings.SkipMD5Check) {
                    if (file.Size != item.OriginalSize || file.LastModified != item.OriginalLastModified) {
                        file.BackupItemId = item.Id;
                        file.FileAction = File.FileActions.Restore;
                    } else {
                        file.FileAction = File.FileActions.None;
                    }
                } else {
                    var md5 = await Utility.Files.GetMD5(_destinationFm, file.FullPath);
                    if (md5 != item.OriginalMD5) {
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
            DeleteTotalBytes = cArgs.BytesToDelete,
            DeleteTotalFiles = cArgs.FilesToDelete,

            RestoreTotalBytes = cArgs.BytesToRestore,
            RestoreTotalFiles = cArgs.FilesToRestore
        };

        EncryptDecrypt? encrypter = null;
        if (!string.IsNullOrEmpty(Settings.EncryptPassword)) {
            encrypter = new EncryptDecrypt(Settings.EncryptPassword);
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

                int tryCount = 0;
                while (++tryCount <= Settings.RetryOnError) {
                    try {
                        await DeleteFile(fileToDelete);

                        _progressData.DeleteBytes += fileToDelete.Size;
                        DeleteProgress?.Invoke(this, ++_progressData.DeleteFileIndex, _progressData.DeleteTotalFiles,
                            _progressData.DeleteBytes, _progressData.DeleteTotalBytes);
                        break;
                    } catch (Exception ex) {
                        if (tryCount == Settings.RetryOnError)
                            throw;

                        LogWarning($"Failed to delete {fileToDelete.FullPath}: {ex.Message}, retrying");
                    }
                }
            }
        }

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
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                return false;
            }

            int tryCount = 0;
            while (++tryCount <= Settings.RetryOnError) {
                try {
                    await RestoreFile(backupSettings, fileToRestore, encrypter);

                    _progressData.RestoreBytes += fileToRestore.Size;
                    RestoreProgress?.Invoke(this, ++_progressData.RestoreFileIndex, cArgs.FilesToRestore, _progressData.RestoreBytes, _progressData.RestoreTotalBytes);
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

    private async Task RestoreFile(BackupJobSettings backupSettings, File? fileToRestore, EncryptDecrypt? encrypter)
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
                if (!await _destinationFm.DirectoryExists(destFilePath))
                    await _destinationFm.CreateDirectory(destFilePath);

                // Restore last modified date (only on local file system)
                if (_destinationFm is FileSystem && fileToRestore.BackupItem!.OriginalLastModified.HasValue) {
                    Directory.SetLastWriteTime(destFilePath, fileToRestore.BackupItem.OriginalLastModified.Value);
                }
                break;
            case File.FileTyes.File:
                DateTime? lastFileProgress = null;
                using (var backupStream = await BackupFm.OpenFile(BackupFm.CombinePath(BackupPath!, fileToRestore.BackupItem!.Backup!.Guid, fileToRestore.BackupItem.Volume!.Name!, fileToRestore.BackupItem.Name!))) {
                    using (var destStream = await _destinationFm.OpenFile(destFilePath, true)) {
                        var progressAction = new Action<long>((copied) =>
                        {
                            if (lastFileProgress == null || copied == fileToRestore.Size ||
                                (DateTime.UtcNow - lastFileProgress.Value).TotalMilliseconds >= 500) {
                                FileProgress?.Invoke(this, destFilePath, copied, fileToRestore.Size);
                                RestoreProgress?.Invoke(this, _progressData!.RestoreFileIndex, _progressData.RestoreTotalFiles, _progressData.RestoreBytes + copied, _progressData.RestoreTotalBytes);
                                lastFileProgress = DateTime.UtcNow;
                            }
                        });

                        string? md5;
                        if (encrypter != null)
                            md5 = await encrypter.Decrypt(backupStream, destStream, backupSettings.Compress,
                                progressAction);
                        else if (backupSettings.Compress)
                            md5 = await RestoreFileFromSourceDecompress(backupStream, destStream, progressAction);
                        else
                            md5 = await RestoreFileFromSourceCopy(backupStream, destStream, progressAction);

                        if (md5 != fileToRestore.BackupItem.OriginalMD5)
                            throw new Exception($"MD5 mismatch for file {fileToRestore.BackupItem.OriginalPath}");
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
        using (var hasher = MD5.Create()) {
            using (CryptoStream hashStream = new CryptoStream(destStream, hasher, CryptoStreamMode.Write, true)) {
                using (var compressorStream = new GZipStream(hashStream, CompressionLevel.Optimal, true)) {
                    long copied = 0;
                    int read;
                    byte[] buffer = new byte[32768];
                    while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
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
        using (var hasher = MD5.Create()) {
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