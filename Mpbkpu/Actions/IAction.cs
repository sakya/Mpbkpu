namespace Mpbkpu.Actions;

public interface IAction
{
    DateTime? Started { get; set; }
    DateTime? Completed { get; set; }

    Task<bool> Run(Arguments pArgs);
}