using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
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

    public enum ErrorCodes
    {
        BackupNotFound,
        BackupDirectoryNotFound,
        FileNotFound,
        Md5Mismatch
    }

    #region classes
    public class Error
    {
        public Error(ErrorCodes code, string message)
        {
            Code = code;
            Message = message;
        }

        public ErrorCodes Code { get; set; }
        public string Message { get; set; }
    }
    #endregion

    #region private members
    private CancellationToken? _cancellationToken;
    #endregion

    #region delegates
    public delegate void StepHandler(object sender, Steps step);

    public event StepHandler? Step;

    public delegate void ProgressHandler(object sender, int file, int totalFiles);

    public event ProgressHandler? Progress;
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

    public List<Error>? Errors { get; set; }
    #endregion

    #region public operations
    public override async Task<BaseJobResult> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        Started = DateTime.UtcNow;
        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        BackupFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out BackupPath, Settings.BackupPathUser, Settings.BackupPathPassword);
        Step?.Invoke(this, Steps.OpenDatabase);
        VerifyJobResult? res = null;
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword)) {
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
                Completed = DateTime.UtcNow;
                transaction.Dispose();
            }
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
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        Errors = new List<Error>();
        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null) {
            Errors.Add(new Error(ErrorCodes.BackupNotFound, $"Backup with GUID {Settings.BackupGuid} not found"));
            LogError($"Backup with GUID {Settings.BackupGuid} not found");
            return new VerifyJobResult()
            {
                Success = false,
                ErrorMessage = $"Backup with GUID {Settings.BackupGuid} not found"
            };
        }

        if (bkp.Result != Backup.Results.Completed)
            LogWarning($"Database result is { bkp.Result }");

        var items = backupFile.BackupItems.AsNoTracking()
            .Include(i => i.Volume)
            .Where(i => i.BackupId == bkp.Id)
            .OrderBy(i => i.Id);

        var filesCount = await items.CountAsync();

        LogInfo($"Type     : { bkp.Type }");
        LogInfo($"Started  : { bkp.StartedAt!.Value.ToLocalTime().ToShortDateString() } { bkp.StartedAt!.Value.ToLocalTime().ToShortTimeString() }");
        if (bkp.CompletedAt.HasValue)
            LogInfo($"Completed: { bkp.CompletedAt!.Value.ToLocalTime().ToShortDateString() } { bkp.CompletedAt!.Value.ToLocalTime().ToShortTimeString() }");
        LogInfo($"Size     : { Utility.String.BytesToString(await items.SumAsync(i => i.OriginalSize)) }");

        var settings = JsonConvert.DeserializeObject<BackupJobSettings>(bkp.Settings!);
        if (settings == null)
            throw new Exception("Cannot read backup settings");

        // Verify backup directory
        var backupRoot = BackupFm.CombinePath(BackupPath!, bkp.Guid);
        if (!await BackupFm.DirectoryExists(backupRoot)) {
            Errors.Add(new Error(ErrorCodes.BackupDirectoryNotFound, $"Backup directory {backupRoot} does not exists"));
            LogWarning("Backup directory does not exists");
        } else {
            // Verify backup items
            int filesError = 0;
            Step?.Invoke(this, Steps.VerifyFiles);
            int fileIdx = 0;
            foreach (var item in items) {
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    return new VerifyJobResult()
                    {
                        Success = false,
                        ErrorMessage = "Cancelled by user"
                    };
                }

                if (!await VerifyBackupItem(BackupFm, backupRoot, item))
                    filesError++;
                Progress?.Invoke(this, ++fileIdx, filesCount);
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
        if (string.IsNullOrEmpty(Settings.BackupPath))
            throw new ArgumentNullException(nameof(Settings.BackupPath), "Backup path cannot be empty");
        if (string.IsNullOrEmpty(Settings.BackupGuid))
            throw new ArgumentNullException(nameof(Settings.BackupGuid), "Backup GUID cannot be empty");
    }

    private async Task<bool> VerifyBackupItem(IFileManager fm, string backupRoot, BackupItem item)
    {
        var filePath = fm.CombinePath(backupRoot, item.Volume!.Name!, item.Name!);
        if (!await fm.FileExists(filePath)) {
            Errors!.Add(new Error(ErrorCodes.FileNotFound, $"File {filePath} not found"));
            LogWarning($"File {filePath} not found");
            return false;
        }

        // Check MD5
        if (item.Type == File.FileTyes.File) {
            if (string.IsNullOrEmpty(item.MD5)) {
                LogWarning($"Missing MD5 for file {item.OriginalPath}");
                return false;
            } else {
                var md5 = await Utility.Files.GetMD5(fm, filePath);
                if (md5 != item.MD5) {
                    Errors!.Add(new Error(ErrorCodes.Md5Mismatch, $"MD5 mismatch for file {item.OriginalPath}"));
                    LogWarning($"MD5 mismatch for file {item.OriginalPath}");
                    return false;
                }
            }
        }

        return true;
    }

    #endregion
}