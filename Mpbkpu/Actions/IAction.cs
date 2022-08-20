namespace Mpbkpu.Actions;

public interface IAction
{
    DateTime? Started { get; set; }
    DateTime? Completed { get; set; }
    long? Bytes { get; set; }

    Task<bool> Run(Arguments pArgs);
}