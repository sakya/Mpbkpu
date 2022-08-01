using Microsoft.Graph;
using MpbkpuLib;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Utility;

namespace Mpbkpu.Actions;

public abstract class BaseAction : IAction
{
    protected const ConsoleColor DefaultColor = ConsoleColor.Gray;
    protected const ConsoleColor HighlightColor = ConsoleColor.White;

    protected const ConsoleColor InfoColor = ConsoleColor.Blue;
    protected const ConsoleColor SuccessColor = ConsoleColor.Green;
    protected const ConsoleColor WarningColor = ConsoleColor.Yellow;
    protected const ConsoleColor ErrorColor = ConsoleColor.Red;

    protected class BackupDatabase : IDisposable
    {
        private readonly string? _tempFile;
        public BackupDatabase(BackupFile backup, string? tempFile = null)
        {
            BackupFile = backup;
            _tempFile = tempFile;
        }

        public BackupFile BackupFile { get; private set; }

        public void Dispose()
        {
            if (!string.IsNullOrEmpty(_tempFile)) {
                var fm = new MpbkpuLib.FileManagers.FileSystem();
                fm.DeleteFile(_tempFile).Wait();
            }
        }
    }

    public abstract Task<bool> Run(Arguments pArgs);

    protected async Task<BackupDatabase?> OpenBackup(string? backupPath)
    {
        if (string.IsNullOrEmpty(backupPath))
            throw new ArgumentException($"{nameof(backupPath)} cannot be empty", nameof(backupPath));

        BackupDatabase? backupFile;
        try {
            backupFile = await OpenBackupPrimitive(backupPath);
        } catch (Exception ex) {
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine($"ERROR: {ex.Message}");
            return null;
        }

        return backupFile;
    }

    private async Task<BackupDatabase?> OpenBackupPrimitive(string backupPath)
    {
        var sourceFm = FileManagerFactory.GetFileManager(backupPath, out var newPath);
        if (!await sourceFm.DirectoryExists(backupPath))
            throw new Exception($"Directory {backupPath} does not exists");

        var backupDbPath = sourceFm.CombinePath(newPath, BackupFile.DefaultDatabaseFileName);
        if (await sourceFm.FileExists(backupDbPath)) {
            if (sourceFm.CanOpenDatabase()) {
                var backupFile = new BackupFile();
                await backupFile.Open(backupDbPath);
                return new BackupDatabase(backupFile);
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
                return new BackupDatabase(backupFile, tempDbFile);
            }
        }

        throw new Exception($"Database file not found in {backupPath}");
    }
}