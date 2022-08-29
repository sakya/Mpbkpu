using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Pages;

public class JobSettingsPage : BasePage
{
    private readonly string _jobId;
    private readonly Job _job;

    public JobSettingsPage()
    {
        _jobId = string.Empty;
        _job = new BackupJob();
        InitializeComponent();
    }

    public JobSettingsPage(Job job)
    {
        _jobId = job.Id;
        _job = job.Copy();
        DataContext = _job;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = Localizer.Localizer.Instance["JobSettings"];
        NavigateBackWithKeyboard = false;

        var ctrl = this.FindControl<Controls.PositionDetail>("Source");
        ctrl.Value = _job.Source;
        ctrl = this.FindControl<Controls.PositionDetail>("Destination");
        ctrl.Value = _job.Destination;

        var cmbType = this.FindControl<ComboBox>("CmbType");
        cmbType.Items = new List<Backup.BackupTypes>()
        {
            Backup.BackupTypes.Full,
            Backup.BackupTypes.Differential,
            Backup.BackupTypes.Incremental
        };

        var cmbCompression = this.FindControl<ComboBox>("CmbCompressionLevel");
        cmbCompression.Items = new List<CompressionLevel>()
        {
            CompressionLevel.NoCompression,
            CompressionLevel.Fastest,
            CompressionLevel.Optimal,
            CompressionLevel.SmallestSize
        };

        if (_job is BackupJob backupJob) {
            cmbType.SelectedItem = backupJob.Type;
            cmbCompression.SelectedItem = backupJob.CompressionLevel;
        }
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

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var ctrl = this.FindControl<Controls.PositionDetail>("Source");
        _job.Source = ctrl.Value;
        ctrl = this.FindControl<Controls.PositionDetail>("Destination");
        _job.Destination = ctrl.Value;
        var job = App.Settings!.Jobs
            .FirstOrDefault(j => j.Id == _jobId);
        if (job != null) {
            var idx = App.Settings.Jobs.IndexOf(job);
            App.Settings.Jobs[idx] = _job;
        } else {
            App.Settings.Jobs.Add(_job);
        }
        await App.Settings.Save(App.SettingFile!);
        await MainWindow!.NavigateBack();
    }
}