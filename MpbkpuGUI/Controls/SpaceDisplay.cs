using Avalonia.Controls;
using MpbkpuLib.FileManagers;

namespace MpbkpuGUI.Controls;

/// <summary>
/// Shows a <see cref="StorageSpace"/> with a bar (used space) and a text, the same way in every page
/// </summary>
public static class SpaceDisplay
{
    /// <summary>
    /// Used percentage from which the bar is shown as a warning
    /// </summary>
    private const double WarningPercentage = 80;
    /// <summary>
    /// Used percentage from which the bar is shown as a danger
    /// </summary>
    private const double DangerPercentage = 95;

    public static void Show(StorageSpace? space, ProgressBar bar, TextBlock text)
    {
        var used = space?.UsedPercentage;
        bar.IsVisible = used.HasValue;
        bar.Value = used ?? 0;
        bar.Classes.Set("warning", used is >= WarningPercentage and < DangerPercentage);
        bar.Classes.Set("danger", used >= DangerPercentage);

        var localizer = Localizer.Localizer.Instance;
        if (space == null) {
            text.Text = localizer["SpaceUnknown"];
        } else if (space.Total.HasValue) {
            text.Text = string.Format(localizer["FreeSpace"], MpbkpuLib.Utility.String.BytesToString(space.Available),
                MpbkpuLib.Utility.String.BytesToString(space.Total.Value));
        } else {
            text.Text = FreeText(space);
        }
    }

    /// <summary>
    /// "N free", null when the space is not known
    /// </summary>
    public static string? FreeText(StorageSpace? space)
    {
        return space == null ? null :
            string.Format(Localizer.Localizer.Instance["FreeSpaceNoTotal"], MpbkpuLib.Utility.String.BytesToString(space.Available));
    }
}
