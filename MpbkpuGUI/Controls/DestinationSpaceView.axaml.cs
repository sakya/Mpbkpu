using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Dialogs;

namespace MpbkpuGUI.Controls;

/// <summary>
/// The space of a destination of a job, with a button to read it again for remote destinations
/// </summary>
public partial class DestinationSpaceView : UserControl
{
    private readonly Job.Position _position;

    public DestinationSpaceView()
    {
        _position = new Job.Position();
        AvaloniaXamlLoader.Load(this);
    }

    /// <param name="position">The destination</param>
    /// <param name="showPath">Show the path of the destination (when the job has more than one)</param>
    public DestinationSpaceView(Job.Position position, bool showPath)
    {
        _position = position;
        AvaloniaXamlLoader.Load(this);

        var path = this.FindControl<TextBlock>("PathText")!;
        path.Text = position.FullPath;
        path.IsVisible = showPath;
        this.FindControl<Button>("SpaceRefresh")!.IsVisible = !position.IsLocal;
        ShowSpace();
        // Local destinations are cheap to read: always show the current space
        if (position.IsLocal)
            _ = RefreshSpace(false);
    }

    private async void OnRefreshSpaceClick(object? sender, RoutedEventArgs e)
    {
        await RefreshSpace(true);
    }

    /// <summary>
    /// Read the space of the destination, save it in the job and show it
    /// </summary>
    /// <param name="showErrors">Show a message if the space cannot be read</param>
    private async Task RefreshSpace(bool showErrors)
    {
        var button = this.FindControl<Button>("SpaceRefresh")!;
        button.IsEnabled = false;
        try {
            var space = await Task.Run(async () =>
            {
                using var fm = _position.GetFileManager(out var path);
                return await fm.GetSpace(path);
            });
            _position.Space = space;
            _position.SpaceRead = DateTime.Now;
            if (!_position.IsLocal)
                await App.Settings!.Save(App.SettingFile!);
        } catch (Exception ex) {
            if (showErrors) {
                await MessageDialog.ShowMessage(App.GetMainWindow(),
                    $"{Localizer.Localizer.Instance["AnErrorOccurred"]}{ex.Message}", MessageDialog.Icons.Error);
            }
        } finally {
            button.IsEnabled = true;
        }

        ShowSpace();
    }

    private void ShowSpace()
    {
        var text = this.FindControl<TextBlock>("SpaceText")!;
        SpaceDisplay.Show(_position.Space, this.FindControl<ProgressBar>("SpaceBar")!, text);
        ToolTip.SetTip(text, _position.SpaceRead == null ? null :
            string.Format(Localizer.Localizer.Instance["SpaceReadAt"], _position.SpaceRead.Value.ToString("g")));
    }
}
