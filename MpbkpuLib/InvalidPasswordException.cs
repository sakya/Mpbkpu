namespace MpbkpuLib;

/// <summary>
/// The password of an encrypted backup is wrong (or the database is corrupted)
/// </summary>
public class InvalidPasswordException : Exception
{
    public InvalidPasswordException(string message) : base(message)
    {
    }
}
