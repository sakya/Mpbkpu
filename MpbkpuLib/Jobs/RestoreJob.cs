using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using Newtonsoft.Json;

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
        RestoreFiles,
        CopyDatabase
    }

    #region classes

    #endregion

    #region private members
    private CancellationToken? _cancellationToken;
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
        /// Number of bytes that will be restored to the destnation
        /// </summary>
        public long BytesToRestore { get; set; }
    }

    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmRestore;

    public delegate void ProgressHandler(object sender, int file, int totalFiles, long bytes, long totalBytes);
    public event ProgressHandler? Progress;

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
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword, true)) {
            var backupFile = openRes.BackupFile;
            await backupFile.EmptyFiles();
            try {
                await RunPrimitive(backupFile);
                backupFile.Close();
            } catch (Exception ex) {
                LogError(ex);
                throw;
            } finally {
                Completed = DateTime.UtcNow;
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
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null) {
            LogError($"Backup with GUID {Settings.BackupGuid} not found");
            return false;
        }

        // Read destination
        Step?.Invoke(this, Steps.ReadDestination);
        LogInfo($"Reading destination {Settings.Destination}");
        if (!ReadPathContent(backupFile, Settings.Destination, Settings.Destination, true, _cancellationToken).Result) {
            LogError("Failed to read destination");
            return false;
        }
        if (_cancellationToken?.IsCancellationRequested == true) {
            LogInfo("Cancelled by user");
            return false;
        }
        await backupFile.SaveChangesAsync();

        Step?.Invoke(this, Steps.CalculateFilesToRestore);
        // Get backup files
        var backupItems = backupFile.GetBackupItems(bkp.Id);

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

    #endregion
}