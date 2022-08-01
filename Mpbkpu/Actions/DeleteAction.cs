using Microsoft.EntityFrameworkCore;
using MpbkpuLib;

namespace Mpbkpu.Actions;

public class DeleteAction : BaseAction
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

        var bkp = await backupFile.Backups.AsNoTracking()
            .Where(b => b.Guid == pArgs.BackupGuid)
            .FirstOrDefaultAsync();
        if (bkp == null) {
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine($"ERROR: Backup with GUID {pArgs.BackupGuid} not found");
            return false;
        }

        return true;
    }
}