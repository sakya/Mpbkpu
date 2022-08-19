namespace MpbkpuLib.Abstracts;

public abstract class BaseJobResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}