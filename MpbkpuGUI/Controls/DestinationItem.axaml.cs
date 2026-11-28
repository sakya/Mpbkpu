using System;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Material.Icons;
using Material.Icons.Avalonia;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Controls;

/// <summary>
/// A destination in the destination list of the job settings: a header (number, type, path, free space) that opens
/// its <see cref="PositionDetail"/>. The page keeps one destination open at a time.
/// </summary>
public partial class DestinationItem : UserControl
{
    public DestinationItem()
    {
        AvaloniaXamlLoader.Load(this);
        IsExpanded = false;
    }

    /// <summary>
    /// The header of a closed destination was clicked
    /// </summary>
    public event EventHandler? ExpandRequested;

    /// <summary>
    /// The remove button was clicked
    /// </summary>
    public event EventHandler? RemoveRequested;

    private PositionDetail Editor => this.FindControl<PositionDetail>("Editor")!;

    public Job.Position? Value
    {
        get => Editor.Value;
        set
        {
            Editor.Value = value;
            UpdateHeader();
        }
    }

    private int _number;
    /// <summary>
    /// Position of the destination in the list, from 1
    /// </summary>
    public int Number
    {
        get => _number;
        set
        {
            _number = value;
            this.FindControl<TextBlock>("Number")!.Text = value.ToString();
            UpdateHeader();
        }
    }

    /// <summary>
    /// False for the only destination of a job (or of a synchronization)
    /// </summary>
    public bool Removable
    {
        set => this.FindControl<Button>("Remove")!.IsVisible = value;
    }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            this.FindControl<Control>("Body")!.IsVisible = value;
            this.FindControl<Border>("Frame")!.Classes.Set("expanded", value);
            this.FindControl<MaterialIcon>("Chevron")!.Kind = value ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown;
            // The header shows what was edited
            if (!value)
                UpdateHeader();
        }
    }

    /// <summary>
    /// Validate the editor, opening the destination to show its errors
    /// </summary>
    public bool Validate()
    {
        var valid = Editor.Validate();
        if (!valid)
            IsExpanded = true;
        return valid;
    }

    private void UpdateHeader()
    {
        var value = Editor.Value;
        var localizer = Localizer.Localizer.Instance;
        var path = string.IsNullOrEmpty(value?.Path) ? localizer["NewDestination"] : value.FullPath;
        this.FindControl<TextBlock>("PathText")!.Text = path;
        this.FindControl<TextBlock>("SpaceText")!.Text = SpaceDisplay.FreeText(value?.Space);
        this.FindControl<MaterialIcon>("TypeIcon")!.Kind = value?.Type switch
        {
            Job.Position.FileSystemTypes.FileSystem or null => MaterialIconKind.FolderOutline,
            Job.Position.FileSystemTypes.Ftp or Job.Position.FileSystemTypes.Sftp or Job.Position.FileSystemTypes.Smb =>
                MaterialIconKind.Server,
            _ => MaterialIconKind.CloudOutline
        };
        AutomationProperties.SetName(this.FindControl<Button>("Header")!, $"{localizer["Destination"]} {Number}: {path}");
    }

    private void OnHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (IsExpanded)
            IsExpanded = false;
        else
            ExpandRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }
}
