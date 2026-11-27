using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using Newtonsoft.Json;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.Jobs;

public class VerifyJob: BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        VerifyFiles
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
    #endregion

    #region private members
    private CancellationToken? _cancellationToken;
    private List<VerifyJobResult.Error>? _errors;
    private ProgressData? _progressData;
    #endregion

    #region delegates
    public delegate void StepHandler(object sender, Steps step);
    public event StepHandler? Step;

    public delegate void ProgressHandler(object sender, ProgressHandlerArgs args);
    public event ProgressHandler? Progress;

    public delegate void FileProgressHandler(object sender, string filePath, long bytes, long totalBytes);
    public event FileProgressHandler? FileProgress;
    #endregion

    public VerifyJob(VerifyJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new VerifyJobSettings Settings
    {
        get { return (base.Settings as VerifyJobSettings)!; }
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

        LogInfo("Opening database");
        Step?.Invoke(this, Steps.OpenDatabase);
        VerifyJobResult? res;
        using (var openRes = await OpenBackupDestinations(Settings.BackupDestinations, false, _cancellationToken)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            var transaction = backupFile.BeginTransaction();

            try {
                res = await RunPrimitive(backupFile);
                await transaction.CommitAsync();
            } catch (Exception ex) {
                LogError(ex);
                await transaction.RollbackAsync();
                throw;
            } finally {
                transaction.Dispose();
            }
        }

        res.Started = started;
        res.Completed = DateTime.UtcNow;
        res.Bytes = _progressData!.Bytes;
        res.Errors = _errors;
        // A backup that does not pass the verification is a failure (CLI exit code, GUI message)
        if (res.Success && _errors?.Count > 0) {
            res.Success = false;
            res.ErrorMessage = $"{_errors.Count} verification errors: {string.Join("; ", _errors.Take(3).Select(e => e.Message))}";
        }
        return res;
    }
    #endregion

    #region private operations

    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to use</param>
    /// <returns>True on success</returns>
    private async Task<VerifyJobResult> RunPrimitive(BackupFile backupFile)
    {
        _errors = new List<VerifyJobResult.Error>();
        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null) {
            _errors.Add(new VerifyJobResult.Error(VerifyJobResult.Error.ErrorCodes.BackupNotFound, $"Backup with GUID {Settings.BackupGuid} not found"));
            LogError($"Backup with GUID {Settings.BackupGuid} not found");
            return new VerifyJobResult()
            {
                Success = false,
                ErrorMessage = $"Backup with GUID {Settings.BackupGuid} not found"
            };
        }

        if (bkp.Result is not (Backup.Results.Completed or Backup.Results.CompletedWithWarnings))
            LogWarning($"Database result is { bkp.Result }");

        var bkpItemsCount = 0;
        long bkpSize = 0;
        await foreach (var i in backupFile.GetBackupItems(bkp.Id)) {
            bkpItemsCount++;
            bkpSize += i.Size;
        }

        LogInfo($"Type     : { bkp.Type }");
        LogInfo($"Started  : { bkp.StartedAt!.Value.ToLocalTime().ToShortDateString() } { bkp.StartedAt!.Value.ToLocalTime().ToShortTimeString() }");
        if (bkp.CompletedAt.HasValue)
            LogInfo($"Completed: { bkp.CompletedAt!.Value.ToLocalTime().ToShortDateString() } { bkp.CompletedAt!.Value.ToLocalTime().ToShortTimeString() }");
        LogInfo($"Size     : { Utility.String.BytesToString(bkpSize) }");

        var settings = JsonConvert.DeserializeObject<BackupJobSettings>(bkp.Settings!);
        if (settings == null)
            throw new Exception("Cannot read backup settings");

        // Verify backup directory
        // The files of a backup can be in some of the destinations only
        var backupRoots = BackupDestinations!.Entries.Select(e => e.Fm.CombinePath(e.Path, bkp.Guid)).ToList();
        var rootExists = false;
        for (var i = 0; i < backupRoots.Count && !rootExists; i++)
            rootExists = await BackupDestinations.Entries[i].Fm.DirectoryExists(backupRoots[i]);
        if (!rootExists && bkpItemsCount > 0) {
            _errors.Add(new VerifyJobResult.Error(VerifyJobResult.Error.ErrorCodes.BackupDirectoryNotFound, $"Backup directory {string.Join(", ", backupRoots)} does not exists"));
            LogWarning("Backup directory does not exists");
        } else {
            // Verify backup items
            _progressData = new ProgressData()
            {
                Eta = StartEta(bkpSize, bkpItemsCount, BackupDestinations.Entries.Select(e => e.Fm).ToArray()),
                TotalBytes = bkpSize,
                TotalFiles = bkpItemsCount
            };

            var filesError = 0;
            Step?.Invoke(this, Steps.VerifyFiles);
            await foreach (var item in backupFile.GetBackupItems(bkp.Id)) {
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    return new VerifyJobResult()
                    {
                        Success = false,
                        ErrorMessage = "Cancelled by user"
                    };
                }

                FileProgress?.Invoke(this, item.OriginalRelativePath!, 0, item.Size);
                if (!await VerifyBackupItem(item))
                    filesError++;
                _progressData.FileIndex++;
                _progressData.Bytes += item.Size;
                Progress?.Invoke(this, new ProgressHandlerArgs(_progressData.FileIndex, bkpItemsCount, _progressData.Bytes, bkpSize, _progressData.Eta!.Update(_progressData.Bytes, _progressData.FileIndex)));
            }

            if (filesError > 0)
                LogWarning($"Errors: {filesError}");
        }

        return new VerifyJobResult()
        {
            Success = true
        };
    }

    /// <summary>
    /// Validate the <see cref="VerifyJobSettings"/>
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    protected override void ValidateSettings()
    {
        DestinationSet.Validate(Settings.BackupDestinations, nameof(Settings.BackupDestinations));
        if (string.IsNullOrEmpty(Settings.BackupGuid))
            throw new ArgumentNullException(nameof(Settings.BackupGuid), "Backup GUID cannot be empty");
    }

    private async Task<bool> VerifyBackupItem(BackupItem item)
    {
        LogDebug($"Verifying {item.OriginalRelativePath}");
        IFileManager fm;
        string filePath;
        try {
            (fm, filePath) = GetBackupItemLocation(item);
        } catch (MissingDestinationException ex) {
            _errors!.Add(new VerifyJobResult.Error(VerifyJobResult.Error.ErrorCodes.FileNotFound, $"File {item.OriginalRelativePath} not found: {ex.Message}"));
            LogWarning($"File {item.OriginalRelativePath} not found: {ex.Message}");
            return false;
        }

        if (!await fm.FileExists(filePath)) {
            _errors!.Add(new VerifyJobResult.Error(VerifyJobResult.Error.ErrorCodes.FileNotFound, $"File {filePath} not found"));
            LogWarning($"File {filePath} not found");
            return false;
        }

        // Check hash
        if (item.Type == File.FileTyes.File) {
            if (string.IsNullOrEmpty(item.Sha256)) {
                LogWarning($"Missing hash for file {item.OriginalRelativePath}");
                return false;
            }

            DateTime? lastFileProgress = null;
            var progressAction = new Action<long>((copied) =>
            {
                if (lastFileProgress == null || copied == item.Size ||
                    (DateTime.UtcNow - lastFileProgress.Value).TotalMilliseconds >= 500) {
                    FileProgress?.Invoke(this, item.OriginalRelativePath!, copied, item.Size);
                    Progress?.Invoke(this, new ProgressHandlerArgs(_progressData!.FileIndex, _progressData.TotalFiles, _progressData.Bytes + copied, _progressData.TotalBytes, _progressData.Eta!.Update(_progressData.Bytes + copied, _progressData.FileIndex)));
                    lastFileProgress = DateTime.UtcNow;
                }
            });

            var hash = await Utility.Files.GetSha256(fm, filePath, progressAction, _cancellationToken);
            if (hash != item.Sha256) {
                _errors!.Add(new VerifyJobResult.Error(VerifyJobResult.Error.ErrorCodes.HashMismatch, $"Hash mismatch for file {item.OriginalRelativePath}"));
                LogWarning($"Hash mismatch for file {item.OriginalRelativePath}");
                return false;
            }
        }

        return true;
    }

    #endregion
}