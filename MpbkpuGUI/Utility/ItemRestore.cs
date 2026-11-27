using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using MpbkpuGUI.Controls;
using MpbkpuGUI.Dialogs;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Jobs;

namespace MpbkpuGUI.Utility;

/// <summary>
/// Restores an item of a backup (a file, a directory or the whole backed up folder) into a local folder chosen by the
/// user. Existing files are overwritten, nothing else in the folder is deleted (RestoreJobSettings.RelativePath).
/// </summary>
public static class ItemRestore
{
    /// <summary>
    /// Localized texts (keys) of the operation
    /// </summary>
    /// <param name="PickerTitle">Title of the folder picker</param>
    /// <param name="Progress">Shown on the spinner, followed by the percentage</param>
    /// <param name="Completed">Message at the end</param>
    /// <param name="Confirm">Question asked after the folder is chosen ({0}: the folder), null to ask nothing</param>
    public record Texts(string PickerTitle, string Progress, string Completed, string? Confirm = null);

    /// <param name="relativePath">The original relative path of the item, "" for the whole backed up folder</param>
    /// <param name="spinner">Shown while restoring</param>
    /// <param name="setMessage">Sets the message of the spinner</param>
    public static async Task Run(string backupGuid, List<BackupDestination> destinations, string? password, string relativePath,
        Texts texts, Spinner spinner, Action<string> setMessage)
    {
        var localizer = Localizer.Localizer.Instance;
        var folders = await App.GetMainWindow()!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
        {
            Title = localizer[texts.PickerTitle]
        });
        var folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(folder))
            return;
        if (texts.Confirm != null &&
            !await MessageDialog.ShowConfirmMessage(App.GetMainWindow(), string.Format(localizer[texts.Confirm], folder)))
            return;

        setMessage(localizer[texts.Progress]);
        spinner.IsVisible = true;
        try {
            using var job = new RestoreJob(new RestoreJobSettings()
            {
                BackupGuid = backupGuid,
                BackupDestinations = destinations,
                EncryptPassword = password,
                Destination = folder,
                RelativePath = relativePath
            });
            job.RestoreProgress += (_, args) => setMessage($"{localizer[texts.Progress]} {Math.Floor(args.Percentage)}%");
            var res = await Task.Run(() => job.Run(CancellationToken.None));
            spinner.IsVisible = false;
            if (res.Success)
                await MessageDialog.ShowMessage(App.GetMainWindow(), localizer[texts.Completed], MessageDialog.Icons.Info);
            else
                await MessageDialog.ShowMessage(App.GetMainWindow(), $"{localizer["AnErrorOccurred"]}{res.ErrorMessage}", MessageDialog.Icons.Error);
        } catch (Exception ex) {
            spinner.IsVisible = false;
            await MessageDialog.ShowMessage(App.GetMainWindow(), $"{localizer["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
        }
    }
}
