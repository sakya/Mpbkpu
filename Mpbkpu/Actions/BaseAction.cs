using Microsoft.Graph;
using MpbkpuLib;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Utility;

namespace Mpbkpu.Actions;

public abstract class BaseAction : IAction
{
    public abstract Task<bool> Run(Arguments pArgs);

    protected async Task<BackupFile>? OpenBackup(string backupPath)
    {
        var sourceFm = FileManagerFactory.GetFileManager(backupPath, out var newPath);
        var backupDbPath = sourceFm.CombinePath(newPath, BackupFile.DefaultDatabaseFileName);
        if (await sourceFm.FileExists(backupDbPath)) {
            if (sourceFm.CanOpenDatabase()) {
                var backupFile = new BackupFile();
                await backupFile.Open(backupDbPath);
                return backupFile;
            } else {
                // Copy database to temp file
                var tempDbFile = TempFile.GetTempFileName(String.Empty);
                var tempFm = FileManagerFactory.GetFileManager(tempDbFile, out _);
                using (var sourceStream = await sourceFm.OpenFile(backupDbPath)) {
                    using (var tempStream = await tempFm.OpenFile(tempDbFile, true)) {
                        byte[] buffer = new byte[4096];
                        int read;
                        while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                            await tempStream.WriteAsync(buffer, 0, read);
                        }
                    }
                }
                var backupFile = new BackupFile();
                await backupFile.Open(tempDbFile);
                return backupFile;
            }
        } else {
            throw new Exception("Database file not found");
        }
    }
}