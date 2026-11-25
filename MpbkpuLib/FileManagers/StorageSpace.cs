namespace MpbkpuLib.FileManagers;

/// <summary>
/// Space of a file system or of a cloud account
/// </summary>
/// <param name="Available">Bytes available for writing</param>
/// <param name="Total">Total bytes, null if unknown</param>
public record StorageSpace(long Available, long? Total)
{
    /// <summary>
    /// Percentage of the space in use, null if the total is unknown
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public double? UsedPercentage => Total > 0 ? Math.Clamp((Total.Value - Available) * 100.0 / Total.Value, 0, 100) : null;
}
