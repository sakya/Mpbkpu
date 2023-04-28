using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Models;
using MpbkpuLib;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;

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
        _model.Directories = await _backupFile.GetDirectoryTree(_backupId);
    }

    private async void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _model.Files = new List<BackupItem>();
        var dir = e.AddedItems.Count == 1 ? e.AddedItems[0] as MpbkpuLib.BackupFile.Directory : null;
        if (dir != null) {
            IFileManager? fm = null;
            var files = new List<BackupItem>();
            var items = _backupFile.GetBackupItems(_backupId);
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
}