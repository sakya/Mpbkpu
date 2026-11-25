using Newtonsoft.Json;
using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Jobs;

/// <summary>
/// A destination of a backup. The files of a backup are split among its destinations and every destination
/// holds a copy of the database.
/// </summary>
public class BackupDestination
{
    /// <summary>
    /// Stable identifier of the destination: the database uses it to find the destination of each volume,
    /// so it must not change when the destination is edited or moved in the list
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The destination path
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// The user to access to the destination
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// The password to access to the destination
    /// </summary>
    [JsonIgnore] // Never logged or saved in the backup database
    public string? Password { get; set; }

    /// <summary>
    /// OAuth settings to access to the destination
    /// </summary>
    public OAuth2Settings? OauthSettings { get; set; }
}
