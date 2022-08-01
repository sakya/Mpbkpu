using Microsoft.EntityFrameworkCore;
using MpbkpuLib;

namespace Mpbkpu.Actions;

public class DeleteAction : BaseAction
{
    public override async Task<bool> Run(Arguments pArgs)
    {
        using (BackupDatabase? backupDb = await OpenBackup(pArgs.BackupPath)) {
            if (backupDb == null)
                return false;

            var backupFile = backupDb.BackupFile;
            var bkp = await backupFile.Backups.AsNoTracking()
                .Where(b => b.Guid == pArgs.BackupGuid)
                .FirstOrDefaultAsync();
            if (bkp == null) {
                Console.ForegroundColor = ErrorColor;
                Console.WriteLine($"ERROR: Backup with GUID {pArgs.BackupGuid} not found");
                return false;
            }
        }

        return true;
    }
}