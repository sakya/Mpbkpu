using System.IO.Compression;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Channels;
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
        public EtaEstimator? Eta { get; set; }
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

    /// <summary>
    /// What the job wrote to a destination
    /// </summary>
    private class DestinationState
    {
        public DestinationState(DestinationSet.Entry entry)
        {
            Entry = entry;
            Limit = Math.Max(1, entry.Fm.MaxParallelTransfers);
        }

        public DestinationSet.Entry Entry { get; }
        /// <summary>
        /// Serializes the creation of the backup directory and of the volumes
        /// </summary>
        public SemaphoreSlim Lock { get; } = new(1, 1);
        /// <summary>
        /// Files being copied to the destination and maximum number of files copied at the same time
        /// </summary>
        public int Active { get; set; }
        public int Limit { get; }
        public LastUsedVolume? LastUsedVolume { get; set; }
        /// <summary>
        /// Bytes assigned to the destination in this run (to balance the destinations)
        /// </summary>
        public long AssignedBytes { get; set; }
        /// <summary>
        /// True when the job created the directory of the backup in the destination
        /// </summary>
        public bool BackupDirectoryCreated { get; set; }
    }
    #endregion

    #region private members
    private Backup? _backup;
    /// <summary>
    /// The database is used by the copy workers one at a time
    /// </summary>
    private readonly SemaphoreSlim _databaseLock = new(1, 1);
    /// <summary>
    /// Cancelled by the user or when a copy worker fails
    /// </summary>
    private CancellationToken _copyToken;
    private List<DestinationState> _states = new();
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
        LogInfo(SerializeSettings());

        _states = CreateBackupDestinations(Settings.Destinations).Entries.Select(e => new DestinationState(e)).ToList();
        // The destination directories must exist before the database is searched in them
        await CheckDestinations();
        await ReadDestinationsSpace();
        _sourceFm = FileManagerFactory.GetFileManager(Settings.Source, out _sourcePath, Settings.SourceUser, Settings.SourcePassword, Settings.SourceOauthSettings);

        LogInfo("Opening database");
        Step?.Invoke(this, Steps.OpenDatabase);
        using (var openRes = await BackupDestinations!.OpenDatabase(Settings.EncryptPassword, true, OnOpenBackup, _cancellationToken)) {
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
                        if (!await SaveDatabaseToDestinations(openRes.TempFilePath) && _result == Backup.Results.Completed)
                            _result = Backup.Results.CompletedWithWarnings;
                        persisted = true;
                    }
                }
            } catch (Exception ex) {
                LogError(ex);
                if (!persisted)
                    await DeleteOrphanBackupDirectories();
                throw;
            }
        }

        if (_chainsToDelete.Count > 0)
            await DeleteOldChains();
        // The real space after the backup (compression and deletions make the estimate approximate)
        await ReadDestinationsSpace();

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
        if (BackupDestinations == null)
            throw new Exception("Destinations not initialized");
        if (_sourceFm == null)
            throw new Exception("Source file manager not initialized");

        await BackupDestinations.Register(backupFile);
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
            Settings = SerializeSettings(),
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
                            if (sourceFile.Size != item.OriginalSize || !Files.SameModificationTime(sourceFile.LastModified, item.OriginalLastModified)) {
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

        // Backup files
        LogInfo("Backing up files");
        Step?.Invoke(this, Steps.BackupFiles);
        _progressData = new ProgressData()
        {
            Eta = StartEta(cArgs.BytesToBackup, cArgs.FilesToBackup, _states.Select(s => s.Entry.Fm).Prepend(_sourceFm).ToArray()),
            TotalBytes = cArgs.BytesToBackup,
            TotalFiles = cArgs.FilesToBackup
        };

        // A reader pages the files from the database, the workers copy them
        var workers = GetWorkerCount();
        LogInfo($"Copying up to {workers} files at the same time");
        using (var copyCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken ?? CancellationToken.None)) {
            _copyToken = copyCts.Token;
            var channel = Channel.CreateBounded<File>(workers * 2);
            var tasks = new List<Task> { RunCopyTask(copyCts, () => ReadFilesToBackup(backupFile, channel.Writer)) };
            for (var i = 0; i < workers; i++)
                tasks.Add(RunCopyTask(copyCts, () => CopyFiles(backupFile, channel.Reader)));
            try {
                await Task.WhenAll(tasks);
            } catch {
                // After the first error the other tasks only report the cancellation
                var error = tasks.Where(t => t.IsFaulted)
                    .SelectMany(t => t.Exception!.InnerExceptions)
                    .FirstOrDefault(e => e is not OperationCanceledException);
                if (error != null)
                    ExceptionDispatchInfo.Capture(error).Throw();
                if (_cancellationToken?.IsCancellationRequested != true)
                    throw;
            }
        }

        if (_cancellationToken?.IsCancellationRequested == true) {
            LogInfo("Cancelled by user");
            if (!await SetBackupResult(backupFile, Backup.Results.Cancelled))
                LogWarning("Cannot set backup result");
            return;
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
    /// Number of files copied at the same time: <see cref="BackupJobSettings.ParallelFiles"/> limited by the source and
    /// by the destinations. The workers are never more than the free places of the destinations, so a worker always
    /// finds a destination (<see cref="AcquireDestination"/>).
    /// </summary>
    private int GetWorkerCount()
    {
        var destinations = _states.Sum(s => (long)s.Limit);
        return (int)Math.Min(Settings.ParallelFiles, Math.Min(destinations, _sourceFm!.MaxParallelTransfers));
    }

    /// <summary>
    /// Run a task of the copy, cancelling the others when it fails
    /// </summary>
    private static Task RunCopyTask(CancellationTokenSource copyCts, Func<Task> task)
    {
        return Task.Run(async () =>
        {
            try {
                await task();
            } catch {
                copyCts.Cancel();
                throw;
            }
        });
    }

    /// <summary>
    /// Read the files to backup or to record as deleted, a page at a time
    /// </summary>
    private async Task ReadFilesToBackup(BackupFile backupFile, ChannelWriter<File> writer)
    {
        try {
            var lastId = 0;
            while (true) {
                var page = await WithDatabase(() => backupFile.Files.AsNoTracking()
                    .Where(f => (f.FileAction == File.FileActions.Backup || f.FileAction == File.FileActions.Delete) && f.Id > lastId)
                    .OrderBy(f => f.Id)
                    .Take(500)
                    .ToListAsync());
                if (page.Count == 0)
                    break;
                foreach (var file in page)
                    await writer.WriteAsync(file, _copyToken);
                lastId = page[^1].Id;
            }
        } finally {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// A copy worker
    /// </summary>
    private async Task CopyFiles(BackupFile backupFile, ChannelReader<File> reader)
    {
        // One per worker: an instance must not be used by concurrent copies
        var encryptor = string.IsNullOrEmpty(Settings.EncryptPassword) ? null : new EncryptDecrypt(Settings.EncryptPassword);
        await foreach (var file in reader.ReadAllAsync(_copyToken)) {
            if (file.FileAction == File.FileActions.Backup)
                await CopyFile(backupFile, file, encryptor);
            else
                await WithDatabase(() => DeleteItem(backupFile, file));
        }
    }

    /// <summary>
    /// Backup a file to a destination, retrying on errors
    /// </summary>
    private async Task CopyFile(BackupFile backupFile, File fileToBackup, EncryptDecrypt? encryptor)
    {
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
                AddProgress(fileToBackup.Size, 1);
                return;
            }
        }

        // Every try goes to the same destination
        var destination = AcquireDestination(Files.GetDestinationFileSize(fileToBackup.Size, Settings.CompressionLevel, encryptor != null) ?? fileToBackup.Size);
        try {
            var tryCount = 0;
            while (++tryCount <= Settings.RetryOnError) {
                if (_copyToken.IsCancellationRequested)
                    return;

                // Bytes of this try already counted in the progress
                var copied = new StrongBox<long>();
                try {
                    // Compute file size of the backup file if possible
                    long? backupFileSize = Files.GetDestinationFileSize(fileToBackup.Size, Settings.CompressionLevel, encryptor != null);
                    // On the last try accept a file changed during the copy (with a warning)
                    await BackupFileFromSource(backupFile, destination, fileToBackup, encryptor, backupFileSize, tryCount == Settings.RetryOnError, copied);
                    DestinationSpaceUsed(destination, backupFileSize ?? fileToBackup.Size);
                    AddProgress(fileToBackup.Size - copied.Value, 1);
                    return;
                } catch (OperationCanceledException) when (_copyToken.IsCancellationRequested) {
                    AddProgress(-copied.Value, 0);
                    return;
                } catch (Exception ex) when (tryCount == Settings.RetryOnError && IsSourceAccessError(ex)) {
                    // A locked or protected file must not abort the whole backup
                    await AddWarning(backupFile, $"File {fileToBackup.FullPath} has not been backed up: {ex.Message}");
                    AddProgress(fileToBackup.Size - copied.Value, 1);
                    return;
                } catch (Exception ex) {
                    AddProgress(-copied.Value, 0);
                    if (tryCount == Settings.RetryOnError)
                        throw;

                    LogWarning($"Failed to backup {fileToBackup.FullPath}: {ex.Message}, retrying");
                }
            }
        } finally {
            ReleaseDestination(destination);
        }
    }

    /// <summary>
    /// Run an operation on the database, one at a time
    /// </summary>
    private async Task<T> WithDatabase<T>(Func<Task<T>> operation)
    {
        await _databaseLock.WaitAsync();
        try {
            return await operation();
        } finally {
            _databaseLock.Release();
        }
    }

    /// <inheritdoc cref="WithDatabase{T}"/>
    private Task WithDatabase(Func<Task> operation)
    {
        return WithDatabase(async () =>
        {
            await operation();
            return true;
        });
    }

    /// <summary>
    /// Add copied bytes and files to the progress
    /// </summary>
    /// <param name="bytes">Bytes copied (negative to remove the bytes of a failed try)</param>
    /// <param name="files">Files completed</param>
    /// <param name="notify">False to update the counters without raising <see cref="Progress"/></param>
    private void AddProgress(long bytes, int files, bool notify = true)
    {
        lock (_progressData!) {
            _progressData.Bytes += bytes;
            _progressData.FileIndex += files;
            if (notify) {
                Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.FileIndex, _progressData.TotalFiles, _progressData.Bytes,
                    _progressData.TotalBytes, _progressData.Eta!.Update(_progressData.Bytes, _progressData.FileIndex)));
            }
        }
    }

    /// <summary>
    /// Raise <see cref="FileProgress"/> (one event at a time: the files are copied at the same time)
    /// </summary>
    private void ReportFileProgress(string filePath, long bytes, long totalBytes)
    {
        lock (_progressData!) {
            FileProgress?.Invoke(this, filePath, bytes, totalBytes);
        }
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

        DestinationSet.Validate(Settings.Destinations, nameof(Settings.Destinations));
        foreach (var destination in Settings.Destinations) {
            if (Utility.Files.IsSameOrSubPath(Settings.Source, destination.Path)) {
                throw new ArgumentException(Utility.Files.IsSameOrSubPath(destination.Path, Settings.Source) ?
                    "Source and Destination cannot be the same" :
                    "Destination cannot be a subdirectory of Source");
            }
        }

        if (Settings.ParallelFiles <= 0)
            throw new ArgumentException("ParallelFiles must be greater than zero");

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
    /// Check the destination directories, creating the missing ones
    /// </summary>
    private async Task CheckDestinations()
    {
        foreach (var state in _states) {
            var fm = state.Entry.Fm;
            if (Settings.CompressionLevel != CompressionLevel.NoCompression && !fm.SupportCompression)
                throw new Exception($"Destination {DestinationSet.MaskedPath(state.Entry)} doesn't support compression");

            if (!await fm.DirectoryExists(state.Entry.Path))
                await fm.CreateDirectory(state.Entry.Path);
        }
    }

    /// <summary>
    /// Read the space of every destination. Never fails the job.
    /// </summary>
    private async Task ReadDestinationsSpace()
    {
        foreach (var state in _states)
            await ReadSpace(state.Entry.Space, state.Entry.Fm, state.Entry.Path);
        OnSpaceChanged(SpaceEstimate.Sum(_states.Select(s => s.Entry.Space)));
    }

    /// <summary>
    /// Update the estimated space of a destination after writing a file
    /// </summary>
    private void DestinationSpaceUsed(DestinationState destination, long bytes)
    {
        lock (_states) {
            destination.Entry.Space.Used(bytes);
            OnSpaceChanged(SpaceEstimate.Sum(_states.Select(s => s.Entry.Space)));
        }
    }

    /// <summary>
    /// Choose the destination of a file among the ones with a free place (<see cref="GetWorkerCount"/>): the one with
    /// the fewest bytes assigned in this run among the ones with enough estimated space (unknown space counts as
    /// enough). When none has enough space the one with the most space is chosen anyway: the estimate is approximate
    /// and the file manager reports a real lack of space. Release it with <see cref="ReleaseDestination"/>.
    /// </summary>
    private DestinationState AcquireDestination(long size)
    {
        lock (_states) {
            var free = _states.Where(s => s.Active < s.Limit).ToList();
            var res = free.Where(s => s.Entry.Space.Current == null || s.Entry.Space.Current.Available >= size)
                          .MinBy(s => s.AssignedBytes) ??
                      free.MaxBy(s => s.Entry.Space.Current!.Available)!;
            res.AssignedBytes += size;
            res.Active++;
            return res;
        }
    }

    private void ReleaseDestination(DestinationState destination)
    {
        lock (_states) {
            destination.Active--;
        }
    }

    /// <summary>
    /// Backup a <see cref="DataModel.File"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/></param>
    /// <param name="destination">The destination of the file</param>
    /// <param name="sourceFile">The <see cref="DataModel.File"/> to backup</param>
    /// <param name="encryptor">The optional <see cref="Utility.EncryptDecrypt"/> to encrypt the file</param>
    /// <param name="backupFileSize">The size of the backup file, if known</param>
    /// <param name="acceptChanges">If false a file changed during the copy throws an exception (to retry)</param>
    /// <param name="copied">Updated with the bytes added to the progress</param>
    /// <exception cref="ArgumentNullException"></exception>
    private async Task BackupFileFromSource(BackupFile? backupFile,
        DestinationState destination,
        File? sourceFile,
        EncryptDecrypt? encryptor,
        long? backupFileSize,
        bool acceptChanges,
        StrongBox<long> copied)
    {
        if (_sourceFm == null)
            throw new Exception("Source file manager not initialized");
        if (backupFile == null)
            throw new ArgumentNullException(nameof(backupFile));
        if (sourceFile == null)
            throw new ArgumentNullException(nameof(sourceFile));

        var (backupItem, volumeName) = await GetNextBackupItem(backupFile, destination);
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

        var destFm = destination.Entry.Fm;
        var destFilePath = destFm.CombinePath(destination.Entry.Path, _backup.Guid, volumeName, backupItem.Name!);
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
                    ReportFileProgress(sourceFile.RelativePath!, 0, size);
                    using (var sourceStream = new MemoryStream(Encoding.ASCII.GetBytes("DIRECTORY"))) {
                        await using (var destStream = await destFm.OpenFile(destFilePath, true, size)) {
                            await sourceStream.CopyToAsync(destStream);
                        }
                    }

                    ReportFileProgress(sourceFile.RelativePath!, size, size);
                    break;
                case File.FileTyes.File:
                    LogDebug($"Backing up {sourceFile.FullPath} to {destFilePath}");
                    ReportFileProgress(sourceFile.RelativePath!, 0, sourceFile.Size);
                    DateTime? lastFileProgress = null;
                    using (var sourceStream = await _sourceFm.OpenFile(sourceFile.FullPath)) {
                        using (var destStream = await destFm.OpenFile(destFilePath, true, backupFileSize)) {
                            var progressAction = new Action<long>((bytes) =>
                            {
                                var notify = lastFileProgress == null || bytes == sourceFile.Size ||
                                             (DateTime.UtcNow - lastFileProgress.Value).TotalMilliseconds >= 500;
                                AddProgress(bytes - copied.Value, 0, notify);
                                copied.Value = bytes;
                                if (notify) {
                                    ReportFileProgress(sourceFile.RelativePath!, bytes, sourceFile.Size);
                                    lastFileProgress = DateTime.UtcNow;
                                }
                            });

                            if (encryptor != null)
                                await encryptor.Encrypt(backupItem, sourceStream, destStream, Settings.CompressionLevel,
                                    progressAction, _copyToken);
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
            await TryDeleteFile(destFm, destFilePath);
            throw;
        }

        await WithDatabase(async () =>
        {
            backupFile.BackupItems.Add(backupItem);
            await backupFile.SaveChangesAsync();
        });
    }

    private async Task BackupFileFromSourceCopy(BackupItem backupItem, Stream sourceStream, Stream destStream, Action<long>? progressAction = null)
    {
        using (var hasher = SHA256.Create()) {
            using (CryptoStream hashStream = new CryptoStream(destStream, hasher, CryptoStreamMode.Write, true)) {
                long copied = 0;
                int read;
                byte[] buffer = new byte[32768];
                while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    _copyToken.ThrowIfCancellationRequested();
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
                            _copyToken.ThrowIfCancellationRequested();
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
    /// Get the next <see cref="DataModel.BackupItem"/> of a destination, creating the backup directory on the first
    /// item and a new <see cref="BackupVolume"/> when the last one is full. Volumes belong to this backup only, so
    /// they are counted in memory.
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to backup</param>
    /// <param name="destination">The destination of the item</param>
    /// <returns>A new <see cref="DataModel.BackupItem"/> and the name of its volume</returns>
    private async Task<(BackupItem Item, string VolumeName)> GetNextBackupItem(BackupFile backupFile, DestinationState destination)
    {
        var fm = destination.Entry.Fm;
        await destination.Lock.WaitAsync(_copyToken);
        try {
            if (!destination.BackupDirectoryCreated) {
                var backupDirectory = fm.CombinePath(destination.Entry.Path, _backup!.Guid);
                if (await fm.DirectoryExists(backupDirectory))
                    throw new Exception($"The backup destination {backupDirectory} exist");
                await fm.CreateDirectory(backupDirectory);
                destination.BackupDirectoryCreated = true;
            }

            var volume = destination.LastUsedVolume;
            if (volume == null || volume.ItemsCount >= Settings.MaxFilesPerDirectory) {
                var record = new BackupVolume()
                {
                    BackupId = _backup!.Id,
                    Name = Guid.NewGuid().ToString("N"),
                    DestinationId = destination.Entry.RecordId,
                };
                await WithDatabase(async () =>
                {
                    backupFile.BackupVolumes.Add(record);
                    await backupFile.SaveChangesAsync();
                });
                await fm.CreateDirectory(fm.CombinePath(destination.Entry.Path, _backup.Guid, record.Name));
                destination.LastUsedVolume = volume = new LastUsedVolume()
                {
                    Id = record.Id,
                    Name = record.Name,
                    ItemsCount = 0
                };
            }

            volume.ItemsCount++;
            return (new BackupItem()
            {
                Name = Guid.NewGuid().ToString("N"),
                VolumeId = volume.Id!.Value
            }, volume.Name!);
        } finally {
            destination.Lock.Release();
        }
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
                    BackupDestinations = Settings.Destinations,
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
    /// Delete the files of a backup not referenced by the database in any destination
    /// </summary>
    private async Task DeleteOrphanBackupDirectories()
    {
        if (_backup == null)
            return;

        foreach (var state in _states.Where(s => s.BackupDirectoryCreated)) {
            var fm = state.Entry.Fm;
            var backupDirectory = fm.CombinePath(state.Entry.Path, _backup.Guid);
            try {
                if (await fm.DirectoryExists(backupDirectory)) {
                    LogWarning($"Deleting the files of the failed backup {backupDirectory}");
                    await DeleteDirectoryRecursive(fm, backupDirectory);
                }
            } catch (Exception ex) {
                LogWarning($"Cannot delete {backupDirectory}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Log a warning and save it in the backup
    /// </summary>
    private async Task AddWarning(BackupFile backupFile, string message)
    {
        LogWarning(message);
        await WithDatabase(async () =>
        {
            _hasWarnings = true;
            backupFile.BackupWarnings.Add(new BackupWarning()
            {
                BackupId = _backup!.Id,
                Message = message.Length > 2048 ? message[..2048] : message
            });
            await backupFile.SaveChangesAsync();
        });
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