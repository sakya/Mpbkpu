namespace MpbkpuLib.Abstracts;

public abstract class BaseJobResult
{
    /// <summary>
    /// Job started date time
    /// </summary>
    public DateTime? Started { get; set; }

    /// <summary>
    /// Job completed date time
    /// </summary>
    public DateTime? Completed { get; set; }

    /// <summary>
    /// Total bytes elaborated by the job
    /// </summary>
    public long? Bytes { get; set; }

    /// <summary>
    /// Job result
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Job error message
    /// </summary>
    public string? ErrorMessage { get; set; }
}