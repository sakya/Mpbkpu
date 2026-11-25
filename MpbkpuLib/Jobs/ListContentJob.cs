using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.Jobs;

public class ListContentJob : BaseJob
{
    public enum Steps
    {
        OpenDatabase,
        ListContent
    }

    #region private members
    private CancellationToken? _cancellationToken;
    #endregion

    #region delegates
    public delegate void StepHandler(object sender, Steps step);
    public event StepHandler? Step;

    public delegate void ContentHandler(object sender, BackupItem item);
    public event ContentHandler? Content;
    #endregion

    public ListContentJob(ListContentJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new ListContentJobSettings Settings
    {
        get { return (base.Settings as ListContentJobSettings)!; }
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
        BackupFm = FileManagerFactory.GetFileManager(Settings.BackupPath, out BackupPath, Settings.BackupPathUser, Settings.BackupPathPassword, Settings.BackupOauthSettings);
        Step?.Invoke(this, Steps.OpenDatabase);
        ListContentJobResult? res;
        using (var openRes = await BackupFile.OpenBackupFile(BackupFm, BackupPath, Settings.EncryptPassword, progressAction: OnOpenBackup, token: _cancellationToken)) {
            var backupFile = openRes.BackupFile;
            try {
                res = await RunPrimitive(backupFile);
            } catch (Exception ex) {
                LogError(ex);
                throw;
            }
        }

        res.Started = started;
        res.Completed = DateTime.UtcNow;
        return res;
    }
    #endregion

    #region private operations

    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="backupFile">The <see cref="BackupFile"/> to use</param>
    /// <returns>True on success</returns>
    private async Task<ListContentJobResult> RunPrimitive(BackupFile backupFile)
    {
        if (BackupFm == null)
            throw new Exception("Backup file manager not initialized");

        var res = new ListContentJobResult();
        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == Settings.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null) {
            LogError($"Backup with GUID {Settings.BackupGuid} not found");
            res.ErrorMessage = $"Backup with GUID {Settings.BackupGuid} not found";
            return res;
        }

        Step?.Invoke(this, Steps.ListContent);
        Regex? regex = !string.IsNullOrEmpty(Settings.Filter) ? new Regex(Settings.Filter) : null;
        await foreach (var item in backupFile.GetBackupItems(bkp.Id)) {
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                res.ErrorMessage = "Cancelled by user";
                return res;
            }

            if (regex != null) {
                if (regex.Match(item.OriginalRelativePath!).Success) {
                    CountResult(res, item);
                    Content?.Invoke(this, item);
                }
            } else {
                CountResult(res, item);
                Content?.Invoke(this, item);
            }
        }

        res.Success = true;
        return res;
    }

    protected override void ValidateSettings()
    {
        if (string.IsNullOrEmpty(Settings.BackupPath))
            throw new ArgumentNullException(nameof(Settings.BackupPath), "Backup path cannot be empty");
        if (string.IsNullOrEmpty(Settings.BackupGuid))
            throw new ArgumentNullException(nameof(Settings.BackupGuid), "Backup GUID cannot be empty");

        if (!string.IsNullOrEmpty(Settings.Filter)) {
            try {
                var m = Regex.Match(string.Empty, Settings.Filter);
            } catch (ArgumentException) {
                throw new ArgumentException("Filter is not a valid regular expression", nameof(Settings.Filter));
            }
        }
    }

    private void CountResult(ListContentJobResult res, BackupItem item)
    {
        switch (item.Type) {
            case File.FileTyes.File:
                res.FilesCount++;
                break;
            case File.FileTyes.Directory:
                res.DirectoriesCount++;
                break;
        }
    }
    #endregion
}