using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dropbox.Api.Team;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Pages;

public partial class JobRunningPage : BasePage
{
    private readonly Job _job;

    public JobRunningPage()
    {
        _job = new BackupJob();
        InitializeComponent();
    }

    public JobRunningPage(Job job)
    {
        _job = job;
        DataContext = _job;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        PageTitle = $"{Localizer.Localizer.Instance["JobRunning"]}: {_job.Name}";
        NavigateBackWithKeyboard = false;
    }
}