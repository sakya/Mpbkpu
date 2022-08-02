using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using Newtonsoft.Json;

namespace MpbkpuLib;

public class DeleteJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        DeleteFiles
    }

    #region private members
    private CancellationToken? _cancellationToken;
    #endregion

    #region delegates
    public class ConfirmArgs : EventArgs
    {
        public string? Guid { get; set; }
        public int FilesToDelete { get; set; }
        public long BytesToDelete { get; set; }
    }
    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmDelete;

    public delegate void StepHandler(object sender, Steps step);

    public event StepHandler? Step;
    #endregion

    public DeleteJob(DeleteJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new DeleteJobSettings Settings
    {
        get { return (base.Settings as DeleteJobSettings)!; }
    }
    #endregion

    #region public operations
    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/></param>
    /// <returns>True on success</returns>
    public override async Task<bool> Run(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        ValidateSettings();

        LogInfo("Starting with settings:");
        LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));

        Step?.Invoke(this, DeleteJob.Steps.OpenDatabase);
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
        throw new NotImplementedException();
    }

    /// <summary>
    /// Validate the <see cref="DeleteJobSettings"/>
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

    #endregion
}