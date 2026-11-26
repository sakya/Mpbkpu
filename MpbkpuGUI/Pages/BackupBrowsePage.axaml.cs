using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuLib;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Jobs;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuGUI.Pages;

public partial class BackupBrowsePage : BasePage
{
    private readonly BackupBrowseModel _model = new();
    private readonly BackupFile _backupFile;
    private readonly Backup _backup;
    private readonly List<BackupDestination> _destinations = new();
    private readonly string? _password;

    public BackupBrowsePage()
    {
        _backupFile = new BackupFile();
        _backup = new Backup();
        InitializeComponent();
    }

    /// <param name="destinations">The destinations of the job and the encryption password: used to download items</param>
    public BackupBrowsePage(BackupFile backupFile, Backup backup, List<BackupDestination> destinations, string? password)
    {
        _backupFile = backupFile;
        _backup = backup;
        _destinations = destinations;
        _password = password;
        DataContext = _model;

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = Localizer.Localizer.Instance["BrowseBackup"];
        _model.ButtonText = Localizer.Localizer.Instance["Close"];
        NavigateBackWithKeyboard = false;
    }

    public override async void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);

        _model.SpinnerMessage = Localizer.Localizer.Instance["Step_OpenBackup"];
        try {
            await Task.Run(OpenBackup);
            // Back on the UI thread: show the files of the backed up folder at once
            this.FindControl<TreeView>("TreeView")!.SelectedItem = _model.Directories?.FirstOrDefault();
        } catch (Exception ex) {
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
        }

        var spinner = this.FindControl<Controls.Spinner>("Spinner")!;
        spinner.IsVisible = false;
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }

    private async Task OpenBackup()
    {
        _model.Directories = await _backupFile.GetDirectoryTree(_backup.Id);
    }

    private async void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _model.Files = new List<BackupItem>();
        var dir = e.AddedItems.Count == 1 ? e.AddedItems[0] as MpbkpuLib.BackupFile.Directory : null;
        if (dir != null) {
            IFileManager? fm = null;
            var files = new List<BackupItem>();
            var items = _backupFile.GetBackupItems(_backup.Id);
            await foreach (var item in items) {
                fm ??= FileManagerFactory.GetFileManager(item.OriginalPath, out _);
                var parentPath = MpbkpuLib.Utility.Files.GetParent(fm.PathSeparator, item.OriginalRelativePath!, out _, addStartingSeparator: false);
                if (parentPath == dir.Path)
                    files.Add(item);
            }

            _model.Files = files.OrderByDescending(f => f.Type)
                .ThenBy(f => f.OriginalName)
                .ToList();
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not BackupItem { Type: File.FileTyes.Directory } item)
            return;

        // Expand the ancestors of the directory in the tree, so its node exists, then select it
        var treeView = this.FindControl<TreeView>("TreeView")!;
        var path = FindDirectory(_model.Directories, item.OriginalRelativePath);
        if (path == null)
            return;
        foreach (var dir in path.SkipLast(1)) {
            if (treeView.TreeContainerFromItem(dir) is TreeViewItem container) {
                container.IsExpanded = true;
                container.UpdateLayout();
            }
        }
        treeView.SelectedItem = path[^1];
        treeView.TreeContainerFromItem(path[^1])?.BringIntoView();
    }

    /// <summary>
    /// The directories from a root to the directory with the given relative path, null when not found
    /// </summary>
    private static List<MpbkpuLib.BackupFile.Directory>? FindDirectory(IEnumerable<MpbkpuLib.BackupFile.Directory>? directories,
        string? relativePath)
    {
        foreach (var dir in directories ?? []) {
            if (dir.Path == relativePath)
                return [dir];
            var path = FindDirectory(dir.Children, relativePath);
            if (path != null)
                return path.Prepend(dir).ToList();
        }

        return null;
    }

    /// <summary>
    /// Restore a file or a directory of the backup to a folder chosen by the user
    /// </summary>
    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var relativePath = (sender as Control)?.DataContext switch
        {
            BackupItem item => item.OriginalRelativePath,
            MpbkpuLib.BackupFile.Directory dir => dir.Path,
            _ => null
        };
        if (relativePath == null)
            return;

        var folders = await App.GetMainWindow()!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
        {
            Title = Localizer.Localizer.Instance["DownloadTo"]
        });
        var folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(folder))
            return;

        var spinner = this.FindControl<Controls.Spinner>("Spinner")!;
        _model.SpinnerMessage = Localizer.Localizer.Instance["Downloading"];
        spinner.IsVisible = true;
        try {
            using var job = new RestoreJob(new RestoreJobSettings()
            {
                BackupGuid = _backup.Guid,
                BackupDestinations = _destinations,
                EncryptPassword = _password,
                Destination = folder,
                RelativePath = relativePath
            });
            job.RestoreProgress += (_, args) =>
                _model.SpinnerMessage = $"{Localizer.Localizer.Instance["Downloading"]} {Math.Floor(args.Percentage)}%";
            var res = await Task.Run(() => job.Run(CancellationToken.None));
            spinner.IsVisible = false;
            if (res.Success)
                await MessageDialog.ShowMessage(App.GetMainWindow(), Localizer.Localizer.Instance["DownloadCompleted"], MessageDialog.Icons.Info);
            else
                await MessageDialog.ShowMessage(App.GetMainWindow(),
                    $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{res.ErrorMessage}", MessageDialog.Icons.Error);
        } catch (Exception ex) {
            spinner.IsVisible = false;
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
        }
    }
}