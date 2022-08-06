using MpbkpuLib.DataModel;

namespace Mpbkpu.Actions;

/// <summary>
/// List backups
/// </summary>
public class ListAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        using (var backupDb = await OpenBackup(pArgs.BackupPath, pArgs.Password)) {
            if (backupDb == null)
                return false;

            var backupFile = backupDb.BackupFile;

            Console.WriteLine();
            Console.ForegroundColor = HighlightColor;
            Console.WriteLine($"{"Guid",-32} {"Type",-12} {"Result",-20} {"Started",-19} {"Completed",-19} Size");

            Console.ForegroundColor = DefaultColor;
            var backups = backupFile.Backups.OrderBy(b => b.Id)
                .Select(b => new
                {
                    Backup = b,
                    Files = b.Items.Count(),
                    Bytes = b.Items.Sum(i => i.OriginalSize)
                });

            foreach (var bkp in backups.OrderBy(b => b.Backup.Id)) {
                Console.Write($"{bkp.Backup.Guid,-32} {bkp.Backup.Type,-12}");

                switch (bkp.Backup.Result) {
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

                Console.Write($" {bkp.Backup.Result,-20}");
                Console.ForegroundColor = DefaultColor;
                Console.Write(
                    $" {$"{bkp.Backup.StartedAt!.Value.ToLocalTime().ToShortDateString()} {bkp.Backup.StartedAt!.Value.ToLocalTime().ToShortTimeString()}",-19}");
                if (bkp.Backup.CompletedAt.HasValue)
                    Console.Write(
                        $" {$"{bkp.Backup.CompletedAt!.Value.ToLocalTime().ToShortDateString()} {bkp.Backup.CompletedAt!.Value.ToLocalTime().ToShortTimeString()}",-19}");
                else
                    Console.Write($"{"",-19}");
                Console.Write($" {MpbkpuLib.Utility.String.BytesToString(bkp.Bytes)}");
                Console.WriteLine();
            }
        }

        return true;
    }
}