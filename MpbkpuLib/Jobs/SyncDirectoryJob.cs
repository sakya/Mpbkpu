using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using Newtonsoft.Json;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.Jobs;

public class SyncDirectoryJob : BaseJob
{
    public enum Steps
    {
        ReadSource,
        ReadDestination,
        CalculateFilesToBackup,
        DeleteFiles,
        BackupFiles,
    }

    #region classes
    private class ProgressData
    {
        public DateTime? Started { get; set; }
        public int DeleteTotalFiles { get; set; }
        public int DeleteFileIndex { get; set; }
        public long DeleteTotalBytes { get; set; }
        public long DeleteBytes { get; set; }

        public int BackupTotalFiles { get; set; }
        public int BackupFileIndex { get; set; }
        public long BackupTotalBytes { get; set; }
        public long BackupBytes { get; set; }
    }
    #endregion

    #region private members
    private CancellationToken? _cancellationToken;
    private IFileManager? _sourceFm;
    private string? _sourcePath;
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
        /// <summary>
        /// Number of files that will be deleted from the destination
        /// </summary>
        public int FilesToDelete { get; set; }
        /// <summary>
        /// Number of bytes that will be deleted from the destination
        /// </summary>
        public long BytesToDelete { get; set; }
    }
    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmBackup;

    public delegate void StepHandler(object sender, Steps step);
    public event StepHandler? Step;

    public delegate void ProgressHandler(object sender, ProgressHandlerArgs args);
    public event ProgressHandler? DeleteProgress;
    public event ProgressHandler? Progress;

    public delegate void FileProgressHandler(object sender, string filePath, long bytes, long totalBytes);
    public event FileProgressHandler? FileProgress;
    #endregion

    public SyncDirectoryJob(SyncDirectoryJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new SyncDirectoryJobSettings Settings
    {
        get { return (base.Settings as SyncDirectoryJobSettings)!; }
    }
    #endregion

    public override async Task<BaseJobResult> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        var started = DateTime.UtcNow;
        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        BackupFm = FileManagerFactory.GetFileManager(Settings.Destination, out BackupPath, Settings.DestinationUser, Settings.DestinationPassword, Settings.DestinationOauthSettings);
        _sourceFm = FileManagerFactory.GetFileManager(Settings.Source, out _sourcePath, Settings.SourceUser, Settings.SourcePassword, Settings.SourceOauthSettings);

        var tempDbFile = TempFile.GetTempFileName(".db");
        using var tempFm = FileManagerFactory.GetFileManager(tempDbFile, out _);
        try {
            var backupFile = new BackupFile();
            await backupFile.Open(tempDbFile);
            await RunPrimitive(backupFile);
            backupFile.Close();
        } catch (Exception ex) {
            LogError(ex);
            throw;
        } finally {
            if (await tempFm.FileExists(tempDbFile))
                await tempFm.DeleteFile(tempDbFile);
        }

        return new SyncDirectoryJobResult()
        {
            Success = true,
            Started = started,
            Bytes = _progressData?.BackupBytes,
            Completed = DateTime.UtcNow,
        };
    }

    public override void Dispose()
    {
        _sourceFm?.Dispose();
        base.Dispose();
    }

    protected override void ValidateSettings()
    {
        if (string.IsNullOrEmpty(Settings.Source))
            throw new ArgumentNullException(nameof(Settings.Source), "Source cannot be empty");

        if (string.IsNullOrEmpty(Settings.Destination))
            throw new ArgumentNullException(nameof(Settings.Destination), "Destination cannot be empty");

        if (Settings.Source == Settings.Destination)
            throw new ArgumentException("Source and Destination cannot be the same");

        if (Settings.Destination.StartsWith(Settings.Source))
            throw new ArgumentException("Destination cannot be a subdirectory of Source");

        if (Settings.RetryOnError <= 0)
            throw new ArgumentException("RetryOnError must be greater than zero");
    }

    #region private operations
    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to use</param>
    /// <returns>True on success</returns>
    private async Task RunPrimitive(BackupFile backupFile)
    {
        if (BackupFm == null)
            throw new Exception("Destination file manager not initialized");
        if (_sourceFm == null)
            throw new Exception("Source file manager not initialized");

        // Read source
        Step?.Invoke(this, Steps.ReadSource);
        LogInfo($"Reading source {Settings.Source}");
        await backupFile.EmptyFiles();
        if (!await ReadPathContent(backupFile, _sourceFm, _sourcePath, Settings.IncludeHiddenFiles, File.FileActions.Backup, _cancellationToken)) {
            LogError("Failed to read source");
            return;
        }
        await backupFile.SaveChangesAsync();

        // Read destination
        Step?.Invoke(this, Steps.ReadDestination);
        if (!await BackupFm.DirectoryExists(BackupPath))
            await BackupFm.CreateDirectory(BackupPath);
        LogInfo($"Reading destination {Settings.Destination}");
        if (!await ReadPathContent(backupFile, BackupFm, BackupPath, true, File.FileActions.Delete, _cancellationToken)) {
            LogError("Failed to read source");
            return;
        }
        await backupFile.SaveChangesAsync();

        LogInfo("Checking files to backup");
        Step?.Invoke(this, Steps.CalculateFilesToBackup);
        var sourceItems = backupFile.Files
            .Where(f => f.RootPath == Settings.Source);
        foreach (var sourceItem in sourceItems) {
            var destItem = await backupFile.Files
                .Where(f => f.RelativePath == sourceItem.RelativePath && f.Id != sourceItem.Id)
                .FirstOrDefaultAsync();
            if (destItem != null) {
                // Check if the source file has changed
                if (sourceItem.Type == File.FileTyes.File) {
                    if (Settings.SkipHashCheck) {
                        if (sourceItem.Size != destItem.Size || sourceItem.LastModified > destItem.LastModified) {
                            sourceItem.Modified = true;
                            destItem.FileAction = File.FileActions.None;
                        } else {
                            sourceItem.FileAction = File.FileActions.None;
                            destItem.FileAction = File.FileActions.None;
                        }
                    } else {
                        var sourceHash = await Files.GetSha256(_sourceFm, sourceItem.FullPath, null, _cancellationToken);
                        var destHash = await Files.GetSha256(BackupFm, destItem.FullPath, null, _cancellationToken);
                        if (sourceHash != destHash) {
                            sourceItem.Modified = true;
                            destItem.FileAction = File.FileActions.None;
                        } else {
                            sourceItem.FileAction = File.FileActions.None;
                            destItem.FileAction = File.FileActions.None;
                        }
                    }
                } else {
                    sourceItem.FileAction = File.FileActions.None;
                    destItem.FileAction = File.FileActions.None;
                }
            }
        }
        await backupFile.SaveChangesAsync();

        var cArgs = new ConfirmArgs()
        {
            FilesToBackup = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Backup)
                .CountAsync(),
            BytesToBackup = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Backup)
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
        if (cArgs.FilesToBackup == 0 && cArgs.FilesToDelete == 0) {
            LogInfo("No files to backup or delete");
            return;
        }

        if (ConfirmBackup != null) {
            if (!await ConfirmBackup.Invoke(this, cArgs)) {
                LogInfo("Cancelled by user");
                return;
            }
        }

        _progressData = new ProgressData()
        {
            Started = DateTime.UtcNow,
            BackupTotalFiles = cArgs.FilesToBackup,
            BackupTotalBytes = cArgs.BytesToBackup,
            DeleteTotalFiles = cArgs.FilesToDelete,
            DeleteTotalBytes = cArgs.BytesToDelete
        };

        // Delete files from destination
        if (cArgs.FilesToDelete > 0) {
            Step?.Invoke(this, Steps.DeleteFiles);
            var itemsToDelete = backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Delete)
                .OrderBy(f => f.RelativePath)
                .ThenBy(f => f.Type);
            foreach (var item in itemsToDelete) {
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    return;
                }

                var tryCount = 0;
                while (++tryCount <= Settings.RetryOnError) {
                    try {
                        await DeleteFile(item);

                        _progressData.DeleteBytes += item.Size;
                        _progressData.DeleteFileIndex++;
                        DeleteProgress?.Invoke(this, new ProgressHandlerArgs(_progressData.DeleteFileIndex, _progressData.DeleteTotalFiles,
                            _progressData.DeleteBytes, _progressData.DeleteTotalBytes, GetEta(_progressData.Started, _progressData.DeleteBytes, _progressData.DeleteTotalBytes)));
                        break;
                    } catch (Exception ex) {
                        if (tryCount == Settings.RetryOnError)
                            throw;

                        LogWarning($"Failed to delete {item.FullPath}: {ex.Message}, retrying");
                    }
                }
            }
        }

        // Backup files
        Step?.Invoke(this, Steps.BackupFiles);
        var itemsToBackup = backupFile.Files.AsNoTracking()
            .Where(f => f.FileAction == File.FileActions.Backup)
            .OrderBy(f => f.RelativePath)
            .ThenByDescending(f => f.Type);
        foreach (var item in itemsToBackup) {
            var tryCount = 0;
            while (++tryCount <= Settings.RetryOnError) {
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    return;
                }
                try {
                    await BackupFile(item);

                    _progressData.BackupBytes += item.Size;
                    _progressData.BackupFileIndex++;
                    Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.BackupFileIndex, _progressData.BackupTotalFiles,
                        _progressData.BackupBytes, _progressData.BackupTotalBytes, GetEta(_progressData.Started, _progressData.BackupBytes, _progressData.BackupTotalBytes)));
                    break;
                } catch (Exception ex) {
                    if (tryCount == Settings.RetryOnError)
                        throw;

                    LogWarning($"Failed to backup {item.FullPath}: {ex.Message}, retrying");
                }
            }
        }
    }

    private async Task DeleteFile(File? fileToDelete)
    {
        if (BackupFm == null)
            throw new Exception("Destination file manager not initialized");
        if (fileToDelete == null)
            throw new ArgumentNullException(nameof(fileToDelete));

        var fullPath = BackupFm.CombinePath(BackupPath!, fileToDelete.RelativePath!);
        if (fileToDelete.Type == File.FileTyes.Directory) {
            if (await BackupFm.DirectoryExists(fullPath))
                await BackupFm.DeleteDirectory(fullPath);
        } else {
            if (await BackupFm.FileExists(fullPath))
                await BackupFm.DeleteFile(fullPath);
        }
    }

    private async Task BackupFile(File? fileToBackup)
    {
        if (_sourceFm == null)
            throw new Exception("Source file manager not initialized");
        if (BackupFm == null)
            throw new Exception("Destination file manager not initialized");
        if (fileToBackup == null)
            throw new ArgumentNullException(nameof(fileToBackup));

        var destFilePath = BackupFm.CombinePath(BackupPath!, fileToBackup.RelativePath!);
        switch (fileToBackup.Type) {
            case File.FileTyes.File:
                try {
                    var fInfo = await _sourceFm!.GetFileInfo(fileToBackup.FullPath);
                    if (fInfo.Size != fileToBackup.Size || fInfo.LastModified != fileToBackup.LastModified) {
                        LogWarning($"File {fileToBackup.FullPath} changed after the backup started");
                        fileToBackup.Size = fInfo.Size;
                    }
                } catch (FileNotFoundException) {
                    LogWarning($"File {fileToBackup.FullPath} not found");

                    _progressData!.BackupBytes += fileToBackup.Size;
                    _progressData.BackupFileIndex++;
                    Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.BackupFileIndex, _progressData.BackupTotalFiles, _progressData.BackupBytes, _progressData.BackupTotalBytes, GetEta(_progressData.Started, _progressData.BackupBytes, _progressData.BackupTotalBytes)));
                    return;
                }

                if (await BackupFm.FileExists(destFilePath))
                    await BackupFm.DeleteFile(destFilePath);

                LogDebug($"Backing up {fileToBackup.FullPath} to {destFilePath}");
                FileProgress?.Invoke(this, fileToBackup.RelativePath!, 0, fileToBackup.Size);
                DateTime? lastFileProgress = null;
                using (var sourceStream = await _sourceFm.OpenFile(fileToBackup.FullPath)) {
                    using (var destStream = await BackupFm.OpenFile(destFilePath, true, fileToBackup.Size)) {
                        int read;
                        long copied = 0;
                        var buffer = new byte[32768];
                        while ((read = await sourceStream.ReadAsync(buffer)) > 0) {
                            _cancellationToken?.ThrowIfCancellationRequested();
                            await destStream.WriteAsync(buffer, 0, read);

                            copied += read;
                            if (lastFileProgress == null || copied == fileToBackup.Size ||
                                (DateTime.UtcNow - lastFileProgress.Value).TotalMilliseconds >= 500) {
                                FileProgress?.Invoke(this, fileToBackup.RelativePath!, copied, fileToBackup.Size);
                                Progress?.Invoke(this, new ProgressHandlerArgs(_progressData!.BackupFileIndex, _progressData.BackupTotalFiles, _progressData.BackupBytes + copied, _progressData.BackupTotalBytes, GetEta(_progressData.Started, _progressData.BackupBytes + copied, _progressData.BackupTotalBytes)));
                                lastFileProgress = DateTime.UtcNow;
                            }
                        }
                    }
                }
                // Restore last modified date (only on local file system)
                if (BackupFm is FileSystem && fileToBackup.LastModified.HasValue) {
                    System.IO.File.SetLastWriteTime(destFilePath, fileToBackup.LastModified.Value);
                }
                break;
            case File.FileTyes.Directory:
                FileProgress?.Invoke(this, fileToBackup.RelativePath!, 0, 1);
                await BackupFm.CreateDirectory(destFilePath);
                // Restore last modified date (only on local file system)
                if (BackupFm is FileSystem && fileToBackup.LastModified.HasValue) {
                    Directory.SetLastWriteTime(destFilePath, fileToBackup.LastModified.Value);
                }
                FileProgress?.Invoke(this, fileToBackup.RelativePath!, 0, 1);
                break;
        }
    }
    #endregion
}