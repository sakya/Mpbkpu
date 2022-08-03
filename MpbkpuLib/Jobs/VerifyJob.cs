using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using Newtonsoft.Json;

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

                backupFile.Close();
                // Copy temporary database file to destination
                if (!string.IsNullOrEmpty(openRes.TempFilePath)) {
                    var sourceFm = FileManagerFactory.GetFileManager(openRes.TempFilePath, out _);
                    var destFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out var destPath);

                    using (var sourceStream = await sourceFm.OpenFile(openRes.TempFilePath)) {
                        using (var destStream = await destFm.OpenFile(openRes.BackupFilePath, true)) {
                            await Utility.Files.Copy(sourceStream, destStream);
                        }
                    }
                }
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
        // Verify backup folders
        var backups = backupFile.Backups.AsNoTracking()
            .OrderBy(b => b.Id);
        foreach (var backup in backups) {
            var settings = JsonConvert.DeserializeObject<BackupJobSettings>(backup.Settings!);
            if (settings == null)
                throw new Exception($"Cannot read settings of backup with GUID {backup.Guid}");
            var fm = FileManagerFactory.GetFileManager(settings.Destination, out var newPath);

            // Verify backup folder
            if (!await fm.DirectoryExists(fm.CombinePath(newPath, backup.Guid))) {

            }

            // Verify backup items
            var items = backupFile.BackupItems.AsNoTracking()
                .Include(i => i.Volume)
                .Where(i => i.BackupId == backup.Id)
                .OrderBy(i => i.Id);

            foreach (var item in items) {
                if (!await fm.FileExists(fm.CombinePath(newPath, backup.Guid, item.Volume!.Name!, item.Name!))) {

                }
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
    }

    #endregion
}