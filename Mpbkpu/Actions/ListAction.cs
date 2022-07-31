using MpbkpuLib;

namespace Mpbkpu.Actions;

/// <summary>
/// List backups
/// </summary>
public class ListAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        var backupFile = await OpenBackup(pArgs.BackupPath);

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{"Guid",-32} {"Type",-15} {"Result",-20} Started");

        Console.ForegroundColor = ConsoleColor.Gray;
        foreach (var bkp in backupFile.Backups.OrderBy(b => b.Id)) {
            Console.WriteLine($"{bkp.Guid,-32} {bkp.Type,-15} {bkp.Result,-20} {bkp.StartedAt!.Value.ToShortDateString()} {bkp.StartedAt!.Value.ToShortTimeString()}");
        }
        return true;
    }
}