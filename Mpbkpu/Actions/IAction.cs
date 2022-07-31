namespace Mpbkpu.Actions;

public interface IAction
{
    Task<bool> Run(Arguments pArgs);
}