using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuLib;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;

namespace MpbkpuGUI.Pages;

public partial class BackupOpenPage : BasePage
{
    private BackupJob _backup;
    private BackupFile.OpenResult? _backupFile;
    private BackupOpenPageModel _model = new();

    public BackupOpenPage()
    {
        _backup = new BackupJob();
        DataContext = _model;
        InitializeComponent();
    }

    public BackupOpenPage(BackupJob backup)
    {
        _backup = backup;
        DataContext = _model;
        InitializeComponent();
    }

    public override void Dispose()
    {
        _backupFile?.Dispose();
        base.Dispose();
    }

    public override async void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);

        await Task.Delay(500);
        var oauthSettings = new OAuth2Settings()
        {
            AppName = _backup.Destination!.OauthAppName,
            AppId = _backup.Destination.OauthAppId,
            AppSecret = _backup.Destination.OauthAppSecret,
            ServerUrl = _backup.Destination.OauthServerUrl,
            RedirectUrl = _backup.Destination.OauthRedirectUrl
        };
        using var fm = FileManagerFactory.GetFileManager(_backup.Destination.FullPath, out var backupPath, oauth2Settings: oauthSettings);
        _backupFile = await BackupFile.OpenBackupFile(fm, backupPath, _backup.Password);

        _model.Backups = new ObservableCollection<BackupOpenPageModel.BackupData>();
        var backups = _backupFile.BackupFile.Backups.OrderBy(b => b.Id)
            .Select(b => new
            {
                Backup = b,
                Files = b.Items.Count(),
                Bytes = b.Items.Sum(i => i.OriginalSize)
            });
        foreach (var bkp in backups) {
            _model.Backups.Add(new BackupOpenPageModel.BackupData()
            {
                Backup = bkp.Backup,
                Items = bkp.Files,
                Size = bkp.Bytes
            });
        }

        await Task.Delay(5000);
        var wait = this.FindControl<Controls.Spinner>("Spinner");
        wait.IsVisible = false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["Backup"]}: {_backup.Name}";
    }
}