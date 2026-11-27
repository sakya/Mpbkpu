using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MpbkpuGUI.Dialogs;
using MpbkpuGUI.Utility;
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
        ctrl.TypeChanged += (_, _) => UpdateExcludeBrowse();
        UpdateExcludeBrowse();
        foreach (var exclusion in _job.Exclude)
            AddExclusion(exclusion);
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

        var cmbVerify = this.FindControl<ComboBox>("CmbVerify")!;
        cmbVerify.ItemsSource = Enum.GetValues<MpbkpuLib.Jobs.BackupJobSettings.VerifyModes>();

        InitializeSchedule();

        if (_job is BackupJob backupJob) {
            cmbType.SelectedItem = backupJob.Type;
            cmbCompression.SelectedItem = backupJob.CompressionLevel;
            cmbVerify.SelectedItem = backupJob.Verify;
            foreach (var destination in backupJob.MoreDestinations)
                AddMoreDestination(destination);
        }
    }

    /// <summary>
    /// The schedule editor: hidden when the operating system has no supported scheduler
    /// </summary>
    private void InitializeSchedule()
    {
        this.FindControl<Control>("ScheduleCard")!.IsVisible = JobScheduler.Current != null;
        var schedule = _job.Schedule ?? new JobSchedule();
        var culture = new CultureInfo(App.Settings!.Language);
        var days = this.FindControl<WrapPanel>("ScheduleDays")!;
        foreach (var day in JobSchedule.WeekDays) {
            var button = new ToggleButton()
            {
                Content = culture.DateTimeFormat.GetAbbreviatedDayName(day),
                Tag = day,
                IsChecked = schedule.Days.Contains(day),
                MinWidth = 52
            };
            AutomationProperties.SetName(button, culture.DateTimeFormat.GetDayName(day));
            button.IsCheckedChanged += (_, _) => UpdateNextRun();
            days.Children.Add(button);
        }

        var enabled = this.FindControl<ToggleSwitch>("ScheduleEnabled")!;
        enabled.IsChecked = schedule.Enabled;
        enabled.IsCheckedChanged += (_, _) => UpdateNextRun();
        var time = this.FindControl<TimePicker>("ScheduleTime")!;
        time.SelectedTime = schedule.Time;
        time.SelectedTimeChanged += (_, _) => UpdateNextRun();
        UpdateNextRun();
    }

    /// <summary>
    /// The schedule as edited
    /// </summary>
    private JobSchedule EditedSchedule => new()
    {
        Enabled = this.FindControl<ToggleSwitch>("ScheduleEnabled")!.IsChecked == true,
        Time = this.FindControl<TimePicker>("ScheduleTime")!.SelectedTime ?? new JobSchedule().Time,
        Days = this.FindControl<WrapPanel>("ScheduleDays")!.Children.OfType<ToggleButton>()
            .Where(b => b.IsChecked == true).Select(b => (DayOfWeek)b.Tag!).ToList()
    };

    private void UpdateNextRun()
    {
        var next = EditedSchedule.NextRun(DateTime.Now);
        var text = this.FindControl<TextBlock>("ScheduleNext")!;
        text.Text = next == null ? null :
            string.Format(Localizer.Localizer.Instance["NextRun"], next.Value.ToString("f", new CultureInfo(App.Settings!.Language)));
    }

    /// <summary>
    /// An encrypted backup runs by itself only with a saved password: ask it now (it is saved with the settings)
    /// </summary>
    /// <returns>False when the user cancelled or the password cannot be saved</returns>
    private async Task<bool> EnsureScheduledPassword()
    {
        if (_job.Schedule is not { Enabled: true } || _job is not BackupJob { Encrypted: true } backup || backup.HasSavedPassword)
            return true;
        if (!App.Settings!.SecretStoreAvailable) {
            await MessageDialog.ShowMessage(App.GetMainWindow(), Localizer.Localizer.Instance["SchedulePasswordUnavailable"],
                MessageDialog.Icons.Error);
            return false;
        }
        var res = await new PasswordDialog(false, Localizer.Localizer.Instance["SchedulePassword"]).Show<PasswordDialog.Result?>();
        if (res == null)
            return false;
        backup.Password = res.Password;
        backup.RememberPassword = true;
        return true;
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
        AddRemovableRow("MoreDestinationsList", new PositionDetail() { Value = position }, "RemoveDestination", VerticalAlignment.Top);
    }

    /// <summary>
    /// Add an editor to a list, with a button on its right that removes it
    /// </summary>
    /// <param name="listName">Name of the StackPanel holding the rows</param>
    /// <param name="editor">The editor of the row</param>
    /// <param name="removeTextKey">Localized text of the remove button (tooltip and automation name)</param>
    private void AddRemovableRow(string listName, Control editor, string removeTextKey, VerticalAlignment alignment)
    {
        var list = this.FindControl<StackPanel>(listName)!;
        var removeText = Localizer.Localizer.Instance[removeTextKey];
        var remove = new Button()
        {
            Content = new MaterialIcon() { Kind = MaterialIconKind.DeleteOutline },
            VerticalAlignment = alignment,
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
            Children = { editor, remove }
        };
        remove.Click += (_, _) => list.Children.Remove(item);
        list.Children.Add(item);
    }

    private void OnAddDestinationClick(object? sender, RoutedEventArgs e)
    {
        AddMoreDestination(new Job.Position());
    }

    /// <summary>
    /// The text boxes of the exclusions
    /// </summary>
    private IEnumerable<TextBox> ExclusionEditors =>
        this.FindControl<StackPanel>("ExcludeList")!.Children.OfType<Grid>()
            .Select(g => g.Children.OfType<TextBox>().First());

    private TextBox AddExclusion(string value)
    {
        var editor = new TextBox() { Text = value };
        AutomationProperties.SetName(editor, Localizer.Localizer.Instance["Exclude"]);
        AddRemovableRow("ExcludeList", editor, "RemoveExclusion", VerticalAlignment.Center);
        return editor;
    }

    private void OnAddExclusionClick(object? sender, RoutedEventArgs e)
    {
        AddExclusion(string.Empty).Focus();
    }

    private void UpdateExcludeBrowse()
    {
        this.FindControl<Control>("ExcludeBrowse")!.IsVisible = this.FindControl<PositionDetail>("Source")!.IsLocalFileSystem;
    }

    private async void OnExcludeFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await App.GetMainWindow()!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
        {
            Title = Localizer.Localizer.Instance["ExcludeFolder"],
            AllowMultiple = true,
            SuggestedStartLocation = await GetSourceFolder()
        });
        await AddPickedExclusions(folders);
    }

    private async void OnExcludeFileClick(object? sender, RoutedEventArgs e)
    {
        var files = await App.GetMainWindow()!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = Localizer.Localizer.Instance["ExcludeFile"],
            AllowMultiple = true,
            SuggestedStartLocation = await GetSourceFolder()
        });
        await AddPickedExclusions(files);
    }

    private string? SourcePath => this.FindControl<PositionDetail>("Source")!.Value?.Path;

    private async Task<IStorageFolder?> GetSourceFolder()
    {
        return string.IsNullOrEmpty(SourcePath) ? null :
            await App.GetMainWindow()!.StorageProvider.TryGetFolderFromPathAsync(SourcePath);
    }

    /// <summary>
    /// Add the picked items as paths relative to the source; items outside the source are reported
    /// </summary>
    private async Task AddPickedExclusions(IEnumerable<IStorageItem> items)
    {
        var source = SourcePath;
        if (string.IsNullOrEmpty(source))
            return;

        var existing = ExclusionEditors.Select(t => t.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outside = new List<string>();
        foreach (var item in items) {
            var path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
                continue;
            var relative = Path.GetRelativePath(source, path);
            if (relative is "." or ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative)) {
                outside.Add(path);
                continue;
            }
            relative = relative.Replace('\\', '/');
            if (existing.Add(relative))
                AddExclusion(relative);
        }

        if (outside.Count > 0)
            await MessageDialog.ShowMessage(App.GetMainWindow(),
                $"{Localizer.Localizer.Instance["ExclusionOutsideSource"]}\n{string.Join("\n", outside)}", MessageDialog.Icons.Warning);
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
        // Distinct values: the CLI parser drops repeated ones (Copy command)
        _job.Exclude = ExclusionEditors
            .Select(t => t.Text?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (JobScheduler.Current != null) {
            var schedule = EditedSchedule;
            // A job never scheduled keeps no schedule
            if (schedule.Enabled || _job.Schedule != null)
                _job.Schedule = schedule;
        }

        if (!Validate() || !await EnsureScheduledPassword())
            return;

        // The entry of the scheduler first: the settings are not saved when it fails
        try {
            JobScheduler.Current?.Apply(_job);
        } catch (Exception ex) {
            await MessageDialog.ShowMessage(App.GetMainWindow(), $"{Localizer.Localizer.Instance["ScheduleFailed"]}{ex.Message}",
                MessageDialog.Icons.Error);
            return;
        }

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

        if (_job.Schedule is { Enabled: true, Days.Count: 0 })
            errors.Add(new ErrorContainer.ErrorData("Schedule", Localizer.Localizer.Instance["ScheduleNoDays"]));

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