namespace MpbkpuLib.Abstracts;

public abstract class BaseJobSettings
{
    public enum LogLevels
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public bool LogToConsole { get; set; }
    public string? LogFile { get; set; }
    public LogLevels LogLevel { get; set; }

    /// <summary>
    /// The password used to encrypt/decrypt the files
    /// </summary>
    public string? EncryptPassword { get; set; }
    public bool ShouldSerializeEncryptPassword()
    {
        return false;
    }
}