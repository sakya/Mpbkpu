using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Material.Icons;
using Material.Icons.Avalonia;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.SingleWindow.Abstracts;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Controls;
using MpbkpuGUI.Models;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Pages;

public partial class JobSettingsPage : BasePage
{
    private readonly JobSettingsPageModel _model;
    private readonly string _jobId;
    private readonly Job _job;

    public JobSettingsPage()
    {
        _model = new JobSettingsPageModel();
        DataContext = _model;
        _jobId = string.Empty;
        _job = new BackupJob();
        InitializeComponent();
    }

    public JobSettingsPage(Job job)
    {
        _jobId = job.Id;
        _job = job.Copy();

        var exJob = App.Settings!.Jobs
            .FirstOrDefault(j => j.Id == _jobId);

        _model = new JobSettingsPageModel()
        {
            Job = _job,
            IsNew = exJob == null
        };
        DataContext = _model;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = Localizer.Localizer.Instance[_job is SyncJob ? "SyncSettings" : "JobSettings"];
        this.FindControl<TextBlock>("PageHeader")!.Text = PageTitle;
        NavigateBackWithKeyboard = false;

        UpdateSavedPasswordPanel();

        var ctrl = this.FindControl<Controls.PositionDetail>("Source")!;
        ctrl.Value = _job.Source;
        ctrl = this.FindControl<Controls.PositionDetail>("Destination")!;
        ctrl.Value = _job.Destination;

        var cmbType = this.FindControl<ComboBox>("CmbType")!;
        cmbType.ItemsSource = new List<Backup.BackupTypes>()
        {
            Backup.BackupTypes.Full,
            Backup.BackupTypes.Differential,
            Backup.BackupTypes.Incremental
        };

        var cmbCompression = this.FindControl<ComboBox>("CmbCompressionLevel")!;
        cmbCompression.ItemsSource = new List<CompressionLevel>()
        {
            CompressionLevel.NoCompression,
            CompressionLevel.Fastest,
            CompressionLevel.Optimal,
            CompressionLevel.SmallestSize
        };

        if (_job is BackupJob backupJob) {
            cmbType.SelectedItem = backupJob.Type;
            cmbCompression.SelectedItem = backupJob.CompressionLevel;
            foreach (var destination in backupJob.MoreDestinations)
                AddMoreDestination(destination);
        }
    }

    /// <summary>
    /// The editors of the destinations after the first one
    /// </summary>
    private IEnumerable<PositionDetail> MoreDestinationEditors =>
        this.FindControl<StackPanel>("MoreDestinationsList")!.Children.OfType<Grid>()
            .Select(g => g.Children.OfType<PositionDetail>().First());

    /// <summary>
    /// Add the editor of a destination after the first one, with a button to remove it
    /// </summary>
    private void AddMoreDestination(Job.Position position)
    {
        var list = this.FindControl<StackPanel>("MoreDestinationsList")!;
        var removeText = Localizer.Localizer.Instance["RemoveDestination"];
        var remove = new Button()
        {
            Content = new MaterialIcon() { Kind = MaterialIconKind.DeleteOutline },
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(12, 0, 0, 0)
        };
        remove.Classes.Add("icon");
        remove.Classes.Add("dangerHover");
        ToolTip.SetTip(remove, removeText);
        AutomationProperties.SetName(remove, removeText);
        Grid.SetColumn(remove, 1);

        var item = new Grid()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { new PositionDetail() { Value = position }, remove }
        };
        remove.Click += (_, _) => list.Children.Remove(item);
        list.Children.Add(item);
    }

    private void OnAddDestinationClick(object? sender, RoutedEventArgs e)
    {
        AddMoreDestination(new Job.Position());
    }

    public override void OnNavigatedTo(NavigationDirection direction)
    {
        this.FindControl<TextBox>("Name")!.Focus();
        base.OnNavigatedTo(direction);
    }

    private async void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        await MainWindow!.NavigateBack();
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var ctrl = this.FindControl<Controls.PositionDetail>("Source")!;
        _job.Source = ctrl.Value;
        ctrl = this.FindControl<Controls.PositionDetail>("Destination")!;
        _job.Destination = ctrl.Value;
        if (_job is BackupJob backupJob)
            backupJob.MoreDestinations = MoreDestinationEditors.Select(d => d.Value!).ToList();

        if (!Validate())
            return;

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

    private void UpdateSavedPasswordPanel()
    {
        this.FindControl<Control>("SavedPasswordPanel")!.IsVisible = _job is BackupJob { HasSavedPassword: true };
    }

    /// <summary>
    /// Forget the saved encryption password (applied when the settings are saved)
    /// </summary>
    private void OnForgetPasswordClick(object? sender, RoutedEventArgs e)
    {
        (_job as BackupJob)?.ForgetPassword();
        UpdateSavedPasswordPanel();
    }

    private bool Validate()
    {
        _model.Errors = null;
        var errors = new List<Controls.ErrorContainer.ErrorData>();
        if (string.IsNullOrEmpty(_model.Job!.Name))
            errors.Add(new ErrorContainer.ErrorData("Name", Localizer.Localizer.Instance["RequiredError"]));

        // The destinations must accept files of unknown size (compressed)
        if (_job is BackupJob { IsCompressed: true } compressed) {
            var unsupported = compressed.Destinations.FirstOrDefault(d => !Job.Position.SupportsCompression(d.Type));
            if (unsupported != null)
                errors.Add(new ErrorContainer.ErrorData("CompressionLevel", string.Format(Localizer.Localizer.Instance["CompressionNotSupported"],
                    Localizer.Localizer.Instance[$"Fs_{unsupported.Type}"])));
        }

        var ctrl = this.FindControl<Controls.PositionDetail>("Source")!;
        var sValid = ctrl.Validate();
        ctrl = this.FindControl<Controls.PositionDetail>("Destination")!;
        var dValid = ctrl.Validate();
        // Validate every editor, to show all the errors
        var moreValid = MoreDestinationEditors.Select(d => d.Validate()).ToList().All(v => v);

        _model.Errors = errors;
        return sValid && dValid && moreValid && errors.Count == 0;
    }
}