using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Pages;

public partial class DeleteBackupPage : BasePage
{
    private readonly Backup _backup;
    public DeleteBackupPage()
    {
        _backup = new Backup();
        InitializeComponent();
    }

    public DeleteBackupPage(Backup backup)
    {
        _backup = backup;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["DeletingBackup"]}: {_backup.Guid}";
        NavigateBackWithKeyboard = false;
    }
}