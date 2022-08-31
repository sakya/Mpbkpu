using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Pages;

public class BackupVerifyPage : BasePage
{
    private readonly Backup _backup;
    public BackupVerifyPage()
    {
        _backup = new Backup();
        InitializeComponent();
    }

    public BackupVerifyPage(Backup backup)
    {
        _backup = backup;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["VerifyingBackup"]}: {_backup.Guid}";
        NavigateBackWithKeyboard = false;
    }
}