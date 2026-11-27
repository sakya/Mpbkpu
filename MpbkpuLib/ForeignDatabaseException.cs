namespace MpbkpuLib;

/// <summary>
/// Raised when the destinations of a job hold the databases of different backups: a destination has a database but
/// the newest copy does not record it (every copy records the destinations of its backup)
/// </summary>
public class ForeignDatabaseException : Exception
{
    /// <param name="databasePath">The destination the newest database was read from</param>
    /// <param name="path">A destination with a database of another backup</param>
    public ForeignDatabaseException(string? databasePath, string? path) :
        base($"The destinations {databasePath} and {path} hold the databases of different backups: remove the wrong one " +
             "from the job or use an empty folder")
    {
        DatabasePath = databasePath;
        Path = path;
    }

    /// <summary>
    /// The destination the newest database was read from (credentials masked)
    /// </summary>
    public string? DatabasePath { get; }

    /// <summary>
    /// The destination with a database the newest one does not record (credentials masked)
    /// </summary>
    public string? Path { get; }
}
