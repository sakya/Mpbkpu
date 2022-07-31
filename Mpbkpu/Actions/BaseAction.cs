using Microsoft.Graph;
using MpbkpuLib;
using MpbkpuLib.FileManagers;

namespace Mpbkpu.Actions;

public abstract class BaseAction : IAction
{
    public abstract Task<bool> Run(Arguments pArgs);

    protected async Task<BackupFile>? OpenBackup(string backupPath)
    {
        var fm = FileManagerFactory.GetFileManager(backupPath, out var newPath);
        var backupDbPath = fm.CombinePath(newPath, BackupFile.DefaultDatabaseFileName);
        if (await fm.FileExists(backupDbPath)) {
            if (fm.CanOpenDatabase()) {
                var backupFile = new BackupFile();
                await backupFile.Open(backupDbPath);
                return backupFile;
            }
        }

        return null;
    }
}