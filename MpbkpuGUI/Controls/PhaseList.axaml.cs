using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MpbkpuGUI.Models;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Controls;

/// <summary>
/// The phases of a running job with their state and duration, and the overall progress as one segment per phase.
/// Phases the job jumps over, or does not reach in a completed run, are "not needed".
/// </summary>
public partial class PhaseList : UserControl
{
    private IReadOnlyList<JobPhase> _phases = [];
    private JobProgressModel? _model;

    public PhaseList()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Show the phases of a job about to start; the model gets the phase text, the step and whether the current phase
    /// reports its progress
    /// </summary>
    public void SetPhases(JobProgressModel model, IReadOnlyList<JobPhase> phases)
    {
        _model = model;
        _phases = phases;
        this.FindControl<ItemsControl>("List")!.ItemsSource = phases;

        var segments = this.FindControl<Grid>("Segments")!;
        for (var i = 0; i < phases.Count; i++) {
            segments.ColumnDefinitions.Add(new ColumnDefinition(phases[i].Weight, GridUnitType.Star));
            var segment = new Border() { Margin = new Thickness(0, 0, i < phases.Count - 1 ? 3 : 0, 0) };
            segment.Classes.Add("segment");
            Grid.SetColumn(segment, i);
            segments.Children.Add(segment);
        }
    }

    /// <summary>
    /// A step of the job started (raised by the job thread)
    /// </summary>
    /// <param name="key">The name of the library step</param>
    public void OnStepStarted(string key)
    {
        var phase = _phases.FirstOrDefault(p => p.Key == key);
        if (_model != null) {
            _model.Step = Localizer.Localizer.Instance[$"Step_{key}"];
            _model.Progress = 0;
            _model.PhaseHasProgress = phase?.HasProgress == true;
        }
        if (phase == null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            var now = DateTime.Now;
            var index = _phases.ToList().IndexOf(phase);
            // The phases before this one ended, or were not needed
            foreach (var previous in _phases.Take(index)) {
                if (previous.State == JobPhase.States.Running)
                    EndPhase(previous, JobPhase.States.Done, now);
                else if (previous.State == JobPhase.States.Pending)
                    SkipPhase(previous);
            }
            phase.State = JobPhase.States.Running;
            phase.Started = now;
            phase.Detail = Localizer.Localizer.Instance["PhaseRunning"];
            if (_model != null)
                _model.PhaseText = string.Format(Localizer.Localizer.Instance["PhaseOf"], index + 1, _phases.Count);
            UpdateSegments();
        });
    }

    /// <summary>
    /// The job ended (UI thread): the running phase gets the result, the phases not reached by a completed job were not
    /// needed
    /// </summary>
    public void Finish(Backup.Results result)
    {
        var completed = result is Backup.Results.Completed or Backup.Results.CompletedWithWarnings;
        var now = DateTime.Now;
        foreach (var phase in _phases) {
            if (phase.State == JobPhase.States.Running) {
                EndPhase(phase, completed ? JobPhase.States.Done :
                    result == Backup.Results.Cancelled ? JobPhase.States.Cancelled : JobPhase.States.Failed, now);
            } else if (phase.State == JobPhase.States.Pending && completed) {
                SkipPhase(phase);
            }
        }
        UpdateSegments();

        if (_model == null)
            return;
        _model.PhaseText = null;
        _model.Step = Localizer.Localizer.Instance[$"Result_{result}"];
        if (completed)
            _model.Progress = 100;
        _model.PhaseHasProgress = completed;
    }

    private static void EndPhase(JobPhase phase, JobPhase.States state, DateTime now)
    {
        phase.State = state;
        var duration = now - (phase.Started ?? now);
        phase.Detail = duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
    }

    private static void SkipPhase(JobPhase phase)
    {
        phase.State = JobPhase.States.Skipped;
        phase.Detail = Localizer.Localizer.Instance["PhaseSkipped"];
    }

    private void UpdateSegments()
    {
        var segments = this.FindControl<Grid>("Segments")!.Children;
        for (var i = 0; i < segments.Count; i++) {
            var phase = _phases[i];
            segments[i].Classes.Set("done", phase.State is JobPhase.States.Done or JobPhase.States.Skipped);
            segments[i].Classes.Set("running", phase.IsRunning);
            segments[i].Classes.Set("failed", phase.IsFailed);
        }
    }
}
