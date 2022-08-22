namespace Mpbkpu.Actions;

public interface IAction
{
    bool UserCancelled();
    DateTime? Started { get; set; }
    DateTime? Completed { get; set; }
    long? Bytes { get; set; }

    Task<bool> Run(Arguments pArgs);
    void Cancel();
}