using Microsoft.Graph;
using MpbkpuLib;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Utility;

namespace Mpbkpu.Actions;

public abstract class BaseAction : IAction
{
    protected const ConsoleColor DefaultColor = ConsoleColor.Gray;
    protected const ConsoleColor HighlightColor = ConsoleColor.White;

    protected const ConsoleColor SuccessColor = ConsoleColor.Green;
    protected const ConsoleColor WarningColor = ConsoleColor.Yellow;
    protected const ConsoleColor ErrorColor = ConsoleColor.Red;

    public abstract Task<bool> Run(Arguments pArgs);

    protected async Task<BackupFile>? OpenBackup(string backupPath)
    {
        var sourceFm = FileManagerFactory.GetFileManager(backupPath, out var newPath);
        if (!await sourceFm.DirectoryExists(backupPath))
            throw new Exception($"Directory {backupPath} does not exists");

        var backupDbPath = sourceFm.CombinePath(newPath, BackupFile.DefaultDatabaseFileName);
        if (await sourceFm.FileExists(backupDbPath)) {
            if (sourceFm.CanOpenDatabase()) {
                var backupFile = new BackupFile();
                await backupFile.Open(backupDbPath);
                return backupFile;
            } else {
                // Copy database to temp file
                var tempDbFile = TempFile.GetTempFileName();
                var tempFm = FileManagerFactory.GetFileManager(tempDbFile, out _);
                using (var sourceStream = await sourceFm.OpenFile(backupDbPath)) {
                    using (var tempStream = await tempFm.OpenFile(tempDbFile, true)) {
                        await Files.Copy(sourceStream, tempStream);
                    }
                }
                var backupFile = new BackupFile();
                await backupFile.Open(tempDbFile);
                return backupFile;
            }
        } else {
            throw new Exception($"Database file not found in {backupPath}");
        }
    }
}