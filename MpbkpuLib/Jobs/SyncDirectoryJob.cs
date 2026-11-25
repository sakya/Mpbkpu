using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
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
        public EtaEstimator? Eta { get; set; }
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
    private bool _hasWarnings;
    #endregion

    /// <summary>
    /// Suffix of the temporary file written in the destination before replacing a file
    /// </summary>
    private const string TempFileSuffix = ".mpbkpu-tmp";

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
        LogInfo(SerializeSettings());

        BackupFm = FileManagerFactory.GetFileManager(Settings.Destination, out BackupPath, Settings.DestinationUser, Settings.DestinationPassword, Settings.DestinationOauthSettings);
        _sourceFm = FileManagerFactory.GetFileManager(Settings.Source, out _sourcePath, Settings.SourceUser, Settings.SourcePassword, Settings.SourceOauthSettings);

        var tempDbFile = TempFile.GetTempFileName(".db");
        using var tempFm = FileManagerFactory.GetFileManager(tempDbFile, out _);
        Backup.Results? result;
        try {
            var backupFile = new BackupFile();
            await backupFile.Open(tempDbFile);
            result = await RunPrimitive(backupFile);
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
            Success = result != Backup.Results.Cancelled,
            Result = result,
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

        if (Utility.Files.IsSameOrSubPath(Settings.Source, Settings.Destination)) {
            throw new ArgumentException(Utility.Files.IsSameOrSubPath(Settings.Destination, Settings.Source) ?
                "Source and Destination cannot be the same" :
                "Destination cannot be a subdirectory of Source");
        }

        if (Settings.RetryOnError <= 0)
            throw new ArgumentException("RetryOnError must be greater than zero");
    }

    #region private operations
    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to use</param>
    /// <returns>The result, null if there was nothing to do</returns>
    private async Task<Backup.Results?> RunPrimitive(BackupFile backupFile)
    {
        if (BackupFm == null)
            throw new Exception("Destination file manager not initialized");
        if (_sourceFm == null)
            throw new Exception("Source file manager not initialized");

        // Read source
        Step?.Invoke(this, Steps.ReadSource);
        LogInfo($"Reading source {Settings.Source}");
        await backupFile.EmptyFiles();
        await ReadPathContent(backupFile, _sourceFm, _sourcePath, Settings.IncludeHiddenFiles, File.FileActions.Backup, _cancellationToken, Settings.Exclude);
        await backupFile.SaveChangesAsync();
        // A partial read of the source would delete files from the destination
        if (_cancellationToken?.IsCancellationRequested == true)
            return Cancelled();

        // Read destination: hidden and excluded items are read only if they are read from the source too,
        // so items ignored in the source are never deleted from the destination
        Step?.Invoke(this, Steps.ReadDestination);
        if (!await BackupFm.DirectoryExists(BackupPath))
            await BackupFm.CreateDirectory(BackupPath);
        LogInfo($"Reading destination {Settings.Destination}");
        await ReadPathContent(backupFile, BackupFm, BackupPath, Settings.IncludeHiddenFiles, File.FileActions.Delete, _cancellationToken, Settings.Exclude);
        await backupFile.SaveChangesAsync();
        if (_cancellationToken?.IsCancellationRequested == true)
            return Cancelled();
        if (UnreadableDirectories.Count > 0)
            _hasWarnings = true;

        LogInfo("Checking files to backup");
        Step?.Invoke(this, Steps.CalculateFilesToBackup);
        await CalculateActions(backupFile);
        await backupFile.SaveChangesAsync();
        if (_cancellationToken?.IsCancellationRequested == true)
            return Cancelled();

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
            return _hasWarnings ? Backup.Results.CompletedWithWarnings : null;
        }

        if (ConfirmBackup != null) {
            if (!await ConfirmBackup.Invoke(this, cArgs))
                return Cancelled();
        }

        _progressData = new ProgressData()
        {
            Eta = StartEta(0, cArgs.FilesToDelete, BackupFm),
            BackupTotalFiles = cArgs.FilesToBackup,
            BackupTotalBytes = cArgs.BytesToBackup,
            DeleteTotalFiles = cArgs.FilesToDelete,
            DeleteTotalBytes = cArgs.BytesToDelete
        };

        // Delete files from destination
        if (cArgs.FilesToDelete > 0) {
            Step?.Invoke(this, Steps.DeleteFiles);
            var itemsToDelete = await backupFile.Files.AsNoTracking()
                .Where(f => f.FileAction == File.FileActions.Delete)
                .OrderBy(f => f.RelativePath)
                .ThenBy(f => f.Type)
                .ToListAsync();
            foreach (var item in itemsToDelete) {
                if (_cancellationToken?.IsCancellationRequested == true)
                    return Cancelled();

                await WithRetry(item, "delete", () => DeleteFile(item));
                _progressData.DeleteBytes += item.Size;
                _progressData.DeleteFileIndex++;
                DeleteProgress?.Invoke(this, new ProgressHandlerArgs(_progressData.DeleteFileIndex, _progressData.DeleteTotalFiles,
                    _progressData.DeleteBytes, _progressData.DeleteTotalBytes, _progressData.Eta!.Update(0, _progressData.DeleteFileIndex)));
            }
        }

        // Backup files: the time left of this phase does not include the deletions
        _progressData.Eta = StartEta(cArgs.BytesToBackup, cArgs.FilesToBackup, _sourceFm, BackupFm);
        Step?.Invoke(this, Steps.BackupFiles);
        var itemsToBackup = await backupFile.Files.AsNoTracking()
            .Where(f => f.FileAction == File.FileActions.Backup)
            .OrderBy(f => f.RelativePath)
            .ThenByDescending(f => f.Type)
            .ToListAsync();
        foreach (var item in itemsToBackup) {
            if (_cancellationToken?.IsCancellationRequested == true)
                return Cancelled();

            try {
                await WithRetry(item, "backup", () => BackupFile(item));
            } catch (Exception) when (_cancellationToken?.IsCancellationRequested == true) {
                // Also an aborted upload of a file manager when the copy is cancelled
                return Cancelled();
            }
            _progressData.BackupBytes += item.Size;
            _progressData.BackupFileIndex++;
            Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.BackupFileIndex, _progressData.BackupTotalFiles,
                _progressData.BackupBytes, _progressData.BackupTotalBytes, _progressData.Eta!.Update(_progressData.BackupBytes, _progressData.BackupFileIndex)));
        }

        return _hasWarnings ? Backup.Results.CompletedWithWarnings : Backup.Results.Completed;
    }

    private Backup.Results Cancelled()
    {
        LogInfo("Cancelled by user");
        return Backup.Results.Cancelled;
    }

    /// <summary>
    /// Compare source and destination items and set the action of each one:
    /// unchanged items are left alone, changed files are copied, items missing in the source are deleted
    /// </summary>
    private async Task CalculateActions(BackupFile backupFile)
    {
        // Source and destination can use different path separators: compare portable relative paths
        var destItems = new Dictionary<string, File>(StringComparer.Ordinal);
        foreach (var d in await backupFile.Files.Where(f => f.FileAction == File.FileActions.Delete).ToListAsync())
            destItems[ToPortablePath(d.RelativePath!, BackupFm!.PathSeparator)] = d;
        var sourceItems = await backupFile.Files.Where(f => f.FileAction == File.FileActions.Backup).ToListAsync();

        foreach (var sourceItem in sourceItems) {
            if (_cancellationToken?.IsCancellationRequested == true)
                return;
            if (!destItems.TryGetValue(ToPortablePath(sourceItem.RelativePath!, _sourceFm!.PathSeparator), out var destItem))
                continue;
            // A file replaced by a directory (or vice versa): delete the destination item, then copy the source one
            if (sourceItem.Type != destItem.Type)
                continue;

            destItem.FileAction = File.FileActions.None;
            if (sourceItem.Type == File.FileTyes.File && await IsChanged(sourceItem, destItem))
                sourceItem.Modified = true;
            else
                sourceItem.FileAction = File.FileActions.None;
        }

        // Never delete the content of a source directory that could not be read
        var unreadable = UnreadableDirectories.Select(d => $"{ToPortablePath(d, _sourceFm!.PathSeparator)}/").ToList();
        foreach (var (path, destItem) in destItems) {
            if (destItem.FileAction == File.FileActions.Delete && unreadable.Any(u => path.StartsWith(u, StringComparison.Ordinal))) {
                LogWarning($"Not deleting {destItem.FullPath}: the source directory could not be read");
                destItem.FileAction = File.FileActions.None;
            }
        }
    }

    private async Task<bool> IsChanged(File sourceItem, File destItem)
    {
        if (Settings.SkipHashCheck)
            return sourceItem.Size != destItem.Size || sourceItem.LastModified > destItem.LastModified;

        var sourceHash = await Files.GetSha256(_sourceFm!, sourceItem.FullPath, null, _cancellationToken);
        var destHash = await Files.GetSha256(BackupFm!, destItem.FullPath, null, _cancellationToken);
        return sourceHash != destHash;
    }

    private async Task WithRetry(File item, string operation, Func<Task> action)
    {
        var tryCount = 0;
        while (true) {
            try {
                await action();
                return;
            } catch (Exception ex) when (ex is not OperationCanceledException && _cancellationToken?.IsCancellationRequested != true &&
                                         ++tryCount < Settings.RetryOnError) {
                LogWarning($"Failed to {operation} {item.FullPath}: {ex.Message}, retrying");
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
            // Not all the file managers delete non empty directories
            if (await BackupFm.DirectoryExists(fullPath))
                await DeleteDirectoryRecursive(BackupFm, fullPath);
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
                        LogWarning($"File {fileToBackup.FullPath} changed after the synchronization started");
                        _hasWarnings = true;
                        fileToBackup.Size = fInfo.Size;
                    }
                } catch (FileNotFoundException) {
                    LogWarning($"File {fileToBackup.FullPath} not found");
                    _hasWarnings = true;
                    return;
                }

                // Write a temporary file and replace the destination only when the copy is complete:
                // an interrupted copy never leaves a truncated file in place of a good one
                var tempFilePath = $"{destFilePath}{TempFileSuffix}";
                if (await BackupFm.FileExists(tempFilePath))
                    await BackupFm.DeleteFile(tempFilePath);

                LogDebug($"Backing up {fileToBackup.FullPath} to {destFilePath}");
                FileProgress?.Invoke(this, fileToBackup.RelativePath!, 0, fileToBackup.Size);
                DateTime? lastFileProgress = null;
                using (var sourceStream = await _sourceFm.OpenFile(fileToBackup.FullPath)) {
                    using (var destStream = await BackupFm.OpenFile(tempFilePath, true, fileToBackup.Size)) {
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
                                Progress?.Invoke(this, new ProgressHandlerArgs(_progressData!.BackupFileIndex, _progressData.BackupTotalFiles, _progressData.BackupBytes + copied, _progressData.BackupTotalBytes, _progressData.Eta!.Update(_progressData.BackupBytes + copied, _progressData.BackupFileIndex)));
                                lastFileProgress = DateTime.UtcNow;
                            }
                        }
                    }
                }
                if (await BackupFm.FileExists(destFilePath))
                    await BackupFm.DeleteFile(destFilePath);
                await BackupFm.RenameFile(tempFilePath, Path.GetFileName(ToPortablePath(fileToBackup.RelativePath!, _sourceFm.PathSeparator)));
                // Restore last modified date (only on local file system)
                if (BackupFm is BaseLocalFileSystem && fileToBackup.LastModified.HasValue) {
                    System.IO.File.SetLastWriteTimeUtc(destFilePath, fileToBackup.LastModified.Value);
                }
                break;
            case File.FileTyes.Directory:
                FileProgress?.Invoke(this, fileToBackup.RelativePath!, 0, 1);
                await BackupFm.CreateDirectory(destFilePath);
                // Restore last modified date (only on local file system)
                if (BackupFm is BaseLocalFileSystem && fileToBackup.LastModified.HasValue) {
                    Directory.SetLastWriteTimeUtc(destFilePath, fileToBackup.LastModified.Value);
                }
                FileProgress?.Invoke(this, fileToBackup.RelativePath!, 0, 1);
                break;
        }
    }
    #endregion
}