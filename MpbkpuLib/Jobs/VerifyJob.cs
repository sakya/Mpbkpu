using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using Newtonsoft.Json;
using SMBLibrary.Authentication;
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

    #endregion

    #region public operations
    public override async Task<bool> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        Step?.Invoke(this, Steps.OpenDatabase);
        using (var openRes = await BackupFile.OpenBackupFile(Settings.BackupPath!)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            var transaction = backupFile.BeginTransaction();

            try {
                await RunPrimitive(backupFile);
                await transaction.CommitAsync();
            } catch (Exception ex) {
                await transaction.RollbackAsync();
                LogError(ex);
                throw;
            }
        }

        return true;

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
        if (bkp == null)
            throw new Exception($"Backup with GUID { Settings.BackupGuid } not found");

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
        var fm = FileManagerFactory.GetFileManager(settings.Destination, out var newPath);

        // Verify backup folder
        var backupRoot = fm.CombinePath(newPath, bkp.Guid);
        if (!await fm.DirectoryExists(backupRoot)) {
            LogWarning("Database folder does not exists");
        } else {
            // Verify backup items
            Step?.Invoke(this, Steps.VerifyFiles);
            int fileIdx = 0;
            foreach (var item in items) {
                await VerifyBackupItem(fm, backupRoot, item);
                Progress?.Invoke(this, ++fileIdx, filesCount);
            }
        }

        return true;
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
            LogWarning($"File {filePath} not found");
        } else {
            // Check MD5
            if (item.Type == File.FileTyes.File) {
                if (string.IsNullOrEmpty(item.MD5))
                    LogWarning($"Missing MD5 for file {item.OriginalPath}");
                else {
                    using (var source = await fm.OpenFile(filePath, false)) {
                        using (var hasher = MD5.Create()) {
                            var hash = await hasher.ComputeHashAsync(source);
                            var md5 = BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
                            if (md5 != item.MD5) {
                                LogWarning($"MD5 mismatch for file {item.OriginalPath}");
                            }
                        }
                    }
                }
            }
        }

        return true;
    }

    #endregion
}