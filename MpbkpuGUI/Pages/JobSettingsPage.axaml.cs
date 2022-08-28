using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Pages;

public class JobSettingsPage : BasePage
{
    private readonly Job _job;

    public JobSettingsPage()
    {
        _job = new BackupJob();
        InitializeComponent();
    }

    public JobSettingsPage(Job job)
    {
        _job = job;
        DataContext = _job;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = Localizer.Localizer.Instance["JobSettings"];
        NavigateBackWithKeyboard = false;

        var cmb = this.FindControl<ComboBox>("CmbType");
        cmb.Items = new List<Backup.BackupTypes>()
        {
            Backup.BackupTypes.Full,
            Backup.BackupTypes.Differential,
            Backup.BackupTypes.Incremental
        };
    }

    public override void OnNavigatedTo(NavigationDirection direction)
    {
        this.FindControl<TextBox>("Name").Focus();
        base.OnNavigatedTo(direction);
    }

    private async void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        await MainWindow!.NavigateBack();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {

    }
}