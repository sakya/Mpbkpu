using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
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
        BackupFiles,
    }

    #region classes
    private class ProgressData
    {
        public DateTime? Started { get; set; }
        public int TotalFiles { get; set; }
        public int FileIndex { get; set; }
        public long TotalBytes { get; set; }
        public long Bytes { get; set; }
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
    }
    public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
    public event ConfirmHandler? ConfirmBackup;

    public delegate void StepHandler(object sender, Steps step);
    public event StepHandler? Step;

    public delegate void ProgressHandler(object sender, ProgressHandlerArgs args);
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

        var tempDbFile = Utility.TempFile.GetTempFileName(".db");
        using var tempFm = FileManagerFactory.GetFileManager(tempDbFile, out _);
        using (var openRes = await BackupFile.OpenBackupFile(tempFm, tempDbFile!, create: true)) {
            var backupFile = openRes.BackupFile;
            try {
                await RunPrimitive(backupFile);
            } catch (Exception ex) {
                LogError(ex);
                throw;
            }
        }

        return new SyncDirectoryJobResult()
        {
            Success = true,
            Started = started,
            Bytes = _progressData?.Bytes,
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
        if (!ReadPathContent(backupFile, _sourceFm, _sourcePath, Settings.IncludeHiddenFiles, File.FileActions.Backup, _cancellationToken).Result) {
            LogError("Failed to read source");
            return;
        }

        // Read destination
        Step?.Invoke(this, Steps.ReadDestination);
        LogInfo($"Reading source {Settings.Destination}");
        if (!ReadPathContent(backupFile, BackupFm, BackupPath, Settings.IncludeHiddenFiles, File.FileActions.Delete, _cancellationToken).Result) {
            LogError("Failed to read source");
            return;
        }

        LogInfo("Checking files to backup");
        Step?.Invoke(this, Steps.CalculateFilesToBackup);
        var sourceItems = backupFile.Files
            .Where(f => f.RootPath == Settings.Source);
        foreach (var sourceItem in sourceItems) {
            var destItem = await backupFile.Files
                .Where(f => f.RelativePath == sourceItem.RelativePath)
                .FirstOrDefaultAsync();
            if (destItem != null) {
                // Check if the source file has changed
            }
        }
    }
    #endregion
}