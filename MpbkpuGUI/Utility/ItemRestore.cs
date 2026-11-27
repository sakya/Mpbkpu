using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    /// <param name="Progress">Shown on the spinner while the files are restored</param>
    /// <param name="Completed">Message at the end</param>
    /// <param name="Confirm">Question asked after the folder is chosen ({0}: the folder), null to ask nothing</param>
    public record Texts(string PickerTitle, string Progress, string Completed, string? Confirm = null);

    /// <summary>
    /// Called when the user leaves the page (back arrow): a running restore is cancelled after a confirmation, as with
    /// the cancel button, and the page stays to show the result (like the verify and backup pages)
    /// </summary>
    /// <returns>True when the page can be left (nothing running)</returns>
    public static Task<bool> ConfirmLeave(Spinner spinner)
    {
        if (!spinner.CanCancel)
            return Task.FromResult(true);
        // The handler of Run asks for the confirmation
        spinner.RequestCancel();
        return Task.FromResult(false);
    }

    /// <param name="relativePath">The original relative path of the item, "" for the whole backed up folder</param>
    /// <param name="spinner">Shown while restoring, with the progress and a cancel button</param>
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

        using var cts = new CancellationTokenSource();
        // Cancel button and back arrow: confirmed, once
        var confirming = false;
        EventHandler cancel = async (_, _) =>
        {
            if (confirming || cts.IsCancellationRequested)
                return;
            confirming = true;
            try {
                if (await MessageDialog.ShowConfirmMessage(App.GetMainWindow(), localizer["ConfirmAbortRestore"],
                        MessageDialog.Icons.Warning))
                    cts.Cancel();
            } catch (ObjectDisposedException) {
                // The restore ended while the question was open
            } finally {
                confirming = false;
            }
        };
        setMessage(localizer["Step_OpenDatabase"]);
        spinner.CancelRequested += cancel;
        spinner.IsVisible = true;
        var running = true;
        try {
            using var job = new RestoreJob(new RestoreJobSettings()
            {
                BackupGuid = backupGuid,
                BackupDestinations = destinations,
                EncryptPassword = password,
                Destination = folder,
                RelativePath = relativePath
            });
            // The job raises its events on its own threads; updates posted after the end are dropped
            job.Step += (_, step) => Dispatcher.UIThread.Post(() =>
            {
                if (running)
                    setMessage(step == RestoreJob.Steps.RestoreFiles ? localizer[texts.Progress] : localizer[$"Step_{step}"]);
            });
            job.RestoreProgress += (_, args) => Dispatcher.UIThread.Post(() =>
            {
                if (running)
                    spinner.ShowProgress(Math.Floor(args.Percentage),
                        $"{localizer["Files"]}: {args.File}/{args.TotalFiles}   " +
                        $"{MpbkpuLib.Utility.String.BytesToString(args.Bytes)}/{MpbkpuLib.Utility.String.BytesToString(args.TotalBytes)}   " +
                        $"{localizer["TimeLeft"]}: {MpbkpuLib.Utility.String.EtaToString(args.Eta)}");
            });

            var res = await Task.Run(() => job.Run(cts.Token));
            running = false;
            spinner.IsVisible = false;
            if (cts.IsCancellationRequested)
                await MessageDialog.ShowMessage(App.GetMainWindow(), localizer["RestoreCancelled"], MessageDialog.Icons.Info);
            else if (res.Success)
                await MessageDialog.ShowMessage(App.GetMainWindow(), localizer[texts.Completed], MessageDialog.Icons.Info);
            else
                await MessageDialog.ShowMessage(App.GetMainWindow(), $"{localizer["AnErrorOccurred"]}{res.ErrorMessage}", MessageDialog.Icons.Error);
        } catch (Exception ex) {
            running = false;
            spinner.IsVisible = false;
            await MessageDialog.ShowMessage(App.GetMainWindow(), $"{localizer["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
        } finally {
            spinner.CancelRequested -= cancel;
        }
    }
}
