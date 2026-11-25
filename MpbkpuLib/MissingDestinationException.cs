namespace MpbkpuLib;

/// <summary>
/// Raised when a backup volume is stored in a destination that has not been given to the job
/// </summary>
public class MissingDestinationException : Exception
{
    public MissingDestinationException(string? path) :
        base($"The destination {path} holding part of the backup is missing")
    {
        Path = path;
    }

    /// <summary>
    /// The path of the missing destination as recorded in the database (credentials masked)
    /// </summary>
    public string? Path { get; }
}
