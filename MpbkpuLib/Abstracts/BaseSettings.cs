namespace MpbkpuLib.Abstracts;

public abstract class BaseSettings
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
}