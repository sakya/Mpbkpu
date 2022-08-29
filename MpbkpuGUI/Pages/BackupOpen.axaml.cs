using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Pages;

public partial class BackupOpen : BasePage
{
    private BackupJob _backup;
    public BackupOpen()
    {
        _backup = new BackupJob();
        InitializeComponent();
    }

    public BackupOpen(BackupJob backup)
    {
        _backup = backup;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["Backup"]}: {_backup.Name}";
    }
}