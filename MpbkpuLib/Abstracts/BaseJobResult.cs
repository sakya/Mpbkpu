namespace MpbkpuLib.Abstracts;

public abstract class BaseJobResult
{
    public DateTime? Started { get; set; }
    public DateTime? Completed { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}