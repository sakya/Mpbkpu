using MpbkpuLib;
using MpbkpuLib.DataModel;

namespace Mpbkpu.Actions;

/// <summary>
/// List backups
/// </summary>
public class ListAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        BackupFile? backupFile;
        try {
            backupFile = await OpenBackup(pArgs.BackupPath);
        } catch (Exception ex) {
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine($"ERROR: {ex.Message}");
            return false;
        }

        Console.ForegroundColor = HighlightColor;
        Console.WriteLine($"{"Guid",-32} {"Type",-12} {"Result",-20} {"Started", -19} {"Completed", -19}");

        Console.ForegroundColor = DefaultColor;
        foreach (var bkp in backupFile.Backups.OrderBy(b => b.Id)) {
            Console.Write($"{bkp.Guid,-32} {bkp.Type,-12}");

            switch (bkp.Result) {
                case Backup.Results.Completed:
                    Console.ForegroundColor = SuccessColor;
                    break;
                case Backup.Results.Cancelled:
                    Console.ForegroundColor = WarningColor;
                    break;
                case Backup.Results.Error:
                    Console.ForegroundColor = ErrorColor;
                    break;
            }
            Console.Write($" {bkp.Result,-20}");
            Console.ForegroundColor = DefaultColor;
            Console.Write($" {$"{bkp.StartedAt!.Value.ToLocalTime().ToShortDateString()} {bkp.StartedAt!.Value.ToLocalTime().ToShortTimeString()}", -19}");
            if (bkp.CompletedAt.HasValue)
                Console.Write($" {$"{bkp.CompletedAt!.Value.ToLocalTime().ToShortDateString()} {bkp.CompletedAt!.Value.ToLocalTime().ToShortTimeString()}", -19}");
            else
                Console.Write($"{ "", -19}");
            Console.WriteLine();
        }
        backupFile.Dispose();
        return true;
    }
}