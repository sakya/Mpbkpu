using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Pages;

public partial class BackupBrowsePage : BasePage
{
    private readonly BackupBrowseModel _model = new();
    private readonly Backup _backup;
    private readonly string? _password;

    public BackupBrowsePage()
    {
        _backup = new Backup();
        InitializeComponent();
    }

    public BackupBrowsePage(Backup backup, string? password)
    {
        _backup = backup;
        _password = password;
        DataContext = _model;

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["BrowseBackup"]}: {_backup.Guid}";
        NavigateBackWithKeyboard = false;
    }

    public override void OnNavigatedTo(NavigationDirection direction)
    {
        base.OnNavigatedTo(direction);

        _model.ButtonText = Localizer.Localizer.Instance["Cancel"];
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }
}