using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuLib;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Pages;

public class BackupBrowsePage : BasePage
{
    private readonly BackupBrowseModel _model = new();
    private readonly BackupFile _backupFile;
    private readonly int _backupId;

    public BackupBrowsePage()
    {
        _backupFile = new BackupFile();
        InitializeComponent();
    }

    public BackupBrowsePage(BackupFile backupFile, int backupId)
    {
        _backupFile = backupFile;
        _backupId = backupId;
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
        } catch (Exception ex) {
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
        }

        var spinner = this.FindControl<Controls.Spinner>("Spinner");
        spinner.IsVisible = false;
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }

    private async Task OpenBackup()
    {
        var bkp = await _backupFile.Backups.AsNoTracking()
            .Where(b => b.Id == _backupId)
            .FirstOrDefaultAsync();
        if (bkp == null)
            throw new Exception($"Backup with id {_backupId} not found");

        // Build directory tree
        var items = _backupFile.GetBackupItems(_backupId, File.FileTyes.Directory);
        _model.Directories = new ObservableCollection<BackupBrowseModel.Directory>();
        Dictionary<string, BackupBrowseModel.Directory> directories = new();
        await foreach (var item in items) {
            var dir = new BackupBrowseModel.Directory()
            {
                Name = item.OriginalName,
                Path = item.OriginalRelativePath,
                Children = new ObservableCollection<BackupBrowseModel.Directory>()
            };

            var parentPath = MpbkpuLib.Utility.Files.GetParent('\\', item.OriginalRelativePath!, out _, addStartingSeparator: false);
            if (directories.TryGetValue(parentPath, out var parent)) {
                parent!.Children!.Add(dir);
            } else {
                _model.Directories.Add(dir);
            }
            directories[dir.Path!] = dir;
        }
    }

    private async void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _model.Files = new ObservableCollection<BackupItem>();
        var dir = e.AddedItems.Count == 1 ? e.AddedItems[0] as BackupBrowseModel.Directory : null;
        if (dir != null) {
            var items = _backupFile.GetBackupItems(_backupId, File.FileTyes.File);
            await foreach (var item in items) {
                var parentPath = MpbkpuLib.Utility.Files.GetParent('\\', item.OriginalRelativePath!, out _, addStartingSeparator: false);
                if (parentPath == dir.Path)
                    _model.Files.Add(item);
            }
        }
    }
}